using Microsoft.EntityFrameworkCore;
using OmniChat.Core.Data;

namespace OmniChat.Core.Services;

public sealed class HybridSearchService
{
    private readonly IDbContextFactory<OmniChatDbContext>? _factory;
    private readonly IChatRepository _chats;

    public HybridSearchService(IChatRepository chats, IDbContextFactory<OmniChatDbContext>? factory = null)
    {
        _chats = chats ?? throw new ArgumentNullException(nameof(chats));
        _factory = factory;
    }

    public async Task<IReadOnlyList<SearchHit>> SearchMessagesAsync(string query, int limit = 20, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SearchHit>();
        if (limit <= 0 || limit > 100) limit = 20;

        // NOTE: SQLite FTS5 virtual-table integration arrives at M10 (a content-external FTS5 table
        // wired with triggers). Until then, a LIKE-based scan keeps semantics consistent across
        // the in-memory + SQLite repositories. Both paths are cheap below 10k messages.
        if (_factory is not null)
        {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken);
            var like = $"%{query.Trim()}%";
            var rows = await db.Messages
                .AsNoTracking()
                .Where(m => EF.Functions.Like(m.Content, like))
                .OrderByDescending(m => m.Timestamp)
                .Take(limit)
                .Select(m => new { m.Id, m.SessionId, m.Content, m.Role, m.Timestamp })
                .ToListAsync(cancellationToken);
            return rows.Select(r => new SearchHit(r.Id, r.SessionId, r.Role, Snippet(r.Content, query), r.Timestamp)).ToArray();
        }

        // In-memory fallback: walk sessions
        var sessions = await _chats.GetSessionsAsync(cancellationToken);
        var hits = new List<SearchHit>();
        foreach (var session in sessions)
        {
            foreach (var msg in session.Messages)
            {
                if (msg.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add(new SearchHit(msg.Id ?? string.Empty, session.Id, msg.Role, Snippet(msg.Content, query), msg.Timestamp));
                    if (hits.Count >= limit) return hits;
                }
            }
        }
        return hits;
    }

    private static string Snippet(string content, string query)
    {
        var i = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return content.Length > 240 ? content[..240] + "…" : content;
        var start = Math.Max(0, i - 60);
        var end = Math.Min(content.Length, i + query.Length + 120);
        var slice = content[start..end];
        return (start > 0 ? "…" : "") + slice + (end < content.Length ? "…" : "");
    }
}

public sealed record SearchHit(string MessageId, string SessionId, string Role, string Snippet, DateTimeOffset Timestamp);
