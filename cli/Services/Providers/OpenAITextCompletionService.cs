using FileFlux.Core;
using FileFlux.Domain;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text.Json;

namespace FileFlux.CLI.Services.Providers;

/// <summary>
/// OpenAI text completion service implementation for CLI
/// </summary>
public class OpenAITextCompletionService : ITextCompletionService
{
    private readonly ChatClient _chatClient;

    public OpenAITextCompletionService(string apiKey, string model, string? endpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        OpenAIClient client;
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            // Custom endpoint for OpenAI-compatible APIs (e.g., GPU-Stack)
            var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
            client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
        }
        else
        {
            client = new OpenAIClient(apiKey);
        }
        _chatClient = client.GetChatClient(model);
    }

    public TextCompletionServiceInfo ProviderInfo => new()
    {
        Name = "OpenAI",
        Type = TextCompletionProviderType.OpenAI,
        SupportedModels = new[] { "gpt-5-nano", "gpt-4o", "gpt-4o-mini" },
        MaxContextLength = 128000,
        InputTokenCost = 0.00015m,
        OutputTokenCost = 0.0006m,
        ApiVersion = "2024-08-01"
    };

    public async Task<StructureAnalysisResult> AnalyzeStructureAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are a document structure analysis expert. Respond only in JSON format."),
                new UserChatMessage(prompt)
            };

            var response = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                Temperature = 0.1f,
                MaxOutputTokenCount = 2000
            }, cancellationToken);

            var content = response.Value.Content[0].Text;
            var tokenUsage = response.Value.Usage?.TotalTokenCount ?? 0;

            return ParseStructureAnalysisResult(content, documentType, tokenUsage);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"OpenAI structure analysis failed: {ex.Message}", ex);
        }
    }

    public async Task<ContentSummary> SummarizeContentAsync(
        string prompt,
        int maxLength = 200,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are a content summarization expert. Respond only in JSON format."),
                new UserChatMessage(prompt)
            };

            var response = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                Temperature = 0.2f,
                MaxOutputTokenCount = 1000
            }, cancellationToken);

            var content = response.Value.Content[0].Text;
            var tokenUsage = response.Value.Usage?.TotalTokenCount ?? 0;

            return ParseContentSummary(content, prompt.Length, tokenUsage);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"OpenAI summarization failed: {ex.Message}", ex);
        }
    }

    public async Task<MetadataExtractionResult> ExtractMetadataAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are a metadata extraction expert. Respond only in JSON format."),
                new UserChatMessage(prompt)
            };

            var response = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                Temperature = 0.1f,
                MaxOutputTokenCount = 1500
            }, cancellationToken);

            var content = response.Value.Content[0].Text;
            var tokenUsage = response.Value.Usage?.TotalTokenCount ?? 0;

            return ParseMetadataExtractionResult(content, tokenUsage);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"OpenAI metadata extraction failed: {ex.Message}", ex);
        }
    }

    public async Task<QualityAssessment> AssessQualityAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are a document quality assessment expert. Respond only in JSON format."),
                new UserChatMessage(prompt)
            };

            var response = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                Temperature = 0.1f,
                MaxOutputTokenCount = 2000
            }, cancellationToken);

            var content = response.Value.Content[0].Text;
            var tokenUsage = response.Value.Usage?.TotalTokenCount ?? 0;

            return ParseQualityAssessment(content, tokenUsage);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"OpenAI quality assessment failed: {ex.Message}", ex);
        }
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = new List<ChatMessage>
            {
                new UserChatMessage(prompt)
            };

            var response = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                Temperature = 0.7f,
                MaxOutputTokenCount = 1000
            }, cancellationToken);

            return response.Value?.Content?.FirstOrDefault()?.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"OpenAI generation failed: {ex.Message}", ex);
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = new List<ChatMessage>
            {
                new UserChatMessage("Hello")
            };

            var response = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                MaxOutputTokenCount = 10
            }, cancellationToken);

            return response.Value != null;
        }
        catch
        {
            return false;
        }
    }

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
}
