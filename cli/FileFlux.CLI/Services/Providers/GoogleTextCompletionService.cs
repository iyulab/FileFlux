using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileFlux.CLI.Services.Providers;

/// <summary>
/// Google Gemini API implementation for text completion using direct HTTP API
/// </summary>
public class GoogleTextCompletionService : IDocumentAnalysisService, IDisposable
{
    private static readonly JsonSerializerOptions s_serializeOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly JsonSerializerOptions s_deserializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private const string ApiEndpoint = "https://generativelanguage.googleapis.com/v1beta/models";

    public GoogleTextCompletionService(string apiKey, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
        _model = model;
    }

    public DocumentAnalysisServiceInfo ProviderInfo => new()
    {
        Name = "Google Gemini",
        Type = DocumentAnalysisProviderType.Google,
        SupportedModels = [_model],
        MaxContextLength = 1000000,
        InputTokenCost = 0.00015m,
        OutputTokenCost = 0.0006m,
        ApiVersion = "v1beta"
    };

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GeminiRequest
            {
                Contents = new[]
                {
                    new GeminiContent
                    {
                        Parts = new[]
                        {
                            new GeminiPart { Text = prompt }
                        }
                    }
                },
                GenerationConfig = new GeminiGenerationConfig
                {
                    Temperature = 0.7,
                    MaxOutputTokens = 1000
                }
            };

            var response = await PostAsync<GeminiResponse>(request, cancellationToken);
            return response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Google Gemini generation failed: {ex.Message}", ex);
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GeminiRequest
            {
                Contents = new[]
                {
                    new GeminiContent
                    {
                        Parts = new[]
                        {
                            new GeminiPart { Text = "Hello" }
                        }
                    }
                },
                GenerationConfig = new GeminiGenerationConfig
                {
                    MaxOutputTokens = 10
                }
            };

            await PostAsync<GeminiResponse>(request, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<T> PostAsync<T>(GeminiRequest requestBody, CancellationToken cancellationToken)
    {
        var url = $"{ApiEndpoint}/{_model}:generateContent";
        var json = JsonSerializer.Serialize(requestBody, s_serializeOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(responseJson, s_deserializeOptions)
            ?? throw new InvalidOperationException("Failed to deserialize response");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    // DTOs for Google Gemini API
    private class GeminiRequest
    {
        [JsonPropertyName("contents")]
        public GeminiContent[]? Contents { get; set; }

        [JsonPropertyName("systemInstruction")]
        public GeminiContent? SystemInstruction { get; set; }

        [JsonPropertyName("generationConfig")]
        public GeminiGenerationConfig? GenerationConfig { get; set; }
    }

    private class GeminiContent
    {
        [JsonPropertyName("parts")]
        public GeminiPart[]? Parts { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }
    }

    private class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("inlineData")]
        public GeminiInlineData? InlineData { get; set; }
    }

    private class GeminiInlineData
    {
        [JsonPropertyName("mimeType")]
        public string? MimeType { get; set; }

        [JsonPropertyName("data")]
        public string? Data { get; set; }
    }

    private class GeminiGenerationConfig
    {
        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        [JsonPropertyName("maxOutputTokens")]
        public int? MaxOutputTokens { get; set; }

        [JsonPropertyName("topP")]
        public double? TopP { get; set; }

        [JsonPropertyName("topK")]
        public int? TopK { get; set; }
    }

    private class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public GeminiCandidate[]? Candidates { get; set; }

        [JsonPropertyName("usageMetadata")]
        public GeminiUsageMetadata? UsageMetadata { get; set; }
    }

    private class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }

        [JsonPropertyName("finishReason")]
        public string? FinishReason { get; set; }
    }

    private class GeminiUsageMetadata
    {
        [JsonPropertyName("promptTokenCount")]
        public int PromptTokenCount { get; set; }

        [JsonPropertyName("candidatesTokenCount")]
        public int CandidatesTokenCount { get; set; }

        [JsonPropertyName("totalTokenCount")]
        public int TotalTokenCount { get; set; }
    }
}
