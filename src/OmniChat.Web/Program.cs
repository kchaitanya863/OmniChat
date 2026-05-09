using OmniChat.Core.Models;
using OmniChat.Core.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IChatRepository, InMemoryChatRepository>();
builder.Services.AddSingleton<TextChunker>();
builder.Services.AddSingleton<LocalEmbeddingService>();
builder.Services.AddSingleton<RagIndexer>();
builder.Services.AddSingleton<LocalWebSearchService>();
builder.Services.AddSingleton<AppRuntimeSettings>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/sessions", async (IChatRepository chats, CancellationToken cancellationToken) =>
{
    var sessions = await chats.GetSessionsAsync(cancellationToken);
    return sessions.Select(static s => new
    {
        s.Id,
        s.Title,
        MessageCount = s.Messages.Count,
        LastMessage = s.Messages.LastOrDefault()?.Content
    });
});

app.MapPost("/api/sessions", async (CreateSessionRequest request, IChatRepository chats, AppRuntimeSettings settings, CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        return Results.BadRequest("Request body is required.");
    }

    var session = await chats.CreateSessionAsync(request.Title ?? "New Chat", cancellationToken);

    if (settings.StorageMode == "managed")
    {
        await chats.TrimToLatestSessionsAsync(settings.KeepLatestSessions, cancellationToken);
    }

    return Results.Ok(new { session.Id, session.Title });
});

app.MapGet("/api/sessions/{sessionId}", async (string sessionId, IChatRepository chats, CancellationToken cancellationToken) =>
{
    var session = await chats.GetSessionAsync(sessionId, cancellationToken);
    return session is null ? Results.NotFound() : Results.Ok(session);
});

app.MapPost("/api/sessions/{sessionId}/messages", async (
    string sessionId,
    SendMessageRequest request,
    IChatRepository chats,
    LocalWebSearchService search,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Content))
    {
        return Results.BadRequest("Message content is required.");
    }

    var session = await chats.GetSessionAsync(sessionId, cancellationToken);
    if (session is null)
    {
        return Results.NotFound();
    }

    var userMessage = new ChatMessage("user", request.Content.Trim(), DateTimeOffset.UtcNow, request.ProviderId, request.Model);
    await chats.AddMessageAsync(sessionId, userMessage, cancellationToken);

    var contextForReply = TokenBudgetManager.FitToBudget(session.Messages, 220);
    var evidence = await search.SearchAsync(request.Content, cancellationToken);
    var shortContext = contextForReply.TakeLast(3).Select(static m => $"{m.Role}: {m.Content}");
    var reply = $"You said: {request.Content}\n\nRecent context window:\n- {string.Join("\n- ", shortContext)}\n\nLocal search context:\n- {string.Join("\n- ", evidence.Select(e => $"{e.Title}: {e.Snippet}"))}";

    var assistantMessage = new ChatMessage("assistant", reply, DateTimeOffset.UtcNow, request.ProviderId, request.Model);
    await chats.AddMessageAsync(sessionId, assistantMessage, cancellationToken);

    var budgetedMessages = TokenBudgetManager.FitToBudget(session.Messages, 300);

    return Results.Ok(new
    {
        Reply = assistantMessage,
        ContextWindow = budgetedMessages
    });
});

app.MapPost("/api/rag/index", (IndexRequest request, RagIndexer indexer, AppRuntimeSettings settings) =>
{
    if (request is null ||
        string.IsNullOrWhiteSpace(request.DocumentId) ||
        string.IsNullOrWhiteSpace(request.Content))
    {
        return Results.BadRequest("DocumentId and Content are required.");
    }

    ChunkingSettings chunking;
    try
    {
        chunking = ResolveChunking(
            request.ChunkingStrategy ?? settings.ChunkingStrategy,
            request.MaxWordsPerChunk,
            request.OverlapWords);
    }
    catch (ArgumentOutOfRangeException exception)
    {
        return Results.BadRequest(exception.Message);
    }
    var chunks = indexer.IndexDocument(request.DocumentId, request.Content, chunking.MaxWordsPerChunk, chunking.OverlapWords);
    return Results.Ok(new
    {
        request.DocumentId,
        chunking.ChunkingStrategy,
        chunking.MaxWordsPerChunk,
        chunking.OverlapWords,
        ChunkCount = chunks.Count,
        Preview = chunks.Take(3).Select(static c => c.Content)
    });
});

app.MapGet("/api/settings", (AppRuntimeSettings settings) =>
    Results.Ok(new
    {
        settings.ChunkingStrategy,
        settings.StorageMode,
        settings.KeepLatestSessions
    }));

app.MapPost("/api/settings", (UpdateSettingsRequest request, AppRuntimeSettings settings) =>
{
    if (request is null)
    {
        return Results.BadRequest("Request body is required.");
    }

    if (request.KeepLatestSessions is < 0 or > 1000)
    {
        return Results.BadRequest("KeepLatestSessions must be between 0 and 1000.");
    }

    try
    {
        var resolvedChunking = ResolveChunking(request.ChunkingStrategy ?? settings.ChunkingStrategy, null, null);
        settings.ChunkingStrategy = resolvedChunking.ChunkingStrategy;
        settings.StorageMode = ResolveStorageMode(request.StorageMode ?? settings.StorageMode);
    }
    catch (ArgumentOutOfRangeException exception)
    {
        return Results.BadRequest(exception.Message);
    }

    settings.KeepLatestSessions = request.KeepLatestSessions ?? settings.KeepLatestSessions;

    return Results.Ok(new
    {
        settings.ChunkingStrategy,
        settings.StorageMode,
        settings.KeepLatestSessions
    });
});

