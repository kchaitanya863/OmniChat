namespace OmniChat.Core.Services;

public sealed class TextChunker
{
    public IReadOnlyList<string> Chunk(string? text, int maxWordsPerChunk = 120, int overlapWords = 20)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        if (maxWordsPerChunk <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWordsPerChunk));
        }

        if (overlapWords < 0 || overlapWords >= maxWordsPerChunk)
        {
            throw new ArgumentOutOfRangeException(nameof(overlapWords));
        }

        var words = text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= maxWordsPerChunk)
        {
            return [string.Join(' ', words)];
        }

        var chunks = new List<string>();
        var stride = maxWordsPerChunk - overlapWords;

        for (var i = 0; i < words.Length; i += stride)
        {
            var count = Math.Min(maxWordsPerChunk, words.Length - i);
            if (count <= 0)
            {
                break;
            }

            var chunk = string.Join(' ', words, i, count);
            chunks.Add(chunk);

            if (i + count >= words.Length)
            {
                break;
            }
        }

        return chunks;
    }
}
