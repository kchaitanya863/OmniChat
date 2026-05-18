namespace OmniChat.Core.Services;

public sealed class PlainTextExtractor : IDocumentExtractor
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".rst", ".log",
        ".cs", ".js", ".ts", ".tsx", ".jsx", ".py", ".rb", ".go", ".rs", ".java", ".kt",
        ".html", ".css", ".scss", ".json", ".yml", ".yaml", ".toml", ".ini", ".xml", ".csv", ".tsv"
    };

    public bool CanHandle(string fileName, string? mime)
    {
        var ext = Path.GetExtension(fileName);
        return SupportedExtensions.Contains(ext) || (mime?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    public async Task<string> ExtractTextAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
