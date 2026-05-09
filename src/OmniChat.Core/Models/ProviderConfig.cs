namespace OmniChat.Core.Models;

public sealed record ProviderConfig(
    string Id,
    string DisplayName,
    ProviderType ProviderType,
    string Model,
    bool IsEnabled = true);
