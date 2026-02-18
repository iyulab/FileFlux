using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FI = FluxImprover.Services;

namespace FileFlux.CLI.Services.Providers.FluxImprover;

/// <summary>
/// Anthropic Claude implementation of FluxImprover's ITextCompletionService
/// </summary>
public class AnthropicCompletionService : FI.ITextCompletionService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private const string ApiEndpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    public AnthropicCompletionService(string apiKey, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
        _model = model;
    }

    public async Task<string> CompleteAsync(
        string prompt,
        FI.CompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<object>();

        if (options?.Messages is { Count: > 0 })
        {
            foreach (var msg in options.Messages)
            {
                if (!string.Equals(msg.Role, "system", StringComparison.OrdinalIgnoreCase))
                {
                    messages.Add(new { role = msg.Role.ToLowerInvariant(), content = msg.Content });
                }
            }
        }

        messages.Add(new { role = "user", content = prompt });

        var request = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["messages"] = messages,
            ["max_tokens"] = options?.MaxTokens ?? 4096,
            ["temperature"] = options?.Temperature ?? 0.7f
        };

        if (!string.IsNullOrWhiteSpace(options?.SystemPrompt))
        {
            request["system"] = options.SystemPrompt;
        }

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(ApiEndpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var anthropicResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseJson);

        var contentList = anthropicResponse?.Content;
        return contentList is { Count: > 0 } ? contentList[0].Text ?? string.Empty : string.Empty;
    }

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        string prompt,
        FI.CompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = new List<object>();

        if (options?.Messages is { Count: > 0 })
        {
            foreach (var msg in options.Messages)
            {
                if (!string.Equals(msg.Role, "system", StringComparison.OrdinalIgnoreCase))
                {
                    messages.Add(new { role = msg.Role.ToLowerInvariant(), content = msg.Content });
                }
            }
        }

        messages.Add(new { role = "user", content = prompt });

        var request = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["messages"] = messages,
            ["max_tokens"] = options?.MaxTokens ?? 4096,
            ["temperature"] = options?.Temperature ?? 0.7f,
            ["stream"] = true
        };

        if (!string.IsNullOrWhiteSpace(options?.SystemPrompt))
        {
            request["system"] = options.SystemPrompt;
        }

        var json = JsonSerializer.Serialize(request);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: "))
            {
                continue;
            }

            var data = line[6..];
            if (data == "[DONE]")
            {
                break;
            }

            var streamEvent = JsonSerializer.Deserialize<AnthropicStreamEvent>(data);
            if (streamEvent?.Type == "content_block_delta" && streamEvent.Delta?.Text is not null)
            {
                yield return streamEvent.Delta.Text;
            }
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private class AnthropicResponse
    {
        [JsonPropertyName("content")]
        public List<AnthropicContent>? Content { get; set; }
    }

    private class AnthropicContent
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private class AnthropicStreamEvent
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("delta")]
        public AnthropicDelta? Delta { get; set; }
    }

    private class AnthropicDelta
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
