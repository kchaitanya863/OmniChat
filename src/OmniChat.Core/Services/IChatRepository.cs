using OmniChat.Core.Models;

namespace OmniChat.Core.Services;

public interface IChatRepository
{
    Task<ChatSession> CreateSessionAsync(string title, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatSession>> GetSessionsAsync(CancellationToken cancellationToken = default);

    Task<ChatSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task AddMessageAsync(string sessionId, ChatMessage message, CancellationToken cancellationToken = default);

    Task<int> DeleteAllSessionsAsync(CancellationToken cancellationToken = default);

    Task<int> TrimToLatestSessionsAsync(int keepLatestCount, CancellationToken cancellationToken = default);

    /// <summary>Removes the message with the specified ID and every message after it. Returns the number removed.</summary>
    Task<int> RemoveMessagesFromAsync(string sessionId, string messageId, CancellationToken cancellationToken = default);

    /// <summary>Renames a session. Returns false if the session is missing.</summary>
    Task<bool> RenameSessionAsync(string sessionId, string title, CancellationToken cancellationToken = default);

    /// <summary>Updates pin flag and/or folder assignment. Returns false if the session is missing.</summary>
    Task<bool> UpdateSessionAsync(string sessionId, bool? pinned, string? folderId, CancellationToken cancellationToken = default);
}
