using FileFlux;
using FileFlux.Domain;
using CoreDocumentStructure = FileFlux.CoreDocumentStructure;
using OpenAI;
using OpenAI.Chat;
using System.Text.Json;

namespace FileFlux.SampleApp.Services;

/// <summary>
/// SampleApp용 OpenAI 텍스트 완성 서비스 구현
/// FileFlux의 역할 분리 원칙에 따라 소비 애플리케이션에서 구현
/// </summary>
public class OpenAiTextCompletionService : ITextCompletionService
{
    private readonly ChatClient _chatClient;

    public OpenAiTextCompletionService(ChatClient chatClient)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
    }

    public TextCompletionServiceInfo ProviderInfo => new()
    {
        Name = "OpenAI",
        Type = TextCompletionProviderType.OpenAI,
        SupportedModels = new[] { "gpt-5-nano", "gpt-4o", "gpt-3.5-turbo" },
        MaxContextLength = 128000,
        InputTokenCost = 0.00015m, // gpt-5-nano 가격
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
                new SystemChatMessage("당신은 문서 구조 분석 전문가입니다. JSON 형식으로만 응답해주세요."),
                new UserChatMessage(prompt)
            };

            var response = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                Temperature = 0.1f,
                MaxOutputTokenCount = 2000
            }, cancellationToken);

            var content = response.Value.Content[0].Text;
            var tokenUsage = response.Value.Usage?.TotalTokenCount ?? 0;

            // JSON 응답 파싱 시도
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

                // 섹션 정보 추출
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

                // 문서 구조 생성
                result.Structure = CreateDocumentStructure(result.Sections);

                return result;
            }
            catch (JsonException)
            {
                // JSON 파싱 실패 시 기본 응답 반환
                return new StructureAnalysisResult
                {
                    DocumentType = documentType,
                    Confidence = 0.5,
                    RawResponse = content,
                    TokensUsed = tokenUsage
                };
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"OpenAI 구조 분석 실패: {ex.Message}", ex);
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
                new SystemChatMessage("당신은 내용 요약 전문가입니다. JSON 형식으로만 응답해주세요."),
                new UserChatMessage(prompt)
            };

            var response = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                Temperature = 0.2f,
                MaxOutputTokenCount = 1000
            }, cancellationToken);

            var content = response.Value.Content[0].Text;
            var tokenUsage = response.Value.Usage?.TotalTokenCount ?? 0;

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
                    OriginalLength = prompt.Length,
                    TokensUsed = tokenUsage
                };
            }
            catch (JsonException)
            {
                // JSON 파싱 실패 시 기본 응답
                return new ContentSummary
                {
                    Summary = content.Length > maxLength ? content.Substring(0, maxLength) : content,
                    Confidence = 0.5,
                    TokensUsed = tokenUsage
                };
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"OpenAI 요약 실패: {ex.Message}", ex);
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
                new SystemChatMessage("당신은 메타데이터 추출 전문가입니다. JSON 형식으로만 응답해주세요."),
                new UserChatMessage(prompt)
            };

            var response = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                Temperature = 0.1f,
                MaxOutputTokenCount = 1500
            }, cancellationToken);

            var content = response.Value.Content[0].Text;
            var tokenUsage = response.Value.Usage?.TotalTokenCount ?? 0;

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

                // 기술적 메타데이터 추출
                if (root.TryGetProperty("technicalMetadata", out var techMeta) && techMeta.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in techMeta.EnumerateObject())
                    {
                        result.TechnicalMetadata[prop.Name] = prop.Value.GetString() ?? "";
                    }
                }

                // 엔티티 추출
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
        catch (Exception ex)
        {
            throw new InvalidOperationException($"OpenAI 메타데이터 추출 실패: {ex.Message}", ex);
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
                new SystemChatMessage("당신은 문서 품질 평가 전문가입니다. JSON 형식으로만 응답해주세요."),
                new UserChatMessage(prompt)
            };

            var response = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                Temperature = 0.1f,
                MaxOutputTokenCount = 2000
            }, cancellationToken);

            var content = response.Value.Content[0].Text;
            var tokenUsage = response.Value.Usage?.TotalTokenCount ?? 0;

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

                // 권장사항 추출
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
                    Explanation = "JSON 파싱 실패로 인한 기본 평가",
                    TokensUsed = tokenUsage
                };
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"OpenAI 품질 평가 실패: {ex.Message}", ex);
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

    private static SectionType ParseSectionType(string? typeString)
    {
        return typeString?.ToUpperInvariant() switch
        {
            "HEADING_L1" or "TITLE" => SectionType.HEADING_L1,
            "HEADING_L2" or "SUBTITLE" => SectionType.HEADING_L2,
            "HEADING_L3" => SectionType.HEADING_L3,
            "PARAGRAPH" => SectionType.PARAGRAPH,
            "CODE_BLOCK" => SectionType.CODE_BLOCK,
            "LIST" => SectionType.LIST,
            "TABLE" => SectionType.TABLE,
            "IMAGE" => SectionType.IMAGE,
            "API_ENDPOINT" => SectionType.API_ENDPOINT,
            "CLASS" => SectionType.CLASS,
            "METHOD" => SectionType.METHOD,
            "EXAMPLE" => SectionType.EXAMPLE,
            "COMMENT" => SectionType.COMMENT,
            "NAVIGATION" => SectionType.NAVIGATION,
            "ARTICLE" => SectionType.ARTICLE,
            "ASIDE" => SectionType.ASIDE,
            "FOOTER" => SectionType.FOOTER,
            _ => SectionType.PARAGRAPH
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

            // 루트 섹션 찾기 (레벨 1이면서 첫 번째)
            structure.Root = sections.FirstOrDefault(s => s.Level == 1) ?? sections.First();

            // 섹션 관계 구성 (간단한 구현)
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

    /// <summary>
    /// 범용 LLM 호출 (BasicDocumentParser 호환성을 위한 간단한 인터페이스)
    /// </summary>
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
            throw new InvalidOperationException($"OpenAI LLM 호출 실패: {ex.Message}", ex);
        }
    }
}