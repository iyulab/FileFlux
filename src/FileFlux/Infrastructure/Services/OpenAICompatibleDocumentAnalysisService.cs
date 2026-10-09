using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace FileFlux.Infrastructure.Services;

/// <summary>
/// IDocumentAnalysisService implementation for OpenAI-compatible APIs.
/// Uses direct HTTP calls (no OpenAI SDK dependency).
/// Supports OpenAI, Azure OpenAI, Ollama, and any OpenAI-compatible endpoint.
/// </summary>
public sealed partial class OpenAICompatibleDocumentAnalysisService
    : IDocumentAnalysisService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger<OpenAICompatibleDocumentAnalysisService> _logger;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Creates a new document analysis service with endpoint configuration.
    /// </summary>
    /// <param name="endpoint">OpenAI-compatible API endpoint (e.g., "https://api.openai.com/v1")</param>
    /// <param name="apiKey">API key for authentication (null for keyless endpoints like local Ollama)</param>
    /// <param name="model">Model identifier (e.g., "gpt-4o", "llama3")</param>
    /// <param name="logger">Logger instance</param>
    public OpenAICompatibleDocumentAnalysisService(
        string endpoint,
        string? apiKey,
        string model,
        ILogger<OpenAICompatibleDocumentAnalysisService> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(logger);

        _model = model;
        _logger = logger;
        _ownsHttpClient = true;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint.TrimEnd('/') + "/")
        };

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    /// <summary>
    /// Creates a new document analysis service with a pre-configured HttpClient (for testing or custom configuration).
    /// </summary>
    /// <param name="httpClient">Pre-configured HttpClient with BaseAddress set</param>
    /// <param name="model">Model identifier</param>
    /// <param name="logger">Logger instance</param>
    public OpenAICompatibleDocumentAnalysisService(
        HttpClient httpClient,
        string model,
        ILogger<OpenAICompatibleDocumentAnalysisService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _model = model;
        _logger = logger;
        _ownsHttpClient = false;
    }

    /// <inheritdoc />
    public DocumentAnalysisServiceInfo ProviderInfo => new()
    {
        Name = "OpenAI-Compatible",
        Type = DocumentAnalysisProviderType.OpenAI,
        SupportedModels = [_model],
        MaxContextLength = 128_000,
        ApiVersion = "v1"
    };

    /// <inheritdoc />
    public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        => GenerateAsync(prompt, GenerationSettings.Default, cancellationToken);

    /// <inheritdoc />
    public async Task<string> GenerateAsync(string prompt, GenerationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        LogAnalysis(_logger, "generate", prompt.Length);
        // The literals are this service's defaults; a caller's option, when set, replaces them (0.25.0).
        var temperature = (float)(settings.Temperature ?? 0.7);
        var maxTokens = settings.MaxTokens is { } m && m > 0 ? m : 1000;
        var (content, finishReason) = await CompleteWithReasonAsync(null, prompt, temperature, maxTokens, cancellationToken);
        // A cut-off answer is not an answer: callers rewrite whole texts with this and cannot tell a truncated rewrite
        // from one that removed content. The availability probe below shares the HTTP path with a 10-token budget and
        // is deliberately not subject to this.
        if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
            throw new GenerationTruncatedException(maxTokens);
        return content;
    }

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await CompleteAsync(null, "Hello", temperature: 0.0f, maxTokens: 10, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    #region HTTP

    private async Task<string> CompleteAsync(
        string? systemPrompt,
        string userPrompt,
        float temperature,
        int maxTokens,
        CancellationToken cancellationToken)
        => (await CompleteWithReasonAsync(systemPrompt, userPrompt, temperature, maxTokens, cancellationToken)).Content;

    private async Task<(string Content, string? FinishReason)> CompleteWithReasonAsync(
        string? systemPrompt,
        string userPrompt,
        float temperature,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        var messages = new List<MessageDto>();

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new MessageDto { Role = "system", Content = systemPrompt });
        }

        messages.Add(new MessageDto { Role = "user", Content = userPrompt });

        var request = new ChatCompletionRequest
        {
            Model = _model,
            Messages = messages,
            Temperature = temperature,
            MaxTokens = maxTokens
        };

        var response = await _httpClient.PostAsJsonAsync(
            "chat/completions", request, ChatJsonContext.Default.ChatCompletionRequest, cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync(
            ChatJsonContext.Default.ChatCompletionResponse, cancellationToken);

        var choice = result?.Choices?.FirstOrDefault();
        return (choice?.Message?.Content ?? string.Empty, choice?.FinishReason);
    }

    #endregion

    #region LoggerMessage

    [LoggerMessage(Level = LogLevel.Debug, Message = "Document analysis [{AnalysisType}] (prompt: {Length} chars)")]
    private static partial void LogAnalysis(ILogger logger, string analysisType, int length);

    #endregion

    #region DTO Models

    /// <summary>
    /// Source-generated (snake_case, nulls omitted, case-insensitive reads) so the service works in an application that
    /// disables reflection-based JSON (trimmed, native AOT, file-based <c>dotnet run app.cs</c>).
    /// </summary>
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(ChatCompletionRequest))]
    [JsonSerializable(typeof(ChatCompletionResponse))]
    internal sealed partial class ChatJsonContext : JsonSerializerContext;

    internal sealed class ChatCompletionRequest
    {
        public string Model { get; init; } = string.Empty;
        public List<MessageDto> Messages { get; init; } = [];
        public float? Temperature { get; init; }
        public int? MaxTokens { get; init; }
    }

    internal sealed class MessageDto
    {
        public string Role { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
    }

    internal sealed class ChatCompletionResponse
    {
        public List<ChoiceDto>? Choices { get; init; }
    }

    internal sealed class ChoiceDto
    {
        public MessageDto? Message { get; init; }
        public string? FinishReason { get; init; }
    }

    #endregion
}
