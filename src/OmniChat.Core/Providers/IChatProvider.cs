using OmniChat.Core.Models;

namespace OmniChat.Core.Providers;

public interface IChatProvider
{
    ProviderType ProviderType { get; }

    IAsyncEnumerable<ChatChunk> StreamAsync(
        ChatStreamRequest request,
        CancellationToken cancellationToken = default);
}
