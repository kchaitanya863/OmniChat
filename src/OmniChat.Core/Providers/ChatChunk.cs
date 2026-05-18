namespace OmniChat.Core.Providers;

public sealed record ChatChunk(
    string? Delta = null,
    string? FinishReason = null,
    UsageStats? Usage = null);

public sealed record UsageStats(
    int InputTokens,
    int OutputTokens)
{
    public int TotalTokens => InputTokens + OutputTokens;
}
