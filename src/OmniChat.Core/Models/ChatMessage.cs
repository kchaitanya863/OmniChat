namespace OmniChat.Core.Models;

public sealed record ChatMessage(
    string Role,
    string Content,
    DateTimeOffset Timestamp,
    string? ProviderId = null,
    string? Model = null);
