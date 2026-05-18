using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;

namespace OmniChat.Core.Providers;

/// <summary>
/// Reads an HTTP Server-Sent-Events response into a stream of completed `data:` payloads.
/// Concatenates multi-line `data:` fields with newline separators, skips `[DONE]` sentinels,
/// and ignores comment lines beginning with `:`.
/// </summary>
public static class SseHttpStreamReader
{
    public static async IAsyncEnumerable<string> ReadDataPayloadsAsync(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var buffer = new StringBuilder();

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                if (TryFlush(buffer, out var payload))
                {
                    if (IsDoneSentinel(payload))
                    {
                        yield break;
                    }

                    yield return payload;
                }

                continue;
            }

            if (line[0] == ':')
            {
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var value = line.Length > 5 ? line[5..].TrimStart() : string.Empty;
            if (buffer.Length > 0)
            {
                buffer.Append('\n');
            }

            buffer.Append(value);
        }

        if (TryFlush(buffer, out var trailing) && !IsDoneSentinel(trailing))
        {
            yield return trailing;
        }
    }

    private static bool TryFlush(StringBuilder buffer, out string payload)
    {
        if (buffer.Length == 0)
        {
            payload = string.Empty;
            return false;
        }

        payload = buffer.ToString();
        buffer.Clear();
        return true;
    }

    private static bool IsDoneSentinel(string payload) =>
        string.Equals(payload, "[DONE]", StringComparison.OrdinalIgnoreCase);
}
