using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using OmniChat.Core.Models;

namespace OmniChat.Core.Providers;

public class OpenAiChatProvider : IChatProvider
{
    private const string DefaultBaseUrl = "https://api.openai.com/v1";

    private readonly HttpClient _httpClient;

    public OpenAiChatProvider(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public virtual ProviderType ProviderType => ProviderType.OpenAi;

    public async IAsyncEnumerable<ChatChunk> StreamAsync(
        ChatStreamRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var endpoint = BuildEndpoint(request.BaseUrl);
        using var httpRequest = BuildRequest(endpoint, request);

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        await ProviderErrorHelper.EnsureSuccessAsync(response, ProviderType, cancellationToken);

        await foreach (var payload in SseHttpStreamReader.ReadDataPayloadsAsync(response, cancellationToken))
        {
            var chunk = ParseChunk(payload);
            if (chunk is not null)
            {
                yield return chunk;
            }
        }
    }

    protected virtual string BuildEndpoint(string? baseUrl)
    {
        var trimmed = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
        return $"{trimmed}/chat/completions";
    }

    protected virtual HttpRequestMessage BuildRequest(string endpoint, ChatStreamRequest request)
    {
        var messages = BuildMessagePayload(request);
        var body = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["stream"] = true,
            ["stream_options"] = new { include_usage = true }
        };

        if (request.Temperature is not null)
        {
            body["temperature"] = request.Temperature;
        }

        if (request.MaxOutputTokens is not null)
        {
            body["max_tokens"] = request.MaxOutputTokens;
        }

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(body)
        };

        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        return httpRequest;
    }

    private static IReadOnlyList<object> BuildMessagePayload(ChatStreamRequest request)
    {
        var list = new List<object>(request.Messages.Count + 1);

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            list.Add(new { role = "system", content = request.SystemPrompt });
        }

        foreach (var message in request.Messages)
        {
            list.Add(new
            {
                role = MapRole(message.Role),
                content = message.Content
            });
        }

        return list;
    }

    private static string MapRole(string role) =>
        role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" :
        role.Equals("system", StringComparison.OrdinalIgnoreCase) ? "system" : "user";

    internal static ChatChunk? ParseChunk(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            string? delta = null;
            string? finishReason = null;
            UsageStats? usage = null;

            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("delta", out var deltaEl) &&
                    deltaEl.TryGetProperty("content", out var contentEl) &&
                    contentEl.ValueKind == JsonValueKind.String)
                {
                    delta = contentEl.GetString();
                }

                if (first.TryGetProperty("finish_reason", out var finishEl) &&
                    finishEl.ValueKind == JsonValueKind.String)
                {
                    finishReason = finishEl.GetString();
                }
            }

            if (root.TryGetProperty("usage", out var usageEl) && usageEl.ValueKind == JsonValueKind.Object)
            {
                var input = ReadIntOrZero(usageEl, "prompt_tokens");
                var output = ReadIntOrZero(usageEl, "completion_tokens");
                if (input + output > 0)
                {
                    usage = new UsageStats(input, output);
                }
            }

            if (delta is null && finishReason is null && usage is null)
            {
                return null;
            }

            return new ChatChunk(delta, finishReason, usage);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int ReadIntOrZero(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var parsed))
        {
            return parsed;
        }

        return 0;
    }
}
