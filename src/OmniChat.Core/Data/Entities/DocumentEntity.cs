namespace OmniChat.Core.Data.Entities;

public sealed class DocumentEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string? Mime { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ChunkEntity> Chunks { get; } = new();
}
