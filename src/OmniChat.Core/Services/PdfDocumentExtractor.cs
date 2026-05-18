using System.Text;
using UglyToad.PdfPig;

namespace OmniChat.Core.Services;

public sealed class PdfDocumentExtractor : IDocumentExtractor
{
    public bool CanHandle(string fileName, string? mime) =>
        Path.GetExtension(fileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mime, "application/pdf", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // PdfPig requires a seekable stream. Copy to memory if needed.
        Stream usable = stream;
        MemoryStream? buffered = null;
        if (!stream.CanSeek)
        {
            buffered = new MemoryStream();
            stream.CopyTo(buffered);
            buffered.Position = 0;
            usable = buffered;
        }

        try
        {
            var sb = new StringBuilder();
            using var pdf = PdfDocument.Open(usable);
            foreach (var page in pdf.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine(page.Text);
                sb.AppendLine();
            }
            return Task.FromResult(sb.ToString());
        }
        finally
        {
            buffered?.Dispose();
        }
    }
}
