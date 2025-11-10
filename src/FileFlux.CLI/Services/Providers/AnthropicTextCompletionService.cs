using FileFlux.Core;
using FileFlux.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileFlux.CLI.Services.Providers;

/// <summary>
/// Anthropic Claude API implementation for text completion using direct HTTP API
/// </summary>
public class AnthropicTextCompletionService : ITextCompletionService
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

    public TextCompletionServiceInfo ProviderInfo => new()
    {
        Name = "Anthropic",
        Type = TextCompletionProviderType.Anthropic,
        SupportedModels = new[] { "claude-3-5-sonnet-20241022", "claude-3-5-haiku-20241022", "claude-3-opus-20240229" },
        MaxContextLength = 200000,
        InputTokenCost = 0.003m,
        OutputTokenCost = 0.015m,
        ApiVersion = AnthropicVersion
    };

    public async Task<StructureAnalysisResult> AnalyzeStructureAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new
            {
                model = _model,
                max_tokens = 2000,
                temperature = 0.1,
                system = "You are a document structure analysis expert. Respond only in JSON format.",
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var response = await PostAsync<AnthropicResponse>(request, cancellationToken);
            var content = response.Content?.FirstOrDefault()?.Text ?? string.Empty;
            var tokenUsage = (response.Usage?.InputTokens ?? 0) + (response.Usage?.OutputTokens ?? 0);

            return ParseStructureAnalysisResult(content, documentType, tokenUsage);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Anthropic structure analysis failed: {ex.Message}", ex);
        }
    }

    public async Task<ContentSummary> SummarizeContentAsync(
        string prompt,
        int maxLength = 200,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new
            {
                model = _model,
                max_tokens = 1000,
                temperature = 0.2,
                system = "You are a content summarization expert. Respond only in JSON format.",
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var response = await PostAsync<AnthropicResponse>(request, cancellationToken);
            var content = response.Content?.FirstOrDefault()?.Text ?? string.Empty;
            var tokenUsage = (response.Usage?.InputTokens ?? 0) + (response.Usage?.OutputTokens ?? 0);

            return ParseContentSummary(content, prompt.Length, tokenUsage);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Anthropic summarization failed: {ex.Message}", ex);
        }
    }

    public async Task<MetadataExtractionResult> ExtractMetadataAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new
            {
                model = _model,
                max_tokens = 1500,
                temperature = 0.1,
                system = "You are a metadata extraction expert. Respond only in JSON format.",
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var response = await PostAsync<AnthropicResponse>(request, cancellationToken);
            var content = response.Content?.FirstOrDefault()?.Text ?? string.Empty;
            var tokenUsage = (response.Usage?.InputTokens ?? 0) + (response.Usage?.OutputTokens ?? 0);

            return ParseMetadataExtractionResult(content, tokenUsage);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Anthropic metadata extraction failed: {ex.Message}", ex);
        }
    }

    public async Task<QualityAssessment> AssessQualityAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new
            {
                model = _model,
                max_tokens = 2000,
                temperature = 0.1,
                system = "You are a document quality assessment expert. Respond only in JSON format.",
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var response = await PostAsync<AnthropicResponse>(request, cancellationToken);
            var content = response.Content?.FirstOrDefault()?.Text ?? string.Empty;
            var tokenUsage = (response.Usage?.InputTokens ?? 0) + (response.Usage?.OutputTokens ?? 0);

            return ParseQualityAssessment(content, tokenUsage);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Anthropic quality assessment failed: {ex.Message}", ex);
        }
    }

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

    // Parsing methods (same as OpenAI implementation)
    private static StructureAnalysisResult ParseStructureAnalysisResult(string content, DocumentType documentType, int tokenUsage)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(content);
            var root = jsonDoc.RootElement;

            var result = new StructureAnalysisResult
            {
                DocumentType = documentType,
                Confidence = root.TryGetProperty("confidence", out var conf) ? conf.GetDouble() : 0.8,
                RawResponse = content,
                TokensUsed = tokenUsage
            };

            if (root.TryGetProperty("sections", out var sectionsElement) && sectionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var sectionElement in sectionsElement.EnumerateArray())
                {
                    var section = new SectionInfo
                    {
                        Type = ParseSectionType(sectionElement.GetProperty("type").GetString()),
                        Title = sectionElement.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                        StartPosition = sectionElement.TryGetProperty("startPosition", out var start) ? start.GetInt32() : 0,
                        EndPosition = sectionElement.TryGetProperty("endPosition", out var end) ? end.GetInt32() : 0,
                        Level = sectionElement.TryGetProperty("level", out var level) ? level.GetInt32() : 1,
                        Importance = sectionElement.TryGetProperty("importance", out var imp) ? imp.GetDouble() : 0.5
                    };
                    result.Sections.Add(section);
                }
            }

            result.Structure = CreateDocumentStructure(result.Sections);
            return result;
        }
        catch (JsonException)
        {
            return new StructureAnalysisResult
            {
                DocumentType = documentType,
                Confidence = 0.5,
                RawResponse = content,
                TokensUsed = tokenUsage
            };
        }
    }

    private static ContentSummary ParseContentSummary(string content, int originalLength, int tokenUsage)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(content);
            var root = jsonDoc.RootElement;

            return new ContentSummary
            {
                Summary = root.GetProperty("summary").GetString() ?? "",
                Keywords = root.TryGetProperty("keywords", out var keywords)
                    ? keywords.EnumerateArray().Select(k => k.GetString() ?? "").ToArray()
                    : Array.Empty<string>(),
                Confidence = root.TryGetProperty("confidence", out var conf) ? conf.GetDouble() : 0.8,
                OriginalLength = originalLength,
                TokensUsed = tokenUsage
            };
        }
        catch (JsonException)
        {
            return new ContentSummary
            {
                Summary = content.Length > 200 ? content[..200] : content,
                Confidence = 0.5,
                OriginalLength = originalLength,
                TokensUsed = tokenUsage
            };
        }
    }

    private static MetadataExtractionResult ParseMetadataExtractionResult(string content, int tokenUsage)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(content);
            var root = jsonDoc.RootElement;

            var result = new MetadataExtractionResult
            {
                Keywords = root.TryGetProperty("keywords", out var keywords)
                    ? keywords.EnumerateArray().Select(k => k.GetString() ?? "").ToArray()
                    : Array.Empty<string>(),
                Language = root.TryGetProperty("language", out var lang) ? lang.GetString() : null,
                Categories = root.TryGetProperty("categories", out var cats)
                    ? cats.EnumerateArray().Select(c => c.GetString() ?? "").ToArray()
                    : Array.Empty<string>(),
                Confidence = root.TryGetProperty("confidence", out var conf) ? conf.GetDouble() : 0.8,
                TokensUsed = tokenUsage
            };

            if (root.TryGetProperty("technicalMetadata", out var techMeta) && techMeta.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in techMeta.EnumerateObject())
                {
                    result.TechnicalMetadata[prop.Name] = prop.Value.GetString() ?? "";
                }
            }

            if (root.TryGetProperty("entities", out var entities) && entities.ValueKind == JsonValueKind.Object)
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
            return new MetadataExtractionResult
            {
                Confidence = 0.5,
                TokensUsed = tokenUsage
            };
        }
    }

    private static QualityAssessment ParseQualityAssessment(string content, int tokenUsage)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(content);
            var root = jsonDoc.RootElement;

            var result = new QualityAssessment
            {
                ConfidenceScore = root.TryGetProperty("confidenceScore", out var confScore) ? confScore.GetDouble() : 0.8,
                CompletenessScore = root.TryGetProperty("completenessScore", out var compScore) ? compScore.GetDouble() : 0.8,
                ConsistencyScore = root.TryGetProperty("consistencyScore", out var consScore) ? consScore.GetDouble() : 0.8,
                Explanation = root.TryGetProperty("explanation", out var exp) ? exp.GetString() ?? "" : "",
                TokensUsed = tokenUsage
            };

            if (root.TryGetProperty("recommendations", out var recommendations) && recommendations.ValueKind == JsonValueKind.Array)
            {
                foreach (var recElement in recommendations.EnumerateArray())
                {
                    var recommendation = new QualityRecommendation
                    {
                        Type = ParseRecommendationType(recElement.GetProperty("type").GetString()),
                        Description = recElement.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                        SuggestedValue = recElement.TryGetProperty("suggestedValue", out var sugg) ? sugg.GetString() : null,
                        Priority = recElement.TryGetProperty("priority", out var prio) ? prio.GetInt32() : 5
                    };
                    result.Recommendations.Add(recommendation);
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
                Explanation = "JSON parsing failed - default assessment",
                TokensUsed = tokenUsage
            };
        }
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
            "CHUNK_SIZE_OPTIMIZATION" => RecommendationType.CHUNK_SIZE_OPTIMIZATION,
            "METADATA_ENHANCEMENT" => RecommendationType.METADATA_ENHANCEMENT,
            "TITLE_IMPROVEMENT" => RecommendationType.TITLE_IMPROVEMENT,
            "DESCRIPTION_ENHANCEMENT" => RecommendationType.DESCRIPTION_ENHANCEMENT,
            "CONTEXT_ADDITION" => RecommendationType.CONTEXT_ADDITION,
            "STRUCTURE_IMPROVEMENT" => RecommendationType.STRUCTURE_IMPROVEMENT,
            _ => RecommendationType.METADATA_ENHANCEMENT
        };
    }

    private static CoreDocumentStructure CreateDocumentStructure(List<SectionInfo> sections)
    {
        var structure = new CoreDocumentStructure();

        if (sections.Count != 0)
        {
            structure.AllSections = sections;
            structure.Root = sections.FirstOrDefault(s => s.Level == 1) ?? sections.First();

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
        }

        return structure;
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
