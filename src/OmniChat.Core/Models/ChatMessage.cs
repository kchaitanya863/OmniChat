namespace OmniChat.Core.Models;

public sealed record ChatMessage(
    string Role,
    string Content,
    DateTimeOffset Timestamp,
    string? ProviderId = null,
    string? Model = null,
    string? Id = null,
    string? ParentId = null,
    int? TokenCount = null,
    int? CostMicros = null)
{
    public string EnsureId() => Id ?? Guid.NewGuid().ToString("N");

    public ChatMessage WithId(string id) => this with { Id = id };
}
