using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using OmniChat.Core.Models;

namespace OmniChat.Core.Providers;

public sealed class AzureOpenAiChatProvider : OpenAiChatProvider
{
    private const string DefaultApiVersion = "2024-10-21";

    public AzureOpenAiChatProvider(HttpClient httpClient)
        : base(httpClient)
    {
    }

    public override ProviderType ProviderType => ProviderType.AzureOpenAi;

    protected override string BuildEndpoint(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Azure OpenAI requires BaseUrl to point at the deployment endpoint (e.g. https://<resource>.openai.azure.com/openai/deployments/<deployment>).");
        }

        var trimmed = baseUrl.TrimEnd('/');
        if (!trimmed.Contains("api-version=", StringComparison.OrdinalIgnoreCase))
        {
            var separator = trimmed.Contains('?') ? '&' : '?';
            trimmed = $"{trimmed}/chat/completions{separator}api-version={DefaultApiVersion}";
        }
        else if (!trimmed.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            var queryIndex = trimmed.IndexOf('?', StringComparison.Ordinal);
            var pathPart = queryIndex >= 0 ? trimmed[..queryIndex] : trimmed;
            var queryPart = queryIndex >= 0 ? trimmed[queryIndex..] : string.Empty;
            trimmed = $"{pathPart}/chat/completions{queryPart}";
        }

        return trimmed;
    }

    protected override HttpRequestMessage BuildRequest(string endpoint, ChatStreamRequest request)
    {
        var httpRequest = base.BuildRequest(endpoint, request);
        httpRequest.Headers.Authorization = null;
        httpRequest.Headers.Remove("api-key");
        httpRequest.Headers.Add("api-key", request.ApiKey);
        return httpRequest;
    }
}
