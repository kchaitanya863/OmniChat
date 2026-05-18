using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace OmniChat.Core.Services;

public sealed class DocxDocumentExtractor : IDocumentExtractor
{
    public bool CanHandle(string fileName, string? mime) =>
        Path.GetExtension(fileName).Equals(".docx", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mime, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

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
            using var doc = WordprocessingDocument.Open(usable, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null) return Task.FromResult(string.Empty);

            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text));
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine(text);
                }
            }
            return Task.FromResult(sb.ToString());
        }
        finally
        {
            buffered?.Dispose();
        }
    }
}
