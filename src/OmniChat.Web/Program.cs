using OmniChat.Core.Models;
using OmniChat.Core.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IChatRepository, InMemoryChatRepository>();
builder.Services.AddSingleton<TextChunker>();
builder.Services.AddSingleton<LocalEmbeddingService>();
builder.Services.AddSingleton<RagIndexer>();
builder.Services.AddSingleton<LocalWebSearchService>();

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

app.MapPost("/api/sessions", async (CreateSessionRequest request, IChatRepository chats, CancellationToken cancellationToken) =>
{
    var session = await chats.CreateSessionAsync(request.Title ?? "New Chat", cancellationToken);
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

    var evidence = await search.SearchAsync(request.Content, cancellationToken);
    var reply = $"You said: {request.Content}\n\nLocal search context:\n- {string.Join("\n- ", evidence.Select(e => $"{e.Title}: {e.Snippet}"))}";

    var assistantMessage = new ChatMessage("assistant", reply, DateTimeOffset.UtcNow, request.ProviderId, request.Model);
    await chats.AddMessageAsync(sessionId, assistantMessage, cancellationToken);

    var budgetedMessages = TokenBudgetManager.FitToBudget(session.Messages, 300);

    return Results.Ok(new
    {
        Reply = assistantMessage,
        ContextWindow = budgetedMessages
    });
});

app.MapPost("/api/rag/index", (IndexRequest request, RagIndexer indexer) =>
{
    var chunks = indexer.IndexDocument(request.DocumentId, request.Content);
    return Results.Ok(new
    {
        request.DocumentId,
        ChunkCount = chunks.Count,
        Preview = chunks.Take(3).Select(static c => c.Content)
    });
});

app.MapPost("/api/rag/retrieve", (RetrieveRequest request, RagIndexer indexer) =>
{
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

public sealed record CreateSessionRequest(string? Title);

public sealed record SendMessageRequest(string Content, string? ProviderId, string? Model);

public sealed record IndexRequest(string DocumentId, string Content);

public sealed record RetrieveRequest(string DocumentId, string DocumentContent, string Query, int TopK = 3);
