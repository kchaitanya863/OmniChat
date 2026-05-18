using System.Net;
using System.Net.Http;
using System.Text;
using OmniChat.Core.Models;
using OmniChat.Core.Providers;
using OmniChat.Core.Security;
using OmniChat.Core.Services;

namespace OmniChat.Core.Tests;

public sealed class ChunkingTests
{
    [Fact]
    public void Chunk_CreatesOverlappingChunks_WhenInputIsLong()
    {
        var input = string.Join(' ', Enumerable.Range(1, 30).Select(static i => $"word{i}"));
        var sut = new TextChunker();

        var chunks = sut.Chunk(input, maxWordsPerChunk: 10, overlapWords: 2);

        Assert.Equal(4, chunks.Count);
        Assert.Contains("word9", chunks[1]);
        Assert.Contains("word10", chunks[1]);
    }
}

public sealed class TokenBudgetTests
{
    [Fact]
    public void FitToBudget_KeepsMostRecentMessagesWithinBudget()
    {
        var now = DateTimeOffset.UtcNow;
        var messages = new List<ChatMessage>
        {
            new("user", "old old old old old", now.AddMinutes(-3)),
            new("assistant", "middle middle middle middle middle", now.AddMinutes(-2)),
            new("user", "latest latest latest latest latest", now.AddMinutes(-1))
        };

        var limited = TokenBudgetManager.FitToBudget(messages, tokenBudget: 13);

        Assert.Equal(2, limited.Count);
        Assert.StartsWith("… ", limited[0].Content, StringComparison.Ordinal);
        Assert.Equal("latest latest latest latest latest", limited[1].Content);
    }

    [Fact]
    public void FitToBudget_IncludesTruncatedMostRecentMessage_WhenNothingFitsFully()
    {
        var now = DateTimeOffset.UtcNow;
        var messages = new List<ChatMessage>
        {
            new("user", "alpha beta gamma delta epsilon zeta eta theta", now)
        };

        var limited = TokenBudgetManager.FitToBudget(messages, tokenBudget: 2);

        Assert.Single(limited);
        Assert.StartsWith("… ", limited[0].Content, StringComparison.Ordinal);
    }
}

public sealed class SseParserTests
{
    [Fact]
    public void ParseDataPayloads_ParsesMultiLineEvents_AndSkipsDone()
    {
        var sse = """
                  : keepalive
                  event: message
                  data: {"delta":"Hel"}
                  data: {"delta":"lo"}

                  data: [DONE]

                  data: {"delta":"World"}

                  """;

        var sut = new SseStreamParser();

        var payloads = sut.ParseDataPayloads(sse);

        Assert.Equal(2, payloads.Count);
        Assert.Equal("{\"delta\":\"Hel\"}\n{\"delta\":\"lo\"}", payloads[0]);
        Assert.Equal("{\"delta\":\"World\"}", payloads[1]);
    }
}

public sealed class RagIndexerTests
{
    [Fact]
    public void RetrieveTopK_ReturnsRelevantChunks()
    {
        var chunker = new TextChunker();
        var embedding = new LocalEmbeddingService();
        var sut = new RagIndexer(chunker, embedding);

        var doc = "local first privacy byok secure storage. model routing and chat context. on device embeddings and retrieval.";
        var indexed = sut.IndexDocument("doc-1", doc);

        var top = sut.RetrieveTopK("privacy local storage", indexed, 1);

        Assert.Single(top);
        Assert.Contains("privacy", top[0].Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IndexDocument_UsesProvidedChunkSettings()
    {
        var chunker = new TextChunker();
        var embedding = new LocalEmbeddingService();
        var sut = new RagIndexer(chunker, embedding);
        var text = string.Join(' ', Enumerable.Range(1, 80).Select(static i => $"token{i}"));

        var focused = sut.IndexDocument("doc-focused", text, maxWordsPerChunk: 20, overlapWords: 5);
        var broad = sut.IndexDocument("doc-broad", text, maxWordsPerChunk: 40, overlapWords: 10);

        Assert.True(focused.Count > broad.Count);
    }
}

public sealed class InMemoryChatRepositoryTests
{
    [Fact]
    public async Task TrimToLatestSessions_RemovesOlderSessions()
    {
        var repository = new InMemoryChatRepository();
        var first = await repository.CreateSessionAsync("First");
        await repository.AddMessageAsync(first.Id, new ChatMessage("user", "first", DateTimeOffset.UtcNow.AddMinutes(-2)));
        var second = await repository.CreateSessionAsync("Second");
        await repository.AddMessageAsync(second.Id, new ChatMessage("user", "second", DateTimeOffset.UtcNow.AddMinutes(-1)));

        var removed = await repository.TrimToLatestSessionsAsync(1);
        var remaining = await repository.GetSessionsAsync();

        Assert.Equal(1, removed);
        Assert.Single(remaining);
    }

    [Fact]
    public async Task GetSession_ReturnsSnapshot_NotLiveList()
    {
        var repository = new InMemoryChatRepository();
        var created = await repository.CreateSessionAsync("Race test");
        await repository.AddMessageAsync(created.Id, new ChatMessage("user", "one", DateTimeOffset.UtcNow));

        var snapshot = await repository.GetSessionAsync(created.Id);
        Assert.NotNull(snapshot);
        Assert.Equal(1, snapshot!.MessageCount);

        await repository.AddMessageAsync(created.Id, new ChatMessage("user", "two", DateTimeOffset.UtcNow));

        // Snapshot frozen at point of read.
        Assert.Equal(1, snapshot.MessageCount);

        var fresh = await repository.GetSessionAsync(created.Id);
        Assert.Equal(2, fresh!.MessageCount);
    }
}

public sealed class SseHttpStreamReaderTests
{
    [Fact]
    public async Task ReadDataPayloadsAsync_ConcatenatesMultiLine_StopsAtDone()
    {
        const string sse =
            ": ping\n" +
            "data: {\"a\":1}\n" +
            "data: {\"b\":2}\n" +
            "\n" +
            "data: hello\n" +
            "\n" +
            "data: [DONE]\n" +
            "\n" +
            "data: should-not-appear\n\n";

        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        };

        var payloads = new List<string>();
        await foreach (var payload in SseHttpStreamReader.ReadDataPayloadsAsync(response, CancellationToken.None))
        {
            payloads.Add(payload);
        }

        Assert.Equal(2, payloads.Count);
        Assert.Equal("{\"a\":1}\n{\"b\":2}", payloads[0]);
        Assert.Equal("hello", payloads[1]);
    }
}

public sealed class OpenAiProviderParserTests
{
    [Fact]
    public void ParseChunk_ExtractsDelta()
    {
        const string json = """
            {"choices":[{"index":0,"delta":{"content":"Hello"},"finish_reason":null}]}
            """;

        var chunk = OpenAiChatProvider.ParseChunk(json);

        Assert.NotNull(chunk);
        Assert.Equal("Hello", chunk!.Delta);
        Assert.Null(chunk.FinishReason);
    }

