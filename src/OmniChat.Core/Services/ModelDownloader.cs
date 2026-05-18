using System.Net.Http;
using System.Security.Cryptography;

namespace OmniChat.Core.Services;

/// <summary>
/// Downloads + verifies a single embedding model file on first launch. Streams to disk
/// to avoid loading 20-30 MB into memory, then SHA-256 checks against a pinned manifest.
/// Never commits the model file — the manifest only lives in code.
/// </summary>
public sealed class ModelDownloader
{
    private readonly IHttpClientFactory _factory;

    public ModelDownloader(IHttpClientFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<bool> EnsureModelAsync(ModelManifest manifest, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (File.Exists(manifest.DestinationPath) &&
            await VerifyAsync(manifest.DestinationPath, manifest.ExpectedSha256, cancellationToken))
        {
            return true;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(manifest.DestinationPath)!);

        var client = _factory.CreateClient("model-download");
        using var response = await client.GetAsync(manifest.SourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        var tmpPath = manifest.DestinationPath + ".part";

        await using (var src = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var dst = File.Create(tmpPath))
        {
            var buffer = new byte[81_920];
            long copied = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                copied += read;
                progress?.Report(new DownloadProgress(copied, totalBytes));
            }
        }

        if (!await VerifyAsync(tmpPath, manifest.ExpectedSha256, cancellationToken))
        {
            File.Delete(tmpPath);
            return false;
        }

        if (File.Exists(manifest.DestinationPath)) File.Delete(manifest.DestinationPath);
        File.Move(tmpPath, manifest.DestinationPath);
        return true;
    }

    private static async Task<bool> VerifyAsync(string path, string expectedSha256, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256)) return true;

        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return string.Equals(hex, expectedSha256.ToLowerInvariant(), StringComparison.Ordinal);
    }
}

public sealed record ModelManifest(string SourceUrl, string DestinationPath, string ExpectedSha256);

public sealed record DownloadProgress(long Bytes, long Total);
