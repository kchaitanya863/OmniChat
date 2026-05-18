using Microsoft.EntityFrameworkCore;
using OmniChat.Core.Data;
using OmniChat.Core.Data.Entities;
using OmniChat.Core.Models;

namespace OmniChat.Core.Services;

public sealed class SqliteChatRepository : IChatRepository
{
    private readonly IDbContextFactory<OmniChatDbContext> _factory;

    public SqliteChatRepository(IDbContextFactory<OmniChatDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<ChatSession> CreateSessionAsync(string title, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var entity = new SessionEntity
        {
            Title = string.IsNullOrWhiteSpace(title) ? "New Chat" : title.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Sessions.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return Materialize(entity, Array.Empty<MessageEntity>());
    }

    public async Task<IReadOnlyList<ChatSession>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Sessions
            .AsNoTracking()
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0) return Array.Empty<ChatSession>();

        var ids = rows.Select(r => r.Id).ToArray();
        var messages = await db.Messages
            .AsNoTracking()
            .Where(m => ids.Contains(m.SessionId))
            .OrderBy(m => m.SessionId).ThenBy(m => m.Timestamp)
            .ToListAsync(cancellationToken);

        var bySession = messages.GroupBy(m => m.SessionId).ToDictionary(g => g.Key, g => g.ToList());

        return rows
            .Select(r => Materialize(r, bySession.TryGetValue(r.Id, out var list) ? list : new List<MessageEntity>()))
            .ToArray();
    }

    public async Task<ChatSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var session = await db.Sessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null) return null;

        var messages = await db.Messages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Timestamp)
            .ToListAsync(cancellationToken);

        return Materialize(session, messages);
    }

    public async Task AddMessageAsync(string sessionId, ChatMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var sessionExists = await db.Sessions.AnyAsync(s => s.Id == sessionId, cancellationToken);
        if (!sessionExists)
        {
            throw new KeyNotFoundException($"Chat session '{sessionId}' was not found.");
        }

        db.Messages.Add(new MessageEntity
        {
            Id = string.IsNullOrEmpty(message.Id) ? Guid.NewGuid().ToString("N") : message.Id!,
            SessionId = sessionId,
            ParentId = message.ParentId,
            Role = message.Role,
            Content = message.Content,
            ProviderId = message.ProviderId,
            Model = message.Model,
            TokenCount = message.TokenCount,
            CostMicros = message.CostMicros,
            Timestamp = message.Timestamp
        });

        // bump session UpdatedAt
        var entity = await db.Sessions.FirstAsync(s => s.Id == sessionId, cancellationToken);
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> DeleteAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var count = await db.Sessions.CountAsync(cancellationToken);
        await db.Messages.ExecuteDeleteAsync(cancellationToken);
        await db.Sessions.ExecuteDeleteAsync(cancellationToken);
        return count;
    }

    public async Task<int> TrimToLatestSessionsAsync(int keepLatestCount, CancellationToken cancellationToken = default)
    {
        if (keepLatestCount < 0) throw new ArgumentOutOfRangeException(nameof(keepLatestCount));

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var idsToDelete = await db.Sessions
            .OrderByDescending(s => s.UpdatedAt)
            .Skip(keepLatestCount)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (idsToDelete.Count == 0) return 0;

        await db.Messages.Where(m => idsToDelete.Contains(m.SessionId)).ExecuteDeleteAsync(cancellationToken);
        await db.Sessions.Where(s => idsToDelete.Contains(s.Id)).ExecuteDeleteAsync(cancellationToken);
        return idsToDelete.Count;
    }

    public async Task<int> RemoveMessagesFromAsync(string sessionId, string messageId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var target = await db.Messages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.SessionId == sessionId && m.Id == messageId, cancellationToken);
        if (target is null) return 0;

        var deleted = await db.Messages
            .Where(m => m.SessionId == sessionId && m.Timestamp >= target.Timestamp)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted;
    }

    public async Task<bool> RenameSessionAsync(string sessionId, string title, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (entity is null) return false;

        entity.Title = title.Trim();
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateSessionAsync(string sessionId, bool? pinned, string? folderId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (entity is null) return false;

        if (pinned.HasValue) entity.Pinned = pinned.Value;
        if (folderId is not null) entity.FolderId = string.IsNullOrWhiteSpace(folderId) ? null : folderId;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static ChatSession Materialize(SessionEntity entity, IEnumerable<MessageEntity> messages)
    {
        var session = new ChatSession
        {
            Id = entity.Id,
            Title = entity.Title,
            Pinned = entity.Pinned,
            FolderId = entity.FolderId
        };

        foreach (var m in messages.OrderBy(m => m.Timestamp))
        {
            session.AppendMessage(new ChatMessage(
                Role: m.Role,
                Content: m.Content,
                Timestamp: m.Timestamp,
                ProviderId: m.ProviderId,
                Model: m.Model,
                Id: m.Id,
                ParentId: m.ParentId,
                TokenCount: m.TokenCount,
                CostMicros: m.CostMicros));
        }

        return session;
    }
}
