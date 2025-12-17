using FileFlux.Core;
using FileFlux.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileFlux.CLI.Services.Providers;

/// <summary>
/// Google Gemini API implementation for text completion using direct HTTP API
/// </summary>
public class GoogleTextCompletionService : ITextCompletionService
{
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

    public TextCompletionServiceInfo ProviderInfo => new()
    {
        Name = "Google Gemini",
        Type = TextCompletionProviderType.Google,
        SupportedModels = new[] { "gemini-2.0-flash", "gemini-2.5-flash", "gemini-2.5-flash-lite", "gemini-2.5-pro" },
        MaxContextLength = 1000000,
        InputTokenCost = 0.00015m,
        OutputTokenCost = 0.0006m,
        ApiVersion = "v1beta"
    };

    public async Task<StructureAnalysisResult> AnalyzeStructureAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
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
                SystemInstruction = new GeminiContent
                {
                    Parts = new[] { new GeminiPart { Text = "You are a document structure analysis expert. Respond only in JSON format." } }
                },
                GenerationConfig = new GeminiGenerationConfig
                {
                    Temperature = 0.1,
                    MaxOutputTokens = 2000
                }
            };

            var response = await PostAsync<GeminiResponse>(request, cancellationToken);
            var content = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
            var tokenUsage = (response.UsageMetadata?.PromptTokenCount ?? 0) + (response.UsageMetadata?.CandidatesTokenCount ?? 0);

            return ParseStructureAnalysisResult(content, documentType, tokenUsage);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Google Gemini structure analysis failed: {ex.Message}", ex);
        }
    }

    public async Task<ContentSummary> SummarizeContentAsync(
        string prompt,
        int maxLength = 200,
        CancellationToken cancellationToken = default)
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
                SystemInstruction = new GeminiContent
                {
                    Parts = new[] { new GeminiPart { Text = "You are a content summarization expert. Respond only in JSON format." } }
                },
                GenerationConfig = new GeminiGenerationConfig
                {
                    Temperature = 0.2,
                    MaxOutputTokens = 1000
                }
            };

            var response = await PostAsync<GeminiResponse>(request, cancellationToken);
            var content = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
            var tokenUsage = (response.UsageMetadata?.PromptTokenCount ?? 0) + (response.UsageMetadata?.CandidatesTokenCount ?? 0);

            return ParseContentSummary(content, prompt.Length, tokenUsage);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Google Gemini summarization failed: {ex.Message}", ex);
        }
    }

    public async Task<MetadataExtractionResult> ExtractMetadataAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
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
                SystemInstruction = new GeminiContent
                {
                    Parts = new[] { new GeminiPart { Text = "You are a metadata extraction expert. Respond only in JSON format." } }
                },
                GenerationConfig = new GeminiGenerationConfig
                {
                    Temperature = 0.1,
                    MaxOutputTokens = 1500
                }
            };

            var response = await PostAsync<GeminiResponse>(request, cancellationToken);
            var content = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
            var tokenUsage = (response.UsageMetadata?.PromptTokenCount ?? 0) + (response.UsageMetadata?.CandidatesTokenCount ?? 0);

            return ParseMetadataExtractionResult(content, tokenUsage);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Google Gemini metadata extraction failed: {ex.Message}", ex);
        }
    }

    public async Task<QualityAssessment> AssessQualityAsync(
        string prompt,
        CancellationToken cancellationToken = default)
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
                SystemInstruction = new GeminiContent
                {
                    Parts = new[] { new GeminiPart { Text = "You are a document quality assessment expert. Respond only in JSON format." } }
                },
                GenerationConfig = new GeminiGenerationConfig
                {
                    Temperature = 0.1,
                    MaxOutputTokens = 2000
                }
            };

            var response = await PostAsync<GeminiResponse>(request, cancellationToken);
            var content = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
            var tokenUsage = (response.UsageMetadata?.PromptTokenCount ?? 0) + (response.UsageMetadata?.CandidatesTokenCount ?? 0);

            return ParseQualityAssessment(content, tokenUsage);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Google Gemini quality assessment failed: {ex.Message}", ex);
        }
    }

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
        var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(responseJson, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }) ?? throw new InvalidOperationException("Failed to deserialize response");
    }

    // Parsing methods
    private static StructureAnalysisResult ParseStructureAnalysisResult(string content, DocumentType documentType, int tokenUsage)
    {
        try
        {
            // Extract JSON from potential markdown code block
            content = ExtractJsonFromResponse(content);
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
            content = ExtractJsonFromResponse(content);
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
            content = ExtractJsonFromResponse(content);
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
            content = ExtractJsonFromResponse(content);
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

    private static string ExtractJsonFromResponse(string content)
    {
        // Handle markdown code blocks that Gemini sometimes returns
        if (content.Contains("```json"))
        {
            var start = content.IndexOf("```json", StringComparison.Ordinal) + 7;
            var end = content.IndexOf("```", start, StringComparison.Ordinal);
            if (end > start)
            {
                content = content[start..end].Trim();
            }
        }
        else if (content.Contains("```"))
        {
            var start = content.IndexOf("```", StringComparison.Ordinal) + 3;
            var end = content.IndexOf("```", start, StringComparison.Ordinal);
            if (end > start)
            {
                content = content[start..end].Trim();
            }
        }
        return content;
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
