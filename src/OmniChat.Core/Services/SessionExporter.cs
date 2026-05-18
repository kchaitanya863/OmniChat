using System.Text;
using System.Text.Json;
using OmniChat.Core.Models;

namespace OmniChat.Core.Services;

public static class SessionExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string ToMarkdown(ChatSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var sb = new StringBuilder();
        sb.AppendLine($"# {session.Title}");
        sb.AppendLine();
        sb.AppendLine($"*Exported {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC · {session.MessageCount} messages*");
        sb.AppendLine();

        foreach (var msg in session.Messages)
        {
            var role = msg.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "Assistant" : "You";
            sb.AppendLine($"## {role}");
            if (msg.Model is { Length: > 0 })
            {
                sb.AppendLine($"*Model: `{msg.Model}`*");
            }
            sb.AppendLine();
            sb.AppendLine(msg.Content);
            sb.AppendLine();
            sb.AppendLine($"<sub>{msg.Timestamp:O}</sub>");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string ToJson(ChatSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return JsonSerializer.Serialize(new
        {
            session.Id,
            session.Title,
            session.Pinned,
            session.FolderId,
            ExportedAt = DateTimeOffset.UtcNow,
            Messages = session.Messages
        }, JsonOptions);
    }
}
