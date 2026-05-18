using OmniChat.Core.Models;

namespace OmniChat.Core.Services;

public static class TokenBudgetManager
{
    public static IReadOnlyList<ChatMessage> FitToBudget(IEnumerable<ChatMessage> messages, int tokenBudget)
    {
        if (tokenBudget <= 0)
        {
            return [];
        }

        var stack = new Stack<ChatMessage>();
        var consumed = 0;

        foreach (var message in messages.Reverse())
        {
            var tokens = TokenEstimator.EstimateTokens(message.Content);
            if (consumed + tokens > tokenBudget)
            {
                var remaining = tokenBudget - consumed;
                var truncated = TruncateToApproximateTokens(message.Content, remaining);
                if (truncated is not null)
                {
                    stack.Push(message with { Content = truncated });
                    consumed = tokenBudget;
                }

                continue;
            }

            stack.Push(message);
            consumed += tokens;
        }

        return stack.ToArray();
    }

    private static string? TruncateToApproximateTokens(string content, int maxTokens)
    {
        if (maxTokens <= 0 || string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var words = content.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return null;
        }

        var keepWords = Math.Max(1, (int)Math.Floor(maxTokens / 1.3));
        if (keepWords >= words.Length)
        {
            return content;
        }

        var start = words.Length - keepWords;
        return "… " + string.Join(' ', words, start, keepWords);
    }
}
