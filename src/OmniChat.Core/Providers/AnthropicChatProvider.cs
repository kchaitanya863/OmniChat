using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using OmniChat.Core.Models;

namespace OmniChat.Core.Providers;

public sealed class AnthropicChatProvider : IChatProvider
{
    private const string DefaultBaseUrl = "https://api.anthropic.com/v1";
    private const string DefaultApiVersion = "2023-06-01";
    private const int DefaultMaxOutputTokens = 1024;

    private readonly HttpClient _httpClient;

    public AnthropicChatProvider(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public ProviderType ProviderType => ProviderType.Anthropic;

    public async IAsyncEnumerable<ChatChunk> StreamAsync(
        ChatStreamRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var endpoint = ((request.BaseUrl ?? DefaultBaseUrl).TrimEnd('/')) + "/messages";

        var messages = new List<object>(request.Messages.Count);
        foreach (var message in request.Messages)
        {
            messages.Add(new
            {
                role = MapRole(message.Role),
                content = message.Content
            });
        }

        var body = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["max_tokens"] = request.MaxOutputTokens ?? DefaultMaxOutputTokens,
            ["stream"] = true
        };

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            body["system"] = request.SystemPrompt;
        }

        if (request.Temperature is not null)
        {
            body["temperature"] = request.Temperature;
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(body)
        };

        httpRequest.Headers.Add("x-api-key", request.ApiKey);
        httpRequest.Headers.Add("anthropic-version", request.ExtraHeader ?? DefaultApiVersion);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        await ProviderErrorHelper.EnsureSuccessAsync(response, ProviderType, cancellationToken);

        var inputTokens = 0;
        var outputTokens = 0;

        await foreach (var payload in SseHttpStreamReader.ReadDataPayloadsAsync(response, cancellationToken))
        {
            var (chunk, deltaInput, deltaOutput) = ParseChunk(payload);
            if (deltaInput > 0)
            {
                inputTokens += deltaInput;
            }

            if (deltaOutput > 0)
            {
                outputTokens += deltaOutput;
            }

            if (chunk is not null)
            {
                yield return chunk;
            }
        }

        if (inputTokens + outputTokens > 0)
        {
            yield return new ChatChunk(Usage: new UsageStats(inputTokens, outputTokens));
        }
    }

    private static string MapRole(string role) =>
        role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";

    internal static (ChatChunk? Chunk, int InputTokensDelta, int OutputTokensDelta) ParseChunk(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, 0, 0);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            {
                return (null, 0, 0);
            }

            var type = typeEl.GetString();

            switch (type)
            {
                case "content_block_delta":
                    if (root.TryGetProperty("delta", out var blockDelta) &&
                        blockDelta.TryGetProperty("text", out var textEl) &&
                        textEl.ValueKind == JsonValueKind.String)
                    {
                        return (new ChatChunk(Delta: textEl.GetString()), 0, 0);
                    }
                    break;

                case "message_delta":
                    string? stopReason = null;
                    if (root.TryGetProperty("delta", out var messageDelta) &&
                        messageDelta.TryGetProperty("stop_reason", out var stopEl) &&
                        stopEl.ValueKind == JsonValueKind.String)
                    {
                        stopReason = stopEl.GetString();
                    }

                    var outTok = 0;
                    if (root.TryGetProperty("usage", out var usageEl) &&
                        usageEl.TryGetProperty("output_tokens", out var outEl) &&
                        outEl.TryGetInt32(out var parsedOut))
                    {
                        outTok = parsedOut;
                    }

                    return (stopReason is null ? null : new ChatChunk(FinishReason: stopReason), 0, outTok);

                case "message_start":
                    if (root.TryGetProperty("message", out var msgEl) &&
                        msgEl.TryGetProperty("usage", out var startUsage) &&
                        startUsage.TryGetProperty("input_tokens", out var inEl) &&
                        inEl.TryGetInt32(out var parsedIn))
                    {
                        return (null, parsedIn, 0);
                    }
                    break;
            }
        }
        catch (JsonException)
        {
            // ignore malformed chunk
        }

        return (null, 0, 0);
    }
}
