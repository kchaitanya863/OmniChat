namespace OmniChat.Core.Data.Entities;

public sealed class ChunkEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DocumentId { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public string Content { get; set; } = string.Empty;

    /// <summary>Float32[] embedding packed as little-endian bytes. Read with <see cref="MemoryMarshal.Cast{Byte, Single}"/>.</summary>
    public byte[]? EmbeddingBlob { get; set; }

    public int? EmbeddingDimensions { get; set; }

    public DocumentEntity? Document { get; set; }
}
