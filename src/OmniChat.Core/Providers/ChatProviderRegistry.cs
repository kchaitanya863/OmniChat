using OmniChat.Core.Models;

namespace OmniChat.Core.Providers;

public sealed class ChatProviderRegistry : IChatProviderRegistry
{
    private readonly IReadOnlyDictionary<ProviderType, IChatProvider> _providers;

    public ChatProviderRegistry(IEnumerable<IChatProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToDictionary(static p => p.ProviderType);
    }

    public IReadOnlyCollection<ProviderType> RegisteredTypes => _providers.Keys.ToArray();

    public bool TryGet(ProviderType providerType, out IChatProvider? provider)
    {
        var found = _providers.TryGetValue(providerType, out var resolved);
        provider = resolved;
        return found;
    }
}
