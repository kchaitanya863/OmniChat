using OmniChat.Core.Providers;

namespace OmniChat.Core.Services;

/// <summary>
/// Pinned price table per million tokens. Source of truth for cost tracking.
/// Values are USD. Update when providers change pricing — keep a TODO comment if you can't verify a model.
/// </summary>
public static class ProviderPricing
{
    public sealed record PerMillion(decimal Input, decimal Output);

    private static readonly Dictionary<string, PerMillion> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // OpenAI
        ["gpt-4o-mini"]     = new(0.150m, 0.600m),
        ["gpt-4o"]          = new(2.500m, 10.000m),
        ["o1-mini"]         = new(1.100m, 4.400m),
        ["o1"]              = new(15.000m, 60.000m),
        // Anthropic (verify current pricing before relying)
        ["claude-opus-4-7"]    = new(15.000m, 75.000m),
        ["claude-sonnet-4-6"]  = new(3.000m, 15.000m),
        ["claude-haiku-4-5"]   = new(0.800m, 4.000m),
        // Groq (Llama family)
        ["llama-3.3-70b-versatile"] = new(0.590m, 0.790m),
        ["llama-3.1-8b-instant"]    = new(0.050m, 0.080m),
    };

    /// <summary>Cost in micros (USD × 1,000,000). Null when the model is unknown.</summary>
    public static int? CostMicrosForUsage(string? model, UsageStats? usage)
    {
        if (string.IsNullOrWhiteSpace(model) || usage is null) return null;
        if (!Table.TryGetValue(model, out var price)) return null;

        var inputCost  = (decimal)usage.InputTokens  / 1_000_000m * price.Input;
        var outputCost = (decimal)usage.OutputTokens / 1_000_000m * price.Output;
        return (int)Math.Round((inputCost + outputCost) * 1_000_000m);
    }

    public static IReadOnlyDictionary<string, PerMillion> KnownModels => Table;
}
