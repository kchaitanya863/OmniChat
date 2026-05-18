using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OmniChat.Core.Data;
using OmniChat.Core.Models;
using OmniChat.Core.Providers;
using OmniChat.Core.Security;
using OmniChat.Core.Services;
using OmniChat.Web.Security;

var builder = WebApplication.CreateBuilder(args);

// Loopback guard — refuse to bind to non-loopback addresses unless explicitly allowed.
// Prevents accidental exposure to LAN/internet during development.
var allowRemote = builder.Configuration.GetValue<bool>("OmniChat:AllowRemote");
if (!allowRemote)
{
    var urls = builder.Configuration["ASPNETCORE_URLS"] ?? builder.Configuration["urls"];
    if (urls is { Length: > 0 } && !IsAllLoopback(urls))
    {
        Console.Error.WriteLine(
            "[OmniChat] Refusing to start: non-loopback bind detected and OmniChat:AllowRemote is false. " +
            $"Configured URLs: {urls}");
        Environment.Exit(1);
    }
}

static bool IsAllLoopback(string urls)
{
    foreach (var raw in urls.Split(';', StringSplitOptions.RemoveEmptyEntries))
    {
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.ToLowerInvariant();
        var loopback = host is "localhost" or "127.0.0.1" or "[::1]" or "::1";
        if (!loopback) return false;
    }
    return true;
}

// Storage selection: "sqlite" (default) persists to a local SQLite DB; "memory" keeps state in-process (test mode).
var storageMode = (builder.Configuration["OmniChat:Storage"] ?? "sqlite").Trim().ToLowerInvariant();
var dataDir = builder.Configuration["OmniChat:DataDir"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniChat");
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "omnichat.db");

if (storageMode == "sqlite")
{
    builder.Services.AddDbContextFactory<OmniChatDbContext>(options =>
        options.UseSqlite($"Data Source={dbPath}"));
    builder.Services.AddSingleton<IChatRepository, SqliteChatRepository>();
    builder.Services.AddSingleton<IProviderConfigStore, SqliteProviderConfigStore>();
}
else
{
    builder.Services.AddSingleton<IChatRepository, InMemoryChatRepository>();
    builder.Services.AddSingleton<IProviderConfigStore, InMemoryProviderConfigStore>();
}

builder.Services.AddSingleton<TextChunker>();
builder.Services.AddSingleton<LocalEmbeddingService>();

// Embedding selection: if an ONNX model path is configured, prefer the composite that swaps in ONNX when ready.
var modelPath = builder.Configuration["OmniChat:EmbeddingModel:Path"]
    ?? Path.Combine(dataDir, "models", "all-MiniLM-L6-v2.onnx");
builder.Services.AddSingleton(new OnnxEmbeddingService(modelPath));
builder.Services.AddSingleton<IEmbeddingService>(sp =>
    new CompositeEmbeddingService(
        sp.GetRequiredService<OnnxEmbeddingService>(),
        sp.GetRequiredService<LocalEmbeddingService>()));

builder.Services.AddSingleton<RagIndexer>();
builder.Services.AddSingleton<LocalWebSearchService>();
builder.Services.AddSingleton<AppRuntimeSettingsStore>();
builder.Services.AddSingleton<ModelDownloader>();

// Document extraction
builder.Services.AddSingleton<IDocumentExtractor, PlainTextExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, PdfDocumentExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, DocxDocumentExtractor>();
builder.Services.AddSingleton<DocumentExtractorRegistry>();

// Search + personas
builder.Services.AddSingleton<HybridSearchService>();
if (storageMode == "sqlite")
{
    builder.Services.AddSingleton<DocumentService>();
    builder.Services.AddSingleton<IPersonaService, SqlitePersonaService>();
}
else
{
    builder.Services.AddSingleton<IPersonaService, InMemoryPersonaService>();
}

builder.Services.AddDataProtection();
builder.Services.AddSingleton<IKeyVault, DataProtectionKeyVault>();

builder.Services.AddHttpClient("provider")
    .ConfigureHttpClient(static client =>
    {
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniChat/0.1");
    });

builder.Services.AddSingleton<IChatProvider>(sp =>
    new OpenAiChatProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("provider")));
