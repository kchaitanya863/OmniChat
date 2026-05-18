using OmniChat.Core.Models;

namespace OmniChat.Core.Providers;

public interface IChatProviderRegistry
{
    bool TryGet(ProviderType providerType, out IChatProvider? provider);

    IReadOnlyCollection<ProviderType> RegisteredTypes { get; }
}
