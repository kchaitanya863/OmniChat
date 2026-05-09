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
        return Task.FromResult(session);
    }

    public Task<IReadOnlyList<ChatSession>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ChatSession> sessions = _sessions.Values
            .OrderByDescending(static s => s.Messages.LastOrDefault()?.Timestamp ?? DateTimeOffset.MinValue)
            .ThenBy(static s => s.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(sessions);
    }

    public Task<ChatSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(session);
    }

    public Task AddMessageAsync(string sessionId, ChatMessage message, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            lock (session.Messages)
            {
                session.Messages.Add(message);
            }

            return Task.CompletedTask;
        }

        throw new KeyNotFoundException($"Chat session '{sessionId}' was not found.");
    }
}
