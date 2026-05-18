using OmniChat.Core.Models;

namespace OmniChat.Core.Providers;

public sealed record ChatStreamRequest(
    IReadOnlyList<ChatMessage> Messages,
    string Model,
    string ApiKey,
    string? BaseUrl = null,
    string? SystemPrompt = null,
    double? Temperature = null,
    int? MaxOutputTokens = null,
    string? ExtraHeader = null);
