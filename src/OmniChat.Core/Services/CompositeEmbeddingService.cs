namespace OmniChat.Core.Services;

/// <summary>
/// Prefers <see cref="OnnxEmbeddingService"/> when ready; falls back to <see cref="LocalEmbeddingService"/>.
/// Dimensions reported from whichever back-end is currently active so RagIndexer behaves consistently.
/// </summary>
public sealed class CompositeEmbeddingService : IEmbeddingService
{
    private readonly OnnxEmbeddingService _onnx;
    private readonly LocalEmbeddingService _fallback;

    public CompositeEmbeddingService(OnnxEmbeddingService onnx, LocalEmbeddingService fallback)
    {
        _onnx = onnx ?? throw new ArgumentNullException(nameof(onnx));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public int Dimensions => _onnx.IsReady ? _onnx.Dimensions : _fallback.Dimensions;

    public bool IsReady => true;

    public IReadOnlyList<float> Embed(string text) =>
        _onnx.IsReady ? _onnx.Embed(text) : _fallback.Embed(text);
}
