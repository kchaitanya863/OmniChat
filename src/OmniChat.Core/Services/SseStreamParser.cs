using System.Text;

namespace OmniChat.Core.Services;

public sealed class SseStreamParser
{
    public IReadOnlyList<string> ParseDataPayloads(string ssePayload)
    {
        ArgumentNullException.ThrowIfNull(ssePayload);

        using var reader = new StringReader(ssePayload);
        var payloads = new List<string>();
        var eventBuffer = new StringBuilder();

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                FlushBuffer(payloads, eventBuffer);
                continue;
            }

            if (line.StartsWith(':'))
            {
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var value = line.Length > 5 ? line[5..].TrimStart() : string.Empty;
            if (eventBuffer.Length > 0)
            {
                eventBuffer.Append('\n');
            }

            eventBuffer.Append(value);
        }

        FlushBuffer(payloads, eventBuffer);
        return payloads;
    }

    private static void FlushBuffer(List<string> payloads, StringBuilder eventBuffer)
    {
        if (eventBuffer.Length == 0)
        {
            return;
        }

        var payload = eventBuffer.ToString();
        if (!string.Equals(payload, "[DONE]", StringComparison.OrdinalIgnoreCase))
        {
            payloads.Add(payload);
        }

        eventBuffer.Clear();
    }
}
