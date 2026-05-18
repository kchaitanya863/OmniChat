using OmniChat.Core.Models;

namespace OmniChat.Core.Services;

public interface IProviderConfigStore
{
    Task<IReadOnlyList<ProviderConfig>> ListAsync(CancellationToken cancellationToken = default);

    Task<ProviderConfig?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<ProviderConfig> UpsertAsync(ProviderConfig config, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