    [Fact]
    public void ParseChunk_ExtractsUsage_OnTerminalChunk()
    {
        const string json = """
            {"choices":[{"index":0,"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":12,"completion_tokens":7}}
            """;

        var chunk = OpenAiChatProvider.ParseChunk(json);

        Assert.NotNull(chunk);
        Assert.Equal("stop", chunk!.FinishReason);
        Assert.NotNull(chunk.Usage);
        Assert.Equal(12, chunk.Usage!.InputTokens);
        Assert.Equal(7, chunk.Usage.OutputTokens);
    }

    [Fact]
    public void ParseChunk_ReturnsNull_OnEmptyOrInvalidJson()
    {
        Assert.Null(OpenAiChatProvider.ParseChunk(""));
        Assert.Null(OpenAiChatProvider.ParseChunk("not-json"));
    }
}

public sealed class AnthropicProviderParserTests
{
    [Fact]
    public void ParseChunk_ExtractsContentBlockDeltaText()
    {
        const string json = """
            {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"world"}}
            """;

        var (chunk, input, output) = AnthropicChatProvider.ParseChunk(json);

        Assert.NotNull(chunk);
        Assert.Equal("world", chunk!.Delta);
        Assert.Equal(0, input);
        Assert.Equal(0, output);
    }

    [Fact]
    public void ParseChunk_ExtractsStopReason_AndOutputTokens()
    {
        const string json = """
            {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":42}}
            """;

        var (chunk, input, output) = AnthropicChatProvider.ParseChunk(json);

        Assert.NotNull(chunk);
        Assert.Equal("end_turn", chunk!.FinishReason);
        Assert.Equal(0, input);
        Assert.Equal(42, output);
    }

    [Fact]
    public void ParseChunk_ExtractsInputTokens_OnMessageStart()
    {
        const string json = """
            {"type":"message_start","message":{"id":"msg_1","usage":{"input_tokens":18,"output_tokens":0}}}
            """;

        var (chunk, input, output) = AnthropicChatProvider.ParseChunk(json);

        Assert.Null(chunk);
        Assert.Equal(18, input);
        Assert.Equal(0, output);
    }
}

public sealed class EchoChatProviderTests
{
    [Fact]
    public async Task StreamAsync_EmitsTokens_AndUsageOnCompletion()
    {
        var provider = new EchoChatProvider();
        var request = new ChatStreamRequest(
            Messages: [new ChatMessage("user", "hi there", DateTimeOffset.UtcNow)],
            Model: "echo",
            ApiKey: string.Empty);

        var deltas = new List<string>();
        UsageStats? usage = null;
        string? finish = null;

        await foreach (var chunk in provider.StreamAsync(request, CancellationToken.None))
        {
            if (chunk.Delta is { Length: > 0 })
            {
                deltas.Add(chunk.Delta);
            }
            if (chunk.Usage is not null) usage = chunk.Usage;
            if (chunk.FinishReason is not null) finish = chunk.FinishReason;
        }

        Assert.NotEmpty(deltas);
        Assert.Contains("You said:", string.Concat(deltas), StringComparison.Ordinal);
        Assert.Equal("stop", finish);
        Assert.NotNull(usage);
    }
}

public sealed class KeyFingerprintTests
{
    [Fact]
    public void Compute_ProducesStableSha256Prefix()
    {
        var f1 = KeyFingerprint.Compute("sk-test-abcdef");
        var f2 = KeyFingerprint.Compute("sk-test-abcdef");
        var f3 = KeyFingerprint.Compute("sk-test-other");

        Assert.StartsWith("sha256:", f1, StringComparison.Ordinal);
        Assert.Equal(19, f1.Length);
        Assert.Equal(f1, f2);
        Assert.NotEqual(f1, f3);
    }
}
