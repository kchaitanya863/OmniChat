using Microsoft.ML.OnnxRuntime;

namespace OmniChat.Core.Services;

/// <summary>
/// Sentence embedding via a local ONNX model (target: all-MiniLM-L6-v2, 384-dim).
/// Loads lazily on first <see cref="Embed"/> call. When the model file is missing
/// (first-run pre-download), <see cref="IsReady"/> stays false and Embed throws
/// — callers should compose with <see cref="LocalEmbeddingService"/> via
/// <see cref="CompositeEmbeddingService"/>.
/// </summary>
public sealed class OnnxEmbeddingService : IEmbeddingService, IDisposable
{
    public const int DefaultDimensions = 384;
    public const int MaxSequenceLength = 256;

    private readonly string _modelPath;
    private readonly object _initLock = new();
    private InferenceSession? _session;
    private bool _initFailed;
    private string? _initError;

    public OnnxEmbeddingService(string modelPath, int dimensions = DefaultDimensions)
    {
        _modelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
        Dimensions = dimensions;
    }

    public int Dimensions { get; }

    public bool IsReady
    {
        get
        {
            if (_session is not null) return true;
            if (_initFailed) return false;
            return File.Exists(_modelPath);
        }
    }

    public string? InitError => _initError;

    public IReadOnlyList<float> Embed(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        EnsureSessionLoaded();

        if (_session is null)
        {
            throw new InvalidOperationException(_initError ?? "ONNX embedding model is not ready.");
        }

        // NOTE: real implementation requires a tokenizer (BertTokenizer / SentencePiece)
        // configured for the target model. To keep this milestone shippable without bundling
        // a 23MB ONNX file and a vocab in the repo, we emit a deterministic hash-based vector
        // shaped to `Dimensions` so RagIndexer keeps working. M5b will replace this with the
        // actual tokenize → run → mean-pool → normalize path.
        return HashedFallback.Embed(text, Dimensions);
    }

    private void EnsureSessionLoaded()
    {
        if (_session is not null || _initFailed) return;

        lock (_initLock)
        {
            if (_session is not null || _initFailed) return;

            try
            {
                if (!File.Exists(_modelPath))
                {
                    _initFailed = true;
                    _initError = $"Embedding model not found at '{_modelPath}'.";
                    return;
                }

                var options = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    IntraOpNumThreads = 1
                };
                _session = new InferenceSession(_modelPath, options);
            }
            catch (Exception ex)
            {
                _initFailed = true;
                _initError = ex.Message;
            }
        }
    }

    public void Dispose() => _session?.Dispose();
}

/// <summary>Stable, content-derived vector. Not semantically meaningful, but consistent across calls.</summary>
internal static class HashedFallback
{
    public static IReadOnlyList<float> Embed(string text, int dimensions)
    {
        var vector = new float[dimensions];
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        for (var i = 0; i < bytes.Length; i++)
        {
            vector[i % dimensions] += bytes[i] / 255f;
        }

        var magnitude = 0f;
        for (var i = 0; i < dimensions; i++) magnitude += vector[i] * vector[i];
        magnitude = (float)Math.Sqrt(magnitude);
        if (magnitude > 0)
        {
            for (var i = 0; i < dimensions; i++) vector[i] /= magnitude;
        }
        return vector;
    }
}
