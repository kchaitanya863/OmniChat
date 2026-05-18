namespace OmniChat.Core.Services;

public sealed class DocumentExtractorRegistry
{
    private readonly IReadOnlyList<IDocumentExtractor> _extractors;

    public DocumentExtractorRegistry(IEnumerable<IDocumentExtractor> extractors)
    {
        _extractors = extractors.ToArray();
    }

    public IDocumentExtractor? ResolveFor(string fileName, string? mime) =>
        _extractors.FirstOrDefault(e => e.CanHandle(fileName, mime));
}
