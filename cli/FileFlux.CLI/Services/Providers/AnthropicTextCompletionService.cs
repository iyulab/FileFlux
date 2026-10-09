using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileFlux.CLI.Services.Providers;

/// <summary>
/// Anthropic Claude API implementation for text completion using direct HTTP API
/// </summary>
public class AnthropicTextCompletionService : IDocumentAnalysisService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private const string ApiEndpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    public AnthropicTextCompletionService(string apiKey, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
        _model = model;
    }

    public DocumentAnalysisServiceInfo ProviderInfo => new()
    {
        Name = "Anthropic",
        Type = DocumentAnalysisProviderType.Anthropic,
        SupportedModels = [_model],
        MaxContextLength = 200000,
        InputTokenCost = 0.003m,
        OutputTokenCost = 0.015m,
        ApiVersion = AnthropicVersion
    };

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new
            {
                model = _model,
                max_tokens = 1000,
                temperature = 0.7,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var response = await PostAsync<AnthropicResponse>(request, cancellationToken);
            return response.Content?.FirstOrDefault()?.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Anthropic generation failed: {ex.Message}", ex);
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new
            {
                model = _model,
                max_tokens = 10,
                messages = new[]
                {
                    new { role = "user", content = "Hello" }
                }
            };

            await PostAsync<AnthropicResponse>(request, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<T> PostAsync<T>(object requestBody, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(ApiEndpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(responseJson) ?? throw new InvalidOperationException("Failed to deserialize response");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    // DTOs for Anthropic API
    private class AnthropicResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public List<AnthropicContent>? Content { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("usage")]
        public AnthropicUsage? Usage { get; set; }
    }

    private class AnthropicContent
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private class AnthropicUsage
    {
        [JsonPropertyName("input_tokens")]
        public int InputTokens { get; set; }

        [JsonPropertyName("output_tokens")]
        public int OutputTokens { get; set; }
    }
}
