namespace OmniChat.Core.Services;

public interface IEmbeddingService
{
    /// <summary>Embedding dimensionality. Used by storage layers that need fixed-width vector columns.</summary>
    int Dimensions { get; }

    /// <summary>Returns true when the underlying model is ready (downloaded + loaded).</summary>
    bool IsReady { get; }

    /// <summary>Embeds a single text into a normalized vector.</summary>
    IReadOnlyList<float> Embed(string text);
}
