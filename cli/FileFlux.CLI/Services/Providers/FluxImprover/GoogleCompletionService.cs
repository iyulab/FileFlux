using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FI = FluxImprover.Services;

namespace FileFlux.CLI.Services.Providers.FluxImprover;

/// <summary>
/// Google Gemini implementation of FluxImprover's ITextCompletionService
/// </summary>
public class GoogleCompletionService : FI.ITextCompletionService, IDisposable
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private const string ApiEndpoint = "https://generativelanguage.googleapis.com/v1beta/models";

    public GoogleCompletionService(string apiKey, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
        _model = model;
    }

    public async Task<string> CompleteAsync(
        string prompt,
        FI.CompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var contents = new List<object>();

        if (options?.Messages is { Count: > 0 })
        {
            foreach (var msg in options.Messages)
            {
                if (!string.Equals(msg.Role, "system", StringComparison.OrdinalIgnoreCase))
                {
                    contents.Add(new
                    {
                        role = string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user",
                        parts = new[] { new { text = msg.Content } }
                    });
                }
            }
        }

        contents.Add(new
        {
            role = "user",
            parts = new[] { new { text = prompt } }
        });

        var request = new Dictionary<string, object>
        {
            ["contents"] = contents,
            ["generationConfig"] = new
            {
                maxOutputTokens = options?.MaxTokens ?? 4096,
                temperature = options?.Temperature ?? 0.7f
            }
        };

        if (!string.IsNullOrWhiteSpace(options?.SystemPrompt))
        {
            request["systemInstruction"] = new
            {
                parts = new[] { new { text = options.SystemPrompt } }
            };
        }

        var url = $"{ApiEndpoint}/{_model}:generateContent";
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseJson, s_jsonOptions);

        var candidates = geminiResponse?.Candidates;
        var parts = candidates is { Count: > 0 } ? candidates[0].Content?.Parts : null;
        return parts is { Count: > 0 } ? parts[0].Text ?? string.Empty : string.Empty;
    }

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        string prompt,
        FI.CompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var contents = new List<object>();

        if (options?.Messages is { Count: > 0 })
        {
            foreach (var msg in options.Messages)
            {
                if (!string.Equals(msg.Role, "system", StringComparison.OrdinalIgnoreCase))
                {
                    contents.Add(new
                    {
                        role = string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user",
                        parts = new[] { new { text = msg.Content } }
                    });
                }
            }
        }

        contents.Add(new
        {
            role = "user",
            parts = new[] { new { text = prompt } }
        });

        var request = new Dictionary<string, object>
        {
            ["contents"] = contents,
            ["generationConfig"] = new
            {
                maxOutputTokens = options?.MaxTokens ?? 4096,
                temperature = options?.Temperature ?? 0.7f
            }
        };

        if (!string.IsNullOrWhiteSpace(options?.SystemPrompt))
        {
            request["systemInstruction"] = new
            {
                parts = new[] { new { text = options.SystemPrompt } }
            };
        }

        var url = $"{ApiEndpoint}/{_model}:streamGenerateContent?alt=sse";
        var json = JsonSerializer.Serialize(request);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
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

            var text = TryParseStreamResponse(data);
            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }
    }

    private static string? TryParseStreamResponse(string data)
    {
        try
        {
            var streamResponse = JsonSerializer.Deserialize<GeminiResponse>(data, s_jsonOptions);

            var candidates = streamResponse?.Candidates;
            var parts = candidates is { Count: > 0 } ? candidates[0].Content?.Parts : null;
            return parts is { Count: > 0 } ? parts[0].Text : null;
        }
        catch (JsonException)
        {
            // Skip malformed JSON chunks
            return null;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }

    private class GeminiContent
    {
        [JsonPropertyName("parts")]
        public List<GeminiPart>? Parts { get; set; }
    }

    private class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
