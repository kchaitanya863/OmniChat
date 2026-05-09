using OmniChat.Core.Models;

namespace OmniChat.Core.Services;

public sealed class RagIndexer
{
    private readonly TextChunker _chunker;
    private readonly LocalEmbeddingService _embeddingService;

    public RagIndexer(TextChunker chunker, LocalEmbeddingService embeddingService)
    {
        _chunker = chunker;
        _embeddingService = embeddingService;
    }

    public IReadOnlyList<DocumentChunk> IndexDocument(string documentId, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var chunks = _chunker.Chunk(content);
        return chunks
            .Select((chunk, index) => new DocumentChunk(documentId, index, chunk, _embeddingService.Embed(chunk)))
            .ToArray();
    }

    public IReadOnlyList<DocumentChunk> RetrieveTopK(string query, IReadOnlyList<DocumentChunk> indexedChunks, int k = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        if (k <= 0 || indexedChunks.Count == 0)
        {
            return [];
        }

        var queryEmbedding = _embeddingService.Embed(query);

        return indexedChunks
            .Select(chunk => new { Chunk = chunk, Score = CosineSimilarity(queryEmbedding, chunk.Embedding) })
            .OrderByDescending(static item => item.Score)
            .Take(k)
            .Select(static item => item.Chunk)
            .ToArray();
    }

    private static float CosineSimilarity(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        var length = Math.Min(a.Count, b.Count);
        float sum = 0;
        for (var i = 0; i < length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }
}
