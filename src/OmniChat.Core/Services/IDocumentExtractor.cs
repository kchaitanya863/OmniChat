namespace OmniChat.Core.Services;

public interface IDocumentExtractor
{
    /// <summary>True when this extractor can handle the file (by extension or sniffed MIME).</summary>
    bool CanHandle(string fileName, string? mime);

    Task<string> ExtractTextAsync(Stream stream, string fileName, CancellationToken cancellationToken = default);
}

public sealed record ExtractedDocument(string Name, string Text, string Sha256);
