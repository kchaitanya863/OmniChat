namespace OmniChat.Core.Models;

public sealed record ProviderConfig(
    string Id,
    string DisplayName,
    ProviderType ProviderType,
    string Model,
    bool IsEnabled = true,
    string? ApiKeyCipher = null,
    string? KeyFingerprint = null,
    string? BaseUrl = null,
    string? SystemPrompt = null,
    double? Temperature = null,
    int? MaxOutputTokens = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? UpdatedAt = null);
