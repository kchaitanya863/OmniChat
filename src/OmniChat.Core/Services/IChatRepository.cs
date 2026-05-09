using OmniChat.Core.Models;

namespace OmniChat.Core.Services;

public interface IChatRepository
{
    Task<ChatSession> CreateSessionAsync(string title, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatSession>> GetSessionsAsync(CancellationToken cancellationToken = default);

    Task<ChatSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task AddMessageAsync(string sessionId, ChatMessage message, CancellationToken cancellationToken = default);
}
