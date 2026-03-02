using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileFlux.Domain;
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

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
    public async Task<StructureAnalysisResult> AnalyzeStructureAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        LogAnalysis(_logger, "structure", prompt.Length);

        var content = await CompleteAsync(
            "You are a document structure analysis expert. Respond only in JSON format.",
            prompt, temperature: 0.1f, maxTokens: 2000, cancellationToken);

        return ParseStructureAnalysisResult(content, documentType);
    }

    /// <inheritdoc />
    public async Task<ContentSummary> SummarizeContentAsync(
        string prompt,
        int maxLength = 200,
        CancellationToken cancellationToken = default)
    {
        LogAnalysis(_logger, "summarization", prompt.Length);

        var content = await CompleteAsync(
            "You are a content summarization expert. Respond only in JSON format.",
            prompt, temperature: 0.2f, maxTokens: 1000, cancellationToken);

        return ParseContentSummary(content, prompt.Length);
    }

    /// <inheritdoc />
    public async Task<MetadataExtractionResult> ExtractMetadataAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        LogAnalysis(_logger, "metadata", prompt.Length);

        var content = await CompleteAsync(
            "You are a metadata extraction expert. Respond only in JSON format.",
            prompt, temperature: 0.1f, maxTokens: 1500, cancellationToken);

        return ParseMetadataExtractionResult(content);
    }

    /// <inheritdoc />
    public async Task<QualityAssessment> AssessQualityAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        LogAnalysis(_logger, "quality", prompt.Length);

        var content = await CompleteAsync(
            "You are a document quality assessment expert. Respond only in JSON format.",
            prompt, temperature: 0.1f, maxTokens: 2000, cancellationToken);

        return ParseQualityAssessment(content);
    }

    /// <inheritdoc />
    public async Task<string> GenerateAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        LogAnalysis(_logger, "generate", prompt.Length);
        return await CompleteAsync(null, prompt, temperature: 0.7f, maxTokens: 1000, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await CompleteAsync(null, "Hello", temperature: 0.0f, maxTokens: 10, cancellationToken);
            return true;
        }
        catch
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
            "chat/completions", request, JsonOptions, cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(
            JsonOptions, cancellationToken);

        return result?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
    }

    #endregion

    #region JSON Parsing

    private static StructureAnalysisResult ParseStructureAnalysisResult(
        string content, DocumentType documentType)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(ExtractJson(content));
            var root = jsonDoc.RootElement;

            var result = new StructureAnalysisResult
            {
                DocumentType = documentType,
                Confidence = root.TryGetProperty("confidence", out var conf)
                    ? conf.GetDouble() : 0.8,
                RawResponse = content,
            };

            if (root.TryGetProperty("sections", out var sections) &&
                sections.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in sections.EnumerateArray())
                {
                    result.Sections.Add(new SectionInfo
                    {
                        Type = ParseSectionType(
                            s.TryGetProperty("type", out var t) ? t.GetString() : null),
                        Title = s.TryGetProperty("title", out var title)
                            ? title.GetString() ?? "" : "",
                        StartPosition = s.TryGetProperty("startPosition", out var sp)
                            ? sp.GetInt32() : 0,
                        EndPosition = s.TryGetProperty("endPosition", out var ep)
                            ? ep.GetInt32() : 0,
                        Level = s.TryGetProperty("level", out var lv)
                            ? lv.GetInt32() : 1,
                        Importance = s.TryGetProperty("importance", out var imp)
                            ? imp.GetDouble() : 0.5
                    });
                }
            }

            result.Structure = BuildStructure(result.Sections);
            return result;
        }
        catch (JsonException)
        {
            return new StructureAnalysisResult
            {
                DocumentType = documentType,
                Confidence = 0.5,
                RawResponse = content
            };
        }
    }

    private static ContentSummary ParseContentSummary(string content, int originalLength)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(ExtractJson(content));
            var root = jsonDoc.RootElement;

            return new ContentSummary
            {
                Summary = root.TryGetProperty("summary", out var sum)
                    ? sum.GetString() ?? "" : "",
                Keywords = root.TryGetProperty("keywords", out var kw)
                    ? kw.EnumerateArray().Select(k => k.GetString() ?? "").ToArray()
                    : [],
                Confidence = root.TryGetProperty("confidence", out var conf)
                    ? conf.GetDouble() : 0.8,
                OriginalLength = originalLength
            };
        }
        catch (JsonException)
        {
            return new ContentSummary
            {
                Summary = content.Length > 200 ? content[..200] : content,
                Confidence = 0.5,
                OriginalLength = originalLength
            };
        }
    }

    private static MetadataExtractionResult ParseMetadataExtractionResult(string content)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(ExtractJson(content));
            var root = jsonDoc.RootElement;

            var result = new MetadataExtractionResult
            {
                Keywords = root.TryGetProperty("keywords", out var kw)
                    ? kw.EnumerateArray().Select(k => k.GetString() ?? "").ToArray()
                    : [],
                Language = root.TryGetProperty("language", out var lang)
                    ? lang.GetString() : null,
                Categories = root.TryGetProperty("categories", out var cats)
                    ? cats.EnumerateArray().Select(c => c.GetString() ?? "").ToArray()
                    : [],
                Confidence = root.TryGetProperty("confidence", out var conf)
                    ? conf.GetDouble() : 0.8
            };

            if (root.TryGetProperty("technicalMetadata", out var tech) &&
                tech.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in tech.EnumerateObject())
                {
                    result.TechnicalMetadata[prop.Name] = prop.Value.GetString() ?? "";
                }
            }

            if (root.TryGetProperty("entities", out var entities) &&
                entities.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in entities.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        result.Entities[prop.Name] = prop.Value.EnumerateArray()
                            .Select(e => e.GetString() ?? "")
                            .ToArray();
                    }
                }
            }

            return result;
        }
        catch (JsonException)
        {
            return new MetadataExtractionResult { Confidence = 0.5 };
        }
    }

    private static QualityAssessment ParseQualityAssessment(string content)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(ExtractJson(content));
            var root = jsonDoc.RootElement;

            var result = new QualityAssessment
            {
                ConfidenceScore = root.TryGetProperty("confidenceScore", out var cs)
                    ? cs.GetDouble() : 0.8,
                CompletenessScore = root.TryGetProperty("completenessScore", out var comp)
                    ? comp.GetDouble() : 0.8,
                ConsistencyScore = root.TryGetProperty("consistencyScore", out var cons)
                    ? cons.GetDouble() : 0.8,
                Explanation = root.TryGetProperty("explanation", out var exp)
                    ? exp.GetString() ?? "" : ""
            };

            if (root.TryGetProperty("recommendations", out var recs) &&
                recs.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in recs.EnumerateArray())
                {
                    result.Recommendations.Add(new QualityRecommendation
                    {
                        Type = ParseRecommendationType(
                            r.TryGetProperty("type", out var rt) ? rt.GetString() : null),
                        Description = r.TryGetProperty("description", out var desc)
                            ? desc.GetString() ?? "" : "",
                        SuggestedValue = r.TryGetProperty("suggestedValue", out var sv)
                            ? sv.GetString() : null,
                        Priority = r.TryGetProperty("priority", out var prio)
                            ? prio.GetInt32() : 5
                    });
                }
            }

            return result;
        }
        catch (JsonException)
        {
            return new QualityAssessment
            {
                ConfidenceScore = 0.8,
                CompletenessScore = 0.8,
                ConsistencyScore = 0.8,
                Explanation = "JSON parsing failed — default assessment"
            };
        }
    }

    /// <summary>
    /// Extracts JSON from LLM response that may be wrapped in markdown code blocks.
    /// </summary>
    internal static string ExtractJson(string content)
    {
        var trimmed = content.Trim();

        // Handle ```json ... ``` blocks
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }

            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence > 0)
            {
                trimmed = trimmed[..lastFence];
            }
        }

        return trimmed;
    }

    private static SectionType ParseSectionType(string? typeString)
    {
        return typeString?.ToUpperInvariant() switch
        {
            "HEADING_L1" or "TITLE" => SectionType.HeadingL1,
            "HEADING_L2" or "SUBTITLE" => SectionType.HeadingL2,
            "HEADING_L3" => SectionType.HeadingL3,
            "PARAGRAPH" => SectionType.Paragraph,
            "CODE_BLOCK" => SectionType.CodeBlock,
            "LIST" => SectionType.List,
            "TABLE" => SectionType.Table,
            "IMAGE" => SectionType.Image,
            _ => SectionType.Paragraph
        };
    }

    private static RecommendationType ParseRecommendationType(string? typeString)
    {
        return typeString?.ToUpperInvariant() switch
        {
            "CHUNK_SIZE_OPTIMIZATION" => RecommendationType.ChunkSizeOptimization,
            "METADATA_ENHANCEMENT" => RecommendationType.MetadataEnhancement,
            "TITLE_IMPROVEMENT" => RecommendationType.TitleImprovement,
            "DESCRIPTION_ENHANCEMENT" => RecommendationType.DescriptionEnhancement,
            "CONTEXT_ADDITION" => RecommendationType.ContextAddition,
            "STRUCTURE_IMPROVEMENT" => RecommendationType.StructureImprovement,
            _ => RecommendationType.MetadataEnhancement
        };
    }

    private static CoreDocumentStructure BuildStructure(List<SectionInfo> sections)
    {
        var structure = new CoreDocumentStructure();

        if (sections.Count == 0)
            return structure;

        structure.AllSections = sections;
        structure.Root = sections.Find(s => s.Level == 1) ?? sections[0];

        foreach (var section in sections)
        {
            var children = sections
                .Where(s => s.Level == section.Level + 1 &&
                           s.StartPosition > section.StartPosition &&
                           s.StartPosition < section.EndPosition)
                .Select(s => s.Title)
                .ToList();

            if (children.Count != 0)
            {
                structure.SectionRelations[section.Title] = children;
            }
        }

        return structure;
    }

    #endregion

    #region LoggerMessage

    [LoggerMessage(Level = LogLevel.Debug, Message = "Document analysis [{AnalysisType}] (prompt: {Length} chars)")]
    private static partial void LogAnalysis(ILogger logger, string analysisType, int length);

    #endregion

    #region DTO Models

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
    }

    #endregion
}
