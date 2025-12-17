using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FI = FluxImprover.Services;

namespace FileFlux.CLI.Services.Providers.FluxImprover;

/// <summary>
/// Google Gemini implementation of FluxImprover's ITextCompletionService
/// </summary>
public class GoogleCompletionService : FI.ITextCompletionService
{
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
                if (msg.Role.ToLowerInvariant() != "system")
                {
                    contents.Add(new
                    {
                        role = msg.Role.ToLowerInvariant() == "assistant" ? "model" : "user",
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
        var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseJson, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
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
                if (msg.Role.ToLowerInvariant() != "system")
                {
                    contents.Add(new
                    {
                        role = msg.Role.ToLowerInvariant() == "assistant" ? "model" : "user",
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

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
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
            var streamResponse = JsonSerializer.Deserialize<GeminiResponse>(data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            return streamResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        }
        catch (JsonException)
        {
            // Skip malformed JSON chunks
            return null;
        }
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