builder.Services.AddSingleton<IChatProvider>(sp =>
    new AnthropicChatProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("provider")));
builder.Services.AddSingleton<IChatProvider>(sp =>
    new AzureOpenAiChatProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("provider")));
builder.Services.AddSingleton<IChatProvider>(sp =>
    new GroqChatProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("provider")));
builder.Services.AddSingleton<IChatProvider, EchoChatProvider>();
builder.Services.AddSingleton<IChatProviderRegistry, ChatProviderRegistry>();

// CORS policy. Default: same-origin only (no explicit policy needed for that, but registering
// the named policy lets ops opt-in to extra origins via OmniChat:AllowedOrigins.
builder.Services.AddCors(options =>
{
    options.AddPolicy("default", policy =>
    {
        var origins = builder.Configuration.GetSection("OmniChat:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        if (origins.Length > 0)
        {
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        }
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("provider-stream", context =>
    {
        var providerKey = context.Request.RouteValues["sessionId"]?.ToString() ?? "anonymous";
        return RateLimitPartition.GetTokenBucketLimiter(providerKey, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 30,
            TokensPerPeriod = 30,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

var app = builder.Build();

app.UseCors("default");
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/sessions", async (IChatRepository chats, CancellationToken cancellationToken) =>
{
    var sessions = await chats.GetSessionsAsync(cancellationToken);
    return sessions.Select(static s => new
    {
        s.Id,
        s.Title,
        s.Pinned,
        s.FolderId,
        MessageCount = s.MessageCount,
        LastMessageContent = s.LastMessage?.Content,
        LastMessageTimestamp = s.LastMessage?.Timestamp
    });
});

app.MapPost("/api/sessions", async (CreateSessionRequest? request, IChatRepository chats, AppRuntimeSettingsStore settings, CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        return Results.BadRequest("Request body is required.");
    }

    var session = await chats.CreateSessionAsync(request.Title ?? "New Chat", cancellationToken);

    var current = settings.Current;
    if (current.StorageMode == "managed")
    {
        await chats.TrimToLatestSessionsAsync(current.KeepLatestSessions, cancellationToken);
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

    if (request.Content.Length > Limits.MaxMessageChars)
    {
        return Results.BadRequest($"Message content exceeds {Limits.MaxMessageChars} characters.");
    }

    var session = await chats.GetSessionAsync(sessionId, cancellationToken);
    if (session is null)
    {
        return Results.NotFound();
    }

    var userMessage = new ChatMessage("user", request.Content.Trim(), DateTimeOffset.UtcNow, request.ProviderId, request.Model);
    await chats.AddMessageAsync(sessionId, userMessage, cancellationToken);

    var allMessages = new List<ChatMessage>(session.Messages) { userMessage };
    var contextForReply = TokenBudgetManager.FitToBudget(allMessages, 220);
    var evidence = await search.SearchAsync(request.Content, cancellationToken);
    var shortContext = contextForReply.TakeLast(3).Select(static m => $"{m.Role}: {m.Content}");
    var reply = $"You said: {request.Content}\n\nRecent context window:\n- {string.Join("\n- ", shortContext)}\n\nLocal search context:\n- {string.Join("\n- ", evidence.Select(e => $"{e.Title}: {e.Snippet}"))}";

    var assistantMessage = new ChatMessage("assistant", reply, DateTimeOffset.UtcNow, request.ProviderId, request.Model);
    await chats.AddMessageAsync(sessionId, assistantMessage, cancellationToken);

    return Results.Ok(new
    {
        UserMessage = userMessage,
        AssistantMessage = assistantMessage
    });
});

app.MapPost("/api/sessions/{sessionId}/stream", async (
    string sessionId,
    StreamMessageRequest request,
    HttpContext httpContext,
    IChatRepository chats,
    IProviderConfigStore providerStore,
    IChatProviderRegistry providerRegistry,
    IKeyVault keyVault,
    CancellationToken cancellationToken) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.Content))
    {
        return Results.BadRequest("Message content is required.");
    }

    if (request.Content.Length > Limits.MaxMessageChars)
    {
        return Results.BadRequest($"Message content exceeds {Limits.MaxMessageChars} characters.");
    }

    var session = await chats.GetSessionAsync(sessionId, cancellationToken);
    if (session is null)
    {
        return Results.NotFound();
    }

    var resolvedProviderId = request.ProviderConfigId;
    ProviderConfig? config = null;
    if (!string.IsNullOrWhiteSpace(resolvedProviderId))
    {
        config = await providerStore.GetAsync(resolvedProviderId, cancellationToken);
        if (config is null)
        {
            return Results.BadRequest($"Provider config '{resolvedProviderId}' not found.");
        }

        if (!config.IsEnabled)
        {
            return Results.BadRequest($"Provider config '{resolvedProviderId}' is disabled.");
        }
    }

    var providerType = config?.ProviderType ?? ProviderType.Custom;
    if (!providerRegistry.TryGet(providerType, out var provider) || provider is null)
    {
        return Results.BadRequest($"No registered driver for provider type '{providerType}'.");
    }

    string? apiKey = null;
    if (config is not null && !string.IsNullOrEmpty(config.ApiKeyCipher))
    {
        apiKey = keyVault.Unseal(config.ApiKeyCipher);
        if (apiKey is null)
        {
            return Results.BadRequest("Stored provider key could not be decrypted on this machine.");
        }
    }

    var userMessage = new ChatMessage(
        "user",
        request.Content.Trim(),
        DateTimeOffset.UtcNow,
        config?.Id,
        request.Model ?? config?.Model);
    await chats.AddMessageAsync(sessionId, userMessage, cancellationToken);

    var allMessages = new List<ChatMessage>(session.Messages) { userMessage };

    var streamRequest = new ChatStreamRequest(
        Messages: allMessages,
        Model: request.Model ?? config?.Model ?? "echo",
        ApiKey: apiKey ?? string.Empty,
        BaseUrl: config?.BaseUrl,
        SystemPrompt: config?.SystemPrompt,
        Temperature: config?.Temperature,
        MaxOutputTokens: config?.MaxOutputTokens);

    var response = httpContext.Response;
    response.Headers.Append("Content-Type", "text/event-stream");
    response.Headers.Append("Cache-Control", "no-cache, no-transform");
    response.Headers.Append("X-Accel-Buffering", "no");
    response.Headers.Append("Connection", "keep-alive");

    var assistantBuffer = new System.Text.StringBuilder();
    UsageStats? finalUsage = null;
    string? finishReason = null;

    try
    {
        await foreach (var chunk in provider.StreamAsync(streamRequest, cancellationToken))
        {
            if (chunk.Delta is { Length: > 0 })
            {
                assistantBuffer.Append(chunk.Delta);
            }

            if (chunk.Usage is not null)
            {
                finalUsage = chunk.Usage;
            }

            if (!string.IsNullOrEmpty(chunk.FinishReason))
            {
                finishReason = chunk.FinishReason;
            }

            await WriteSseEventAsync(response, "delta", chunk, cancellationToken);
        }

        var assistantMessage = new ChatMessage(
            "assistant",
            assistantBuffer.ToString(),
            DateTimeOffset.UtcNow,
            config?.Id,
            request.Model ?? config?.Model);
        await chats.AddMessageAsync(sessionId, assistantMessage, cancellationToken);

        await WriteSseEventAsync(response, "done", new
        {
            AssistantMessage = assistantMessage,
            Usage = finalUsage,
            FinishReason = finishReason
        }, cancellationToken);
    }
    catch (ProviderException ex)
    {
        await WriteSseEventAsync(response, "error", new
        {
            ex.ProviderType,
            StatusCode = (int)ex.StatusCode,
            Message = ex.Message
        }, cancellationToken);
    }
    catch (OperationCanceledException)
    {
        // Client disconnected. Persist the partial assistant text if any.
        if (assistantBuffer.Length > 0)
        {
            var partial = new ChatMessage(
                "assistant",
                assistantBuffer + " [interrupted]",
                DateTimeOffset.UtcNow,
                config?.Id,
                request.Model ?? config?.Model);
            await chats.AddMessageAsync(sessionId, partial, CancellationToken.None);
        }
    }

    return Results.Empty;
}).RequireRateLimiting("provider-stream");

app.MapPost("/api/sessions/{sessionId}/regenerate", async (
    string sessionId,
    RegenerateRequest request,
    HttpContext httpContext,
    IChatRepository chats,
    IProviderConfigStore providerStore,
    IChatProviderRegistry providerRegistry,
    IKeyVault keyVault,
    CancellationToken cancellationToken) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.TargetMessageId))
    {
        return Results.BadRequest("TargetMessageId is required.");
    }

    var session = await chats.GetSessionAsync(sessionId, cancellationToken);
    if (session is null)
    {
        return Results.NotFound();
    }

    var removed = await chats.RemoveMessagesFromAsync(sessionId, request.TargetMessageId, cancellationToken);
    if (removed == 0)
    {
        return Results.BadRequest("TargetMessageId not found in session.");
    }

    // Reload after removal — Messages snapshot was stale.
    session = await chats.GetSessionAsync(sessionId, cancellationToken);

    ProviderConfig? config = null;
    if (!string.IsNullOrWhiteSpace(request.ProviderConfigId))
    {
        config = await providerStore.GetAsync(request.ProviderConfigId, cancellationToken);
        if (config is null) return Results.BadRequest($"Provider config '{request.ProviderConfigId}' not found.");
        if (!config.IsEnabled) return Results.BadRequest($"Provider config '{request.ProviderConfigId}' is disabled.");
    }

    var providerType = config?.ProviderType ?? ProviderType.Custom;
    if (!providerRegistry.TryGet(providerType, out var provider) || provider is null)
    {
        return Results.BadRequest($"No registered driver for provider type '{providerType}'.");
    }

    string? apiKey = null;
    if (config is not null && !string.IsNullOrEmpty(config.ApiKeyCipher))
    {
        apiKey = keyVault.Unseal(config.ApiKeyCipher);
        if (apiKey is null) return Results.BadRequest("Stored provider key could not be decrypted on this machine.");
    }

    var streamRequest = new ChatStreamRequest(
        Messages: session!.Messages,
        Model: request.Model ?? config?.Model ?? "echo",
        ApiKey: apiKey ?? string.Empty,
        BaseUrl: config?.BaseUrl,
        SystemPrompt: config?.SystemPrompt,
        Temperature: config?.Temperature,
        MaxOutputTokens: config?.MaxOutputTokens);

    var response = httpContext.Response;
    response.Headers.Append("Content-Type", "text/event-stream");
    response.Headers.Append("Cache-Control", "no-cache, no-transform");
    response.Headers.Append("X-Accel-Buffering", "no");

    var buffer = new System.Text.StringBuilder();
    UsageStats? finalUsage = null;
    string? finishReason = null;

    try
    {
        await foreach (var chunk in provider.StreamAsync(streamRequest, cancellationToken))
        {
            if (chunk.Delta is { Length: > 0 }) buffer.Append(chunk.Delta);
            if (chunk.Usage is not null) finalUsage = chunk.Usage;
            if (!string.IsNullOrEmpty(chunk.FinishReason)) finishReason = chunk.FinishReason;
            await WriteSseEventAsync(response, "delta", chunk, cancellationToken);
        }

        var assistantMessage = new ChatMessage(
            "assistant",
            buffer.ToString(),
            DateTimeOffset.UtcNow,
            config?.Id,
            request.Model ?? config?.Model);
        await chats.AddMessageAsync(sessionId, assistantMessage, cancellationToken);

        await WriteSseEventAsync(response, "done", new
        {
            AssistantMessage = assistantMessage,
            Usage = finalUsage,
            FinishReason = finishReason
        }, cancellationToken);
    }
    catch (ProviderException ex)
    {
        await WriteSseEventAsync(response, "error", new
        {
            ex.ProviderType,
            StatusCode = (int)ex.StatusCode,
            Message = ex.Message
        }, cancellationToken);
    }
    catch (OperationCanceledException)
    {
        // Client disconnect; nothing to persist if interrupted.
    }

    return Results.Empty;
}).RequireRateLimiting("provider-stream");

app.MapPatch("/api/sessions/{sessionId}", async (
    string sessionId,
    PatchSessionRequest? request,
    IChatRepository chats,
    CancellationToken cancellationToken) =>
{
    if (request is null) return Results.BadRequest("Request body is required.");

    var ok = true;
    if (!string.IsNullOrWhiteSpace(request.Title))
    {
        ok &= await chats.RenameSessionAsync(sessionId, request.Title, cancellationToken);
    }
    if (request.Pinned.HasValue || request.FolderId is not null)
    {
        ok &= await chats.UpdateSessionAsync(sessionId, request.Pinned, request.FolderId, cancellationToken);
    }
    return ok ? Results.NoContent() : Results.NotFound();
});

app.MapPost("/api/rag/index", (IndexRequest? request, RagIndexer indexer, AppRuntimeSettingsStore settingsStore) =>
{
    if (request is null ||
        string.IsNullOrWhiteSpace(request.DocumentId) ||
        string.IsNullOrWhiteSpace(request.Content))
    {
        return Results.BadRequest("DocumentId and Content are required.");
    }

    if (request.Content.Length > Limits.MaxRagDocumentChars)
    {
        return Results.BadRequest($"Document content exceeds {Limits.MaxRagDocumentChars} characters.");
    }

    var settings = settingsStore.Current;
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

app.MapGet("/api/settings", (AppRuntimeSettingsStore settingsStore) =>
{
    var settings = settingsStore.Current;
    return Results.Ok(new
    {
        settings.ChunkingStrategy,
        settings.StorageMode,
        settings.KeepLatestSessions
    });
});

app.MapPost("/api/settings", (UpdateSettingsRequest? request, AppRuntimeSettingsStore settingsStore) =>
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
        var updated = settingsStore.Update(current =>
        {
            var resolvedChunking = ResolveChunking(request.ChunkingStrategy ?? current.ChunkingStrategy, null, null);
            var resolvedStorage = ResolveStorageMode(request.StorageMode ?? current.StorageMode);
            return current with
            {
                ChunkingStrategy = resolvedChunking.ChunkingStrategy,
                StorageMode = resolvedStorage,
                KeepLatestSessions = request.KeepLatestSessions ?? current.KeepLatestSessions
            };
        });

        return Results.Ok(new
        {
            updated.ChunkingStrategy,
            updated.StorageMode,
            updated.KeepLatestSessions
        });
    }
    catch (ArgumentOutOfRangeException exception)
    {
        return Results.BadRequest(exception.Message);
    }
});

app.MapGet("/api/storage/summary", async (IChatRepository chats, AppRuntimeSettingsStore settingsStore, CancellationToken cancellationToken) =>
{
    var sessions = await chats.GetSessionsAsync(cancellationToken);
    var messageCount = sessions.Sum(static s => s.MessageCount);
    var approximateChars = sessions.Sum(static s => s.Messages.Sum(static m => m.Content.Length));

    var settings = settingsStore.Current;
    return Results.Ok(new
    {
        SessionCount = sessions.Count,
        MessageCount = messageCount,
        ApproximateContentChars = approximateChars,
        settings.StorageMode,
        settings.KeepLatestSessions
    });
});

app.MapPost("/api/storage/cleanup", async (StorageCleanupRequest? request, IChatRepository chats, CancellationToken cancellationToken) =>
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

app.MapPost("/api/rag/retrieve", (RetrieveRequest? request, RagIndexer indexer) =>
{
    if (request is null ||
        string.IsNullOrWhiteSpace(request.DocumentId) ||
        string.IsNullOrWhiteSpace(request.DocumentContent) ||
        string.IsNullOrWhiteSpace(request.Query))
    {
        return Results.BadRequest("DocumentId, DocumentContent, and Query are required.");
    }

    if (request.DocumentContent.Length > Limits.MaxRagDocumentChars)
    {
        return Results.BadRequest($"DocumentContent exceeds {Limits.MaxRagDocumentChars} characters.");
    }

    if (request.Query.Length > Limits.MaxRagQueryChars)
    {
        return Results.BadRequest($"Query exceeds {Limits.MaxRagQueryChars} characters.");
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

app.MapGet("/api/providers", async (IProviderConfigStore store, IChatProviderRegistry registry, CancellationToken cancellationToken) =>
{
    var configs = await store.ListAsync(cancellationToken);
    var view = configs.Select(static c => new ProviderConfigView(
        c.Id,
        c.DisplayName,
        c.ProviderType,
        c.Model,
        c.IsEnabled,
        c.KeyFingerprint,
        c.BaseUrl,
        c.SystemPrompt,
        c.Temperature,
        c.MaxOutputTokens,
        c.CreatedAt,
        c.UpdatedAt,
        c.ApiKeyCipher is { Length: > 0 }));

    return Results.Ok(new
    {
        Providers = view,
        Available = registry.RegisteredTypes
    });
});

app.MapGet("/api/providers/{id}", async (string id, IProviderConfigStore store, CancellationToken cancellationToken) =>
{
    var config = await store.GetAsync(id, cancellationToken);
    if (config is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(new ProviderConfigView(
        config.Id,
        config.DisplayName,
        config.ProviderType,
        config.Model,
        config.IsEnabled,
        config.KeyFingerprint,
        config.BaseUrl,
        config.SystemPrompt,
        config.Temperature,
        config.MaxOutputTokens,
        config.CreatedAt,
        config.UpdatedAt,
        config.ApiKeyCipher is { Length: > 0 }));
});

app.MapPost("/api/providers", async (
    UpsertProviderRequest? request,
    IProviderConfigStore store,
    IKeyVault keyVault,
    CancellationToken cancellationToken) =>
{
    if (request is null ||
        string.IsNullOrWhiteSpace(request.DisplayName) ||
        string.IsNullOrWhiteSpace(request.Model))
    {
        return Results.BadRequest("DisplayName and Model are required.");
    }

    if (!Enum.IsDefined(request.ProviderType))
    {
        return Results.BadRequest("ProviderType is invalid.");
    }

    string? cipher = null;
    string? fingerprint = null;
    if (!string.IsNullOrWhiteSpace(request.ApiKey))
    {
        if (request.ApiKey.Length > Limits.MaxApiKeyChars)
        {
            return Results.BadRequest("ApiKey is too long.");
        }

        cipher = keyVault.Seal(request.ApiKey);
        fingerprint = keyVault.Fingerprint(request.ApiKey);
    }

    var id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : request.Id.Trim();
    var existing = await store.GetAsync(id, cancellationToken);

    var config = new ProviderConfig(
        Id: id,
        DisplayName: request.DisplayName.Trim(),
        ProviderType: request.ProviderType,
        Model: request.Model.Trim(),
        IsEnabled: request.IsEnabled ?? existing?.IsEnabled ?? true,
        ApiKeyCipher: cipher ?? existing?.ApiKeyCipher,
        KeyFingerprint: fingerprint ?? existing?.KeyFingerprint,
        BaseUrl: request.BaseUrl ?? existing?.BaseUrl,
        SystemPrompt: request.SystemPrompt ?? existing?.SystemPrompt,
        Temperature: request.Temperature ?? existing?.Temperature,
        MaxOutputTokens: request.MaxOutputTokens ?? existing?.MaxOutputTokens,
        CreatedAt: existing?.CreatedAt);

    var saved = await store.UpsertAsync(config, cancellationToken);

    return Results.Ok(new ProviderConfigView(
        saved.Id,
        saved.DisplayName,
        saved.ProviderType,
        saved.Model,
        saved.IsEnabled,
        saved.KeyFingerprint,
        saved.BaseUrl,
        saved.SystemPrompt,
        saved.Temperature,
        saved.MaxOutputTokens,
        saved.CreatedAt,
        saved.UpdatedAt,
        saved.ApiKeyCipher is { Length: > 0 }));
});

app.MapDelete("/api/providers/{id}", async (string id, IProviderConfigStore store, CancellationToken cancellationToken) =>
{
    var removed = await store.DeleteAsync(id, cancellationToken);
    return removed ? Results.NoContent() : Results.NotFound();
});

// ---- Personas ----
app.MapGet("/api/personas", async (IPersonaService personas, CancellationToken cancellationToken) =>
    Results.Ok(await personas.ListAsync(cancellationToken)));

app.MapPost("/api/personas", async (Persona? persona, IPersonaService personas, CancellationToken cancellationToken) =>
{
    if (persona is null || string.IsNullOrWhiteSpace(persona.Name)) return Results.BadRequest("Name is required.");
    if (string.IsNullOrWhiteSpace(persona.SystemPrompt)) return Results.BadRequest("SystemPrompt is required.");
    var saved = await personas.UpsertAsync(persona, cancellationToken);
    return Results.Ok(saved);
});

app.MapDelete("/api/personas/{id}", async (string id, IPersonaService personas, CancellationToken cancellationToken) =>
    await personas.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());

// ---- Search ----
app.MapGet("/api/search", async (string? q, int? limit, HybridSearchService search, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest("Query is required.");
    if (q.Length > Limits.MaxRagQueryChars) return Results.BadRequest("Query is too long.");
    var hits = await search.SearchMessagesAsync(q, limit ?? 20, cancellationToken);
    return Results.Ok(hits);
});

// ---- Documents ----
app.MapPost("/api/documents", async (HttpRequest request, DocumentService? documents, AppRuntimeSettingsStore settingsStore, CancellationToken cancellationToken) =>
{
    if (documents is null) return Results.BadRequest("Document storage requires SQLite mode (OmniChat:Storage=sqlite).");
    if (!request.HasFormContentType) return Results.BadRequest("Expected multipart form upload.");

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.FirstOrDefault();
    if (file is null) return Results.BadRequest("No file uploaded.");
    if (file.Length > Limits.MaxRagDocumentChars * 4L) return Results.BadRequest("File too large.");

    var settings = settingsStore.Current;
    var chunking = settings.ChunkingStrategy.Trim().ToLowerInvariant() switch
    {
        "focused" => (Max: 80, Overlap: 10),
        "broad"   => (Max: 180, Overlap: 30),
        _         => (Max: 120, Overlap: 20)
    };

    await using var stream = file.OpenReadStream();
    try
    {
        var result = await documents.ImportAsync(stream, file.FileName, file.ContentType, chunking.Max, chunking.Overlap, cancellationToken);
        return Results.Ok(result);
    }
    catch (NotSupportedException ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

app.MapGet("/api/documents", async (DocumentService? documents, CancellationToken cancellationToken) =>
    documents is null
        ? Results.Ok(Array.Empty<DocumentSummary>())
        : Results.Ok(await documents.ListAsync(cancellationToken)));

app.MapDelete("/api/documents/{id}", async (string id, DocumentService? documents, CancellationToken cancellationToken) =>
    documents is null ? Results.NotFound() :
    await documents.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());

app.MapGet("/api/documents/retrieve", async (string? q, int? topK, string? documentId, DocumentService? documents, CancellationToken cancellationToken) =>
{
    if (documents is null) return Results.BadRequest("Document retrieval requires SQLite mode.");
    if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest("Query is required.");
    if (q.Length > Limits.MaxRagQueryChars) return Results.BadRequest("Query is too long.");
    var hits = await documents.RetrieveAsync(q, topK ?? 3, documentId, cancellationToken);
    return Results.Ok(hits);
});

// ---- Export ----
app.MapGet("/api/sessions/{sessionId}/export", async (string sessionId, string? format, IChatRepository chats, CancellationToken cancellationToken) =>
{
    var session = await chats.GetSessionAsync(sessionId, cancellationToken);
    if (session is null) return Results.NotFound();

    var f = (format ?? "md").Trim().ToLowerInvariant();
    return f switch
    {
        "md" or "markdown" => Results.Text(
            SessionExporter.ToMarkdown(session),
            "text/markdown; charset=utf-8",
            System.Text.Encoding.UTF8),
        "json" => Results.Text(
            SessionExporter.ToJson(session),
            "application/json; charset=utf-8",
            System.Text.Encoding.UTF8),
        _ => Results.BadRequest("format must be 'md' or 'json'.")
    };
});

// ---- Pricing ----
app.MapGet("/api/pricing", () => Results.Ok(ProviderPricing.KnownModels));

await EnsureDatabaseAsync(app.Services);
await EnsureSeedDataAsync(app.Services);
await app.RunAsync();

static async Task EnsureDatabaseAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var factory = scope.ServiceProvider.GetService<IDbContextFactory<OmniChatDbContext>>();
    if (factory is null) return; // in-memory mode

    await using var db = await factory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
}

static async Task EnsureSeedDataAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var repository = scope.ServiceProvider.GetRequiredService<IChatRepository>();
    var providers = scope.ServiceProvider.GetRequiredService<IProviderConfigStore>();

    var existing = await repository.GetSessionsAsync(CancellationToken.None);
    if (existing.Count == 0)
    {
        var session = await repository.CreateSessionAsync("Welcome chat", CancellationToken.None);
        await repository.AddMessageAsync(session.Id, new ChatMessage(
            "assistant",
            "Welcome to OmniChat. Streaming is wired — add a provider key in Settings to chat with OpenAI, Anthropic, Azure, or Groq. Without a key, the offline Echo provider streams a friendly placeholder.",
            DateTimeOffset.UtcNow), CancellationToken.None);
    }

    var seededProviders = await providers.ListAsync(CancellationToken.None);
    if (seededProviders.Count == 0)
    {
        await providers.UpsertAsync(new ProviderConfig(
            Id: "echo-default",
            DisplayName: "Offline Echo",
            ProviderType: ProviderType.Custom,
            Model: "echo",
            IsEnabled: true,
            SystemPrompt: "You are the offline OmniChat echo agent."), CancellationToken.None);
    }
}

static async Task WriteSseEventAsync<T>(HttpResponse response, string eventName, T payload, CancellationToken cancellationToken)
{
    var json = JsonSerializer.Serialize(payload, SseJsonOptionsHolder.Instance);
    await response.WriteAsync($"event: {eventName}\n", cancellationToken);
    await response.WriteAsync($"data: {json}\n\n", cancellationToken);
    await response.Body.FlushAsync(cancellationToken);
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

public sealed record StreamMessageRequest(string Content, string? ProviderConfigId, string? Model);

public sealed record RegenerateRequest(string TargetMessageId, string? ProviderConfigId, string? Model);

public sealed record PatchSessionRequest(string? Title, bool? Pinned, string? FolderId);

public sealed record IndexRequest(
    string DocumentId,
    string Content,
    string? ChunkingStrategy = null,
    int? MaxWordsPerChunk = null,
    int? OverlapWords = null);

public sealed record RetrieveRequest(string DocumentId, string DocumentContent, string Query, int TopK = 3);

public sealed record UpdateSettingsRequest(string? ChunkingStrategy, string? StorageMode, int? KeepLatestSessions);

public sealed record StorageCleanupRequest(string Mode, int? KeepLatestSessions = null);

public sealed record UpsertProviderRequest(
    string? Id,
    string DisplayName,
    ProviderType ProviderType,
    string Model,
    string? ApiKey,
    bool? IsEnabled,
    string? BaseUrl,
    string? SystemPrompt,
    double? Temperature,
    int? MaxOutputTokens);

public sealed record ProviderConfigView(
    string Id,
    string DisplayName,
    ProviderType ProviderType,
    string Model,
    bool IsEnabled,
    string? KeyFingerprint,
    string? BaseUrl,
    string? SystemPrompt,
    double? Temperature,
    int? MaxOutputTokens,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    bool HasKey);

public sealed record AppRuntimeSettings(
    string ChunkingStrategy = "balanced",
    string StorageMode = "ephemeral",
    int KeepLatestSessions = 10);

public sealed class AppRuntimeSettingsStore
{
    private AppRuntimeSettings _current = new();

    public AppRuntimeSettings Current => Volatile.Read(ref _current);

    public AppRuntimeSettings Update(Func<AppRuntimeSettings, AppRuntimeSettings> updater)
    {
        ArgumentNullException.ThrowIfNull(updater);

        while (true)
        {
            var previous = Volatile.Read(ref _current);
            var next = updater(previous);
            if (ReferenceEquals(Interlocked.CompareExchange(ref _current, next, previous), previous))
            {
                return next;
            }
        }
    }
}

public sealed record ChunkingSettings(string ChunkingStrategy, int MaxWordsPerChunk, int OverlapWords);

internal static class Limits
{
    public const int MaxMessageChars = 100_000;
    public const int MaxRagDocumentChars = 2_000_000;
    public const int MaxRagQueryChars = 1_000;
    public const int MaxApiKeyChars = 8_192;
}

internal static class SseJsonOptionsHolder
{
    public static readonly JsonSerializerOptions Instance = new(JsonSerializerDefaults.Web);
}
