using System.Net.Http;
using OmniChat.Core.Models;

namespace OmniChat.Core.Providers;

public sealed class GroqChatProvider : OpenAiChatProvider
{
    private const string GroqBaseUrl = "https://api.groq.com/openai/v1";

    public GroqChatProvider(HttpClient httpClient)
        : base(httpClient)
    {
    }

    public override ProviderType ProviderType => ProviderType.Groq;

    protected override string BuildEndpoint(string? baseUrl)
    {
        var trimmed = (baseUrl ?? GroqBaseUrl).TrimEnd('/');
        return $"{trimmed}/chat/completions";
    }
}
