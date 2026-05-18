using OmniChat.Core.Models;

namespace OmniChat.Core.Services;

public sealed class LocalWebSearchService
{
    public Task<IReadOnlyList<SearchEvidence>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        IReadOnlyList<SearchEvidence> evidence =
        [
            new SearchEvidence(
                "Local-First Design",
                "https://example.local/docs/local-first",
                $"Local-first summary for query: '{query}'."),
            new SearchEvidence(
                "BYOK Security",
                "https://example.local/docs/byok",
                "Provider keys remain user-managed and are not transmitted to third-party telemetry services."),
            new SearchEvidence(
                "On-device Retrieval",
                "https://example.local/docs/rag",
                "Embeddings and vector retrieval can be executed entirely on device for privacy and latency benefits.")
        ];

        return Task.FromResult(evidence);
    }
}
