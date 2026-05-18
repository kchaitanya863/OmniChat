namespace OmniChat.Core.Data.Entities;

public sealed class MessageEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public string? ProviderId { get; set; }
    public string? Model { get; set; }
    public int? TokenCount { get; set; }
    public int? CostMicros { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public SessionEntity? Session { get; set; }
}
