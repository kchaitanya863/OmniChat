using System.Collections.Concurrent;
using OmniChat.Core.Models;

namespace OmniChat.Core.Services;

public sealed class InMemoryProviderConfigStore : IProviderConfigStore
{
    private readonly ConcurrentDictionary<string, ProviderConfig> _configs = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<ProviderConfig>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProviderConfig> snapshot = _configs.Values
            .OrderByDescending(static c => c.UpdatedAt ?? c.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(static c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(snapshot);
    }

    public Task<ProviderConfig?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        _configs.TryGetValue(id, out var config);
        return Task.FromResult(config);
    }

    public Task<ProviderConfig> UpsertAsync(ProviderConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var stamped = config with
        {
            CreatedAt = config.CreatedAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _configs[stamped.Id] = stamped;
        return Task.FromResult(stamped);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var removed = _configs.TryRemove(id, out _);
        return Task.FromResult(removed);
    }
}
