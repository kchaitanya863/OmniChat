using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OmniChat.Core.Data;
using OmniChat.Core.Data.Entities;

namespace OmniChat.Core.Services;

public sealed class DocumentService
{
    private readonly IDbContextFactory<OmniChatDbContext> _factory;
    private readonly DocumentExtractorRegistry _extractors;
    private readonly TextChunker _chunker;
    private readonly IEmbeddingService _embeddings;

    public DocumentService(
        IDbContextFactory<OmniChatDbContext> factory,
        DocumentExtractorRegistry extractors,
        TextChunker chunker,
        IEmbeddingService embeddings)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _extractors = extractors ?? throw new ArgumentNullException(nameof(extractors));
        _chunker = chunker ?? throw new ArgumentNullException(nameof(chunker));
        _embeddings = embeddings ?? throw new ArgumentNullException(nameof(embeddings));
    }

    public async Task<DocumentImport> ImportAsync(
        Stream stream,
        string fileName,
        string? mime,
        int maxWordsPerChunk,
        int overlapWords,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var extractor = _extractors.ResolveFor(fileName, mime)
            ?? throw new NotSupportedException($"No extractor available for '{fileName}' ({mime}).");

        // Hash + extract in one pass when we can. Simpler: copy to memory once (size limits enforced upstream).
        await using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(memory, cancellationToken)).ToLowerInvariant();
        memory.Position = 0;

        var text = await extractor.ExtractTextAsync(memory, fileName, cancellationToken);
        var chunks = _chunker.Chunk(text, maxWordsPerChunk, overlapWords);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var doc = new DocumentEntity
        {
            Name = fileName,
            Mime = mime,
            Sha256 = sha256,
            Bytes = memory.Length,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Documents.Add(doc);

        var dim = _embeddings.Dimensions;
        var seq = 0;
        foreach (var chunkText in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vector = _embeddings.Embed(chunkText);
            db.Chunks.Add(new ChunkEntity
            {
                DocumentId = doc.Id,
                Sequence = seq++,
                Content = chunkText,
                EmbeddingBlob = PackVector(vector),
                EmbeddingDimensions = dim
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return new DocumentImport(doc.Id, doc.Name, sha256, chunks.Count, dim);
    }

    public async Task<IReadOnlyList<DocumentSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Documents
            .AsNoTracking()
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DocumentSummary(d.Id, d.Name, d.Mime, d.Sha256, d.Bytes, d.Chunks.Count, d.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Documents.Where(d => d.Id == id).ExecuteDeleteAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<IReadOnlyList<DocumentChunkHit>> RetrieveAsync(
        string query,
        int topK,
        string? documentId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || topK <= 0) return Array.Empty<DocumentChunkHit>();
        var queryVec = _embeddings.Embed(query);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var query2 = db.Chunks.AsNoTracking();
        if (!string.IsNullOrEmpty(documentId)) query2 = query2.Where(c => c.DocumentId == documentId);

        var rows = await query2
            .Where(c => c.EmbeddingBlob != null)
            .Select(c => new { c.Id, c.DocumentId, c.Sequence, c.Content, c.EmbeddingBlob })
            .ToListAsync(cancellationToken);

        var scored = rows
            .Select(r => new
            {
                Hit = new DocumentChunkHit(r.Id, r.DocumentId, r.Sequence, r.Content),
                Score = CosineSimilarity(queryVec, UnpackVector(r.EmbeddingBlob!))
            })
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Hit)
            .ToArray();

        return scored;
    }

    private static byte[] PackVector(IReadOnlyList<float> vector)
    {
        var bytes = new byte[vector.Count * sizeof(float)];
        Buffer.BlockCopy(vector is float[] arr ? arr : vector.ToArray(), 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] UnpackVector(byte[] blob)
    {
        var floats = new float[blob.Length / sizeof(float)];
        Buffer.BlockCopy(blob, 0, floats, 0, blob.Length);
        return floats;
    }

    private static float CosineSimilarity(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        var len = Math.Min(a.Count, b.Count);
        float dot = 0, na = 0, nb = 0;
        for (var i = 0; i < len; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        if (na == 0 || nb == 0) return 0;
        return dot / (float)Math.Sqrt(na * nb);
    }
}

public sealed record DocumentImport(string Id, string Name, string Sha256, int ChunkCount, int EmbeddingDimensions);
public sealed record DocumentSummary(string Id, string Name, string? Mime, string Sha256, long Bytes, int ChunkCount, DateTimeOffset CreatedAt);
public sealed record DocumentChunkHit(string ChunkId, string DocumentId, int Sequence, string Content);
