using System.Runtime.CompilerServices;
using OmniChat.Core.Models;

namespace OmniChat.Core.Providers;

/// <summary>
/// Offline provider that streams a deterministic echo of the last user message.
/// Used as a zero-dependency default when no real provider is configured —
/// preserves the local-first/BYOK posture during onboarding and tests.
/// </summary>
public sealed class EchoChatProvider : IChatProvider
{
    public ProviderType ProviderType => ProviderType.Custom;

    public async IAsyncEnumerable<ChatChunk> StreamAsync(
        ChatStreamRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lastUser = request.Messages.LastOrDefault(static m =>
            m.Role.Equals("user", StringComparison.OrdinalIgnoreCase));
        var prompt = lastUser?.Content ?? string.Empty;

        var reply = string.IsNullOrWhiteSpace(prompt)
            ? "Echo provider ready. Send a message and OmniChat will echo it back token-by-token."
            : $"You said: {prompt}\n\n(Echo provider — configure a real provider in Settings to stream from OpenAI, Anthropic, Azure, or Groq.)";

        foreach (var token in TokenizeWithSpaces(reply))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatChunk(Delta: token);
            await Task.Delay(15, cancellationToken);
        }

        yield return new ChatChunk(
            FinishReason: "stop",
            Usage: new UsageStats(InputTokens: prompt.Length / 4, OutputTokens: reply.Length / 4));
    }

    private static IEnumerable<string> TokenizeWithSpaces(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != ' ')
            {
                continue;
            }

            yield return text[start..(i + 1)];
            start = i + 1;
        }

        if (start < text.Length)
        {
            yield return text[start..];
        }
    }
}