app.MapGet("/api/storage/summary", async (IChatRepository chats, AppRuntimeSettings settings, CancellationToken cancellationToken) =>
{
    var sessions = await chats.GetSessionsAsync(cancellationToken);
    var messageCount = sessions.Sum(static s => s.Messages.Count);
    var approximateChars = sessions.Sum(static s => s.Messages.Sum(static m => m.Content.Length));

    return Results.Ok(new
    {
        SessionCount = sessions.Count,
        MessageCount = messageCount,
        ApproximateContentChars = approximateChars,
        settings.StorageMode,
        settings.KeepLatestSessions
    });
});

app.MapPost("/api/storage/cleanup", async (StorageCleanupRequest request, IChatRepository chats, CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        return Results.BadRequest("Request body is required.");
    }

    if (request.Mode is not ("clear_all" or "keep_latest"))
    {
        return Results.BadRequest("Mode must be 'clear_all' or 'keep_latest'.");
    }

    if (request.Mode == "keep_latest" && request.KeepLatestSessions is < 0 or > 1000)
    {
        return Results.BadRequest("KeepLatestSessions must be between 0 and 1000 for keep_latest mode.");
    }

    var removed = request.Mode == "clear_all"
        ? await chats.DeleteAllSessionsAsync(cancellationToken)
        : await chats.TrimToLatestSessionsAsync(request.KeepLatestSessions ?? 0, cancellationToken);

    return Results.Ok(new { RemovedSessions = removed });
});

app.MapPost("/api/rag/retrieve", (RetrieveRequest request, RagIndexer indexer) =>
{
    if (request is null ||
        string.IsNullOrWhiteSpace(request.DocumentId) ||
        string.IsNullOrWhiteSpace(request.DocumentContent) ||
        string.IsNullOrWhiteSpace(request.Query))
    {
        return Results.BadRequest("DocumentId, DocumentContent, and Query are required.");
    }

    if (request.TopK <= 0 || request.TopK > 10)
    {
        return Results.BadRequest("TopK must be between 1 and 10.");
    }

    var indexed = indexer.IndexDocument(request.DocumentId, request.DocumentContent);
    var matches = indexer.RetrieveTopK(request.Query, indexed, request.TopK);

    return Results.Ok(matches.Select(static m => new
    {
        m.DocumentId,
        m.Sequence,
        m.Content
    }));
});

await EnsureSeedDataAsync(app.Services);
await app.RunAsync();

static async Task EnsureSeedDataAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var repository = scope.ServiceProvider.GetRequiredService<IChatRepository>();

    var existing = await repository.GetSessionsAsync(CancellationToken.None);
    if (existing.Count > 0)
    {
        return;
    }

    var session = await repository.CreateSessionAsync("Welcome chat", CancellationToken.None);
    await repository.AddMessageAsync(session.Id, new ChatMessage(
        "assistant",
        "Welcome to OmniChat. This baseline implementation stores session data in local process memory and demonstrates local-first flow patterns.",
        DateTimeOffset.UtcNow), CancellationToken.None);
}

static ChunkingSettings ResolveChunking(string? strategy, int? maxWordsPerChunk, int? overlapWords)
{
    if (maxWordsPerChunk is not null || overlapWords is not null)
    {
        var maxWords = maxWordsPerChunk ?? 120;
        var overlap = overlapWords ?? 20;
        if (maxWords <= 0 || overlap < 0 || overlap >= maxWords)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWordsPerChunk), "Chunk settings are invalid.");
        }

        return new ChunkingSettings("custom", maxWords, overlap);
    }

    return (strategy ?? "balanced").Trim().ToLowerInvariant() switch
    {
        "focused" => new ChunkingSettings("focused", 80, 10),
        "balanced" => new ChunkingSettings("balanced", 120, 20),
        "broad" => new ChunkingSettings("broad", 180, 30),
        _ => throw new ArgumentOutOfRangeException(nameof(strategy), "ChunkingStrategy must be focused, balanced, or broad.")
    };
}

static string ResolveStorageMode(string? storageMode)
{
    if (string.IsNullOrWhiteSpace(storageMode))
    {
        return "ephemeral";
    }

    return storageMode.Trim().ToLowerInvariant() switch
    {
        "ephemeral" => "ephemeral",
        "managed" => "managed",
        _ => throw new ArgumentOutOfRangeException(nameof(storageMode), "StorageMode must be ephemeral or managed.")
    };
}

public sealed record CreateSessionRequest(string? Title);

public sealed record SendMessageRequest(string Content, string? ProviderId, string? Model);

public sealed record IndexRequest(
    string DocumentId,
    string Content,
    string? ChunkingStrategy = null,
    int? MaxWordsPerChunk = null,
    int? OverlapWords = null);

public sealed record RetrieveRequest(string DocumentId, string DocumentContent, string Query, int TopK = 3);

public sealed record UpdateSettingsRequest(string? ChunkingStrategy, string? StorageMode, int? KeepLatestSessions);

public sealed record StorageCleanupRequest(string Mode, int? KeepLatestSessions = null);

public sealed class AppRuntimeSettings
{
    public string ChunkingStrategy { get; set; } = "balanced";
    public string StorageMode { get; set; } = "ephemeral";
    public int KeepLatestSessions { get; set; } = 10;
}

public sealed record ChunkingSettings(string ChunkingStrategy, int MaxWordsPerChunk, int OverlapWords);
