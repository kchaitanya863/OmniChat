namespace OmniChat.Core.Data.Entities;

public sealed class SessionEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New Chat";
    public string? FolderId { get; set; }
    public bool Pinned { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<MessageEntity> Messages { get; } = new();
}
