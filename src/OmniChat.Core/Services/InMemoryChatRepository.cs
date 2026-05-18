using System.Collections.Concurrent;
using OmniChat.Core.Models;

namespace OmniChat.Core.Services;

public sealed class InMemoryChatRepository : IChatRepository
{
    private readonly ConcurrentDictionary<string, ChatSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public Task<ChatSession> CreateSessionAsync(string title, CancellationToken cancellationToken = default)
    {
        var session = new ChatSession
        {
            Title = string.IsNullOrWhiteSpace(title) ? "New Chat" : title.Trim()
        };

        _sessions[session.Id] = session;
        return Task.FromResult(session.Snapshot());
    }

    public Task<IReadOnlyList<ChatSession>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ChatSession> sessions = _sessions.Values
            .Select(static s => s.Snapshot())
            .OrderByDescending(static s => s.LastMessage?.Timestamp ?? DateTimeOffset.MinValue)
            .ThenBy(static s => s.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(sessions);
    }

    public Task<ChatSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult<ChatSession?>(session.Snapshot());
        }

        return Task.FromResult<ChatSession?>(null);
    }

    public Task AddMessageAsync(string sessionId, ChatMessage message, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.AppendMessage(message);
            return Task.CompletedTask;
        }

        throw new KeyNotFoundException($"Chat session '{sessionId}' was not found.");
    }

    public Task<int> RemoveMessagesFromAsync(string sessionId, string messageId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult(session.RemoveFrom(messageId));
        }

        throw new KeyNotFoundException($"Chat session '{sessionId}' was not found.");
    }

    public Task<bool> RenameSessionAsync(string sessionId, string title, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.Title = title.Trim();
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<bool> UpdateSessionAsync(string sessionId, bool? pinned, string? folderId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult(false);
        }

        if (pinned.HasValue) session.Pinned = pinned.Value;
        if (folderId is not null) session.FolderId = string.IsNullOrWhiteSpace(folderId) ? null : folderId;
        return Task.FromResult(true);
    }

    public Task<int> DeleteAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        var deleted = _sessions.Count;
        _sessions.Clear();
        return Task.FromResult(deleted);
    }

    public Task<int> TrimToLatestSessionsAsync(int keepLatestCount, CancellationToken cancellationToken = default)
    {
        if (keepLatestCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keepLatestCount));
        }

        var ordered = _sessions.Values
            .Select(static s => (s.Id, Timestamp: s.LastMessage?.Timestamp ?? DateTimeOffset.MinValue, s.Title))
            .OrderByDescending(static t => t.Timestamp)
            .ThenBy(static t => t.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var removed = 0;
        foreach (var (id, _, _) in ordered.Skip(keepLatestCount))
        {
            if (_sessions.TryRemove(id, out _))
            {
                removed++;
            }
        }

        return Task.FromResult(removed);
    }
}
