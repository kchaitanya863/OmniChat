namespace OmniChat.Core.Services;

public sealed class LocalEmbeddingService
{
    public IReadOnlyList<float> Embed(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var vector = new float[26];
        foreach (var ch in text.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z')
            {
                vector[ch - 'a'] += 1;
            }
        }

        var magnitude = (float)Math.Sqrt(vector.Sum(v => v * v));
        if (magnitude == 0)
        {
            return vector;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= magnitude;
        }

        return vector;
    }
}
