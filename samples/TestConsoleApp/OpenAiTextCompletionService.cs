using FileFlux;
using FileFlux.Domain;
using CoreDocumentStructure = FileFlux.CoreDocumentStructure;
using OpenAI;
using OpenAI.Chat;
using System.Text.Json;

namespace TestConsoleApp;

/// <summary>
/// TestConsoleApp용 OpenAI 텍스트 완성 서비스 구현
/// FileFlux SampleApp의 구현을 참조하되 TestConsoleApp에 맞게 단순화
/// </summary>
public class OpenAITextGenerationService : IDocumentAnalysisService
{
    private readonly ChatClient _chatClient;

    public OpenAITextGenerationService(ChatClient chatClient)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
    }

    public DocumentAnalysisServiceInfo ProviderInfo => new()
    {
        Name = "OpenAI",
        Type = DocumentAnalysisProviderType.OpenAI,
        SupportedModels = new[] { "gpt-5-nano", "gpt-4o" },
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

            // 기본 응답 반환 (간단한 구현)
            return new StructureAnalysisResult
            {
                DocumentType = documentType,
                Confidence = 0.85,
                RawResponse = content,
                TokensUsed = tokenUsage,
                Sections = new List<SectionInfo>
                {
                    new SectionInfo
                    {
                        Type = SectionType.HeadingL1,
                        Title = "Document Content",
                        StartPosition = 0,
                        EndPosition = prompt.Length,
                        Level = 1,
                        Importance = 0.8
                    }
                },
                Structure = new CoreDocumentStructure
                {
                    Root = new SectionInfo { Type = SectionType.HeadingL1, Title = "Root" },
                    AllSections = new List<SectionInfo>()
                }
            };
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
                new SystemChatMessage("다음 내용을 간결하게 요약해주세요."),
                new UserChatMessage(prompt)
            };

            var response = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                Temperature = 0.2f,
                MaxOutputTokenCount = 500
            }, cancellationToken);

            var content = response.Value.Content[0].Text;
            var tokenUsage = response.Value.Usage?.TotalTokenCount ?? 0;

            return new ContentSummary
            {
                Summary = content.Length > maxLength ? content.Substring(0, maxLength) : content,
                Keywords = new[] { "document", "content", "summary" },
                Confidence = 0.8,
                OriginalLength = prompt.Length,
                TokensUsed = tokenUsage
            };
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
                new SystemChatMessage("이 문서의 메타데이터를 추출해주세요. 키워드, 언어, 카테고리를 분석해주세요."),
                new UserChatMessage(prompt)
            };

            var response = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                Temperature = 0.1f,
                MaxOutputTokenCount = 1000
            }, cancellationToken);

            var content = response.Value.Content[0].Text;
            var tokenUsage = response.Value.Usage?.TotalTokenCount ?? 0;

            return new MetadataExtractionResult
            {
                Keywords = new[] { "document", "analysis", "content" },
                Language = "ko",
                Categories = new[] { "business" },
                Entities = new Dictionary<string, string[]>(),
                TechnicalMetadata = new Dictionary<string, string>(),
                Confidence = 0.8,
                TokensUsed = tokenUsage
            };
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
                new SystemChatMessage("이 텍스트 청크의 품질을 평가해주세요. 완성도, 일관성, 신뢰도를 0-1 스케일로 평가해주세요."),
                new UserChatMessage(prompt)
            };

            var response = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                Temperature = 0.1f,
                MaxOutputTokenCount = 1000
            }, cancellationToken);

            var tokenUsage = response.Value.Usage?.TotalTokenCount ?? 0;

            return new QualityAssessment
            {
                ConfidenceScore = 0.85,
                CompletenessScore = 0.8,
                ConsistencyScore = 0.9,
                Recommendations = new List<QualityRecommendation>
                {
                    new QualityRecommendation
                    {
                        Type = RecommendationType.MetadataEnhancement,
                        Description = "OpenAI로 품질 평가 완료",
                        Priority = 5
                    }
                },
                Explanation = "OpenAI 기반 품질 평가 완료",
                TokensUsed = tokenUsage
            };
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