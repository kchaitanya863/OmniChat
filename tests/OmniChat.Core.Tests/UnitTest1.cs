using OmniChat.Core.Models;
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

        Assert.Single(limited);
        Assert.Equal("latest latest latest latest latest", limited[0].Content);
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
}
