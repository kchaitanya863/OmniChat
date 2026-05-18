namespace OmniChat.Core.Models;

public sealed class ChatSession
{
    private readonly List<ChatMessage> _messages = [];
    private readonly Lock _gate = new();

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = "New Chat";

    public bool Pinned { get; set; }

    public string? FolderId { get; set; }

    public IReadOnlyList<ChatMessage> Messages
    {
        get
        {
            lock (_gate)
            {
                return _messages.Count == 0
                    ? Array.Empty<ChatMessage>()
                    : _messages.ToArray();
            }
        }
    }

    public int MessageCount
    {
        get
        {
            lock (_gate)
            {
                return _messages.Count;
            }
        }
    }

    public ChatMessage? LastMessage
    {
        get
        {
            lock (_gate)
            {
                return _messages.Count == 0 ? null : _messages[^1];
            }
        }
    }

    public ChatMessage AppendMessage(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var stamped = message.Id is { Length: > 0 }
            ? message
            : message with { Id = Guid.NewGuid().ToString("N") };

        lock (_gate)
        {
            _messages.Add(stamped);
        }

        return stamped;
    }

    /// <summary>Removes the message with <paramref name="messageId"/> and every message after it.</summary>
    public int RemoveFrom(string messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        lock (_gate)
        {
            for (var i = 0; i < _messages.Count; i++)
            {
                if (string.Equals(_messages[i].Id, messageId, StringComparison.Ordinal))
                {
                    var removed = _messages.Count - i;
                    _messages.RemoveRange(i, removed);
                    return removed;
                }
            }
        }

        return 0;
    }

    public ChatSession Snapshot()
    {
        var clone = new ChatSession
        {
            Id = Id,
            Title = Title,
            Pinned = Pinned,
            FolderId = FolderId
        };

        lock (_gate)
        {
            clone._messages.AddRange(_messages);
        }

        return clone;
    }
}
