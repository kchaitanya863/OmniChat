using OmniChat.Core.Models;

namespace OmniChat.Core.Data.Entities;

public sealed class ProviderConfigEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = string.Empty;
    public ProviderType ProviderType { get; set; }
    public string Model { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string? ApiKeyCipher { get; set; }
    public string? KeyFingerprint { get; set; }
    public string? BaseUrl { get; set; }
    public string? SystemPrompt { get; set; }
    public double? Temperature { get; set; }
    public int? MaxOutputTokens { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
