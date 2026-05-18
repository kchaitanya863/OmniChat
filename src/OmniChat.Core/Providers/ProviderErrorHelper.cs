using System.Net.Http;
using OmniChat.Core.Models;

namespace OmniChat.Core.Providers;

internal static class ProviderErrorHelper
{
    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        ProviderType providerType,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            body = string.Empty;
        }

        var trimmed = body.Length > 2_000 ? body[..2_000] + "…" : body;
        throw new ProviderException(
            providerType,
            response.StatusCode,
            $"{providerType} returned {(int)response.StatusCode} {response.ReasonPhrase}. {trimmed}");
    }
}
