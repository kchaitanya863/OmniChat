namespace OmniChat.Core.Services;

public static class TokenEstimator
{
    public static int EstimateTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var words = text
            .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Length;

        return Math.Max(1, (int)Math.Ceiling(words * 1.3));
    }
}
