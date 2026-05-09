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
                continue;
            }

            stack.Push(message);
            consumed += tokens;
        }

        return stack.ToArray();
    }
}
