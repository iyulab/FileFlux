using FileFlux;
using FileFlux.Domain;

namespace FileFlux.Tests.Mocks;

/// <summary>
/// 테스트용 Mock Text Completion Service
/// </summary>
public class MockTextCompletionService : ITextCompletionService
{
    public TextCompletionServiceInfo ProviderInfo { get; } = new()
    {
        Name = "Mock Service",
        Type = TextCompletionProviderType.Custom,
        SupportedModels = new[] { "mock-model" },
        MaxContextLength = 4096
    };
    private static readonly string[] result = new[] { "test", "mock", "sample" };

    public Task<StructureAnalysisResult> AnalyzeStructureAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new StructureAnalysisResult
        {
            DocumentType = documentType,
            Sections = new List<SectionInfo>
            {
                new SectionInfo
                {
                    Type = SectionType.HEADING_L1,
                    Title = "Test Section",
                    StartPosition = 0,
                    EndPosition = 100,
                    Level = 1,
                    Importance = 0.8
                }
            },
            Structure = new FileFlux.CoreDocumentStructure
            {
                Root = new SectionInfo { Type = SectionType.HEADING_L1, Title = "Root" },
                AllSections = new List<SectionInfo>()
            },
            Confidence = 0.8,
            TokensUsed = 100
        });
    }

    public Task<ContentSummary> SummarizeContentAsync(
        string prompt,
        int maxLength = 200,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ContentSummary
        {
            Summary = "Mock summary for testing purposes",
            Keywords = result,
            Confidence = 0.8,
            OriginalLength = prompt.Length,
            TokensUsed = 50
        });
    }

    public Task<MetadataExtractionResult> ExtractMetadataAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new MetadataExtractionResult
        {
            Keywords = new[] { "test", "mock" },
            Language = "en",
            Categories = new[] { "test" },
            Entities = new Dictionary<string, string[]>(),
            TechnicalMetadata = new Dictionary<string, string>(),
            Confidence = 0.9,
            TokensUsed = 75
        });
    }

    public Task<QualityAssessment> AssessQualityAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new QualityAssessment
        {
            ConfidenceScore = 0.85,
            CompletenessScore = 0.8,
            ConsistencyScore = 0.9,
            Recommendations = new List<QualityRecommendation>
            {
                new QualityRecommendation
                {
                    Type = RecommendationType.CHUNK_SIZE_OPTIMIZATION,
                    Description = "Mock recommendation",
                    Priority = 5
                }
            },
            Explanation = "Mock quality assessment",
            TokensUsed = 60
        });
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        // 실제 LLM처럼 프롬프트를 분석하여 의미있는 응답 생성
        if (prompt.Contains("문서 구조화") || prompt.Contains("TOPIC:") || prompt.Contains("KEYWORDS:"))
        {
            return GenerateIntelligentStructureResponse(prompt);
        }

        return Task.FromResult($"Mock response to: {prompt.Substring(0, Math.Min(50, prompt.Length))}...");
    }

    private static Task<string> GenerateIntelligentStructureResponse(string prompt)
    {
        // Markdown 내용에서 실제 구조 정보 추출
        var hasRequirements = prompt.Contains("요구사항") || prompt.Contains("FR-");
        var hasStack = prompt.Contains("기술 스택") || prompt.Contains(".NET") || prompt.Contains("GPUStack");
        var hasInfra = prompt.Contains("인프라") || prompt.Contains("Docker") || prompt.Contains("GPU");

        var topic = "";
        var keywords = new List<string>();
        var summary = "";

        if (hasRequirements && hasStack)
        {
            topic = "AIMS MVP 시스템 요구사항 분석";
            keywords.AddRange(new[] { "MVP", "기술스택", "기능요구사항", "비기능요구사항", "GPUStack", ".NET", "인프라" });
            summary = "AIMS MVP 프로젝트의 기술 스택 구성과 기능/비기능 요구사항을 정의한 기술 문서입니다.";
        }
        else if (hasStack)
        {
            topic = "기술 스택 구성";
            keywords.AddRange(new[] { ".NET", "GPUStack", "AI모델", "벡터데이터베이스", "컨테이너" });
            summary = "프로젝트의 백엔드, 프론트엔드, AI/ML, 인프라 기술 스택을 정의합니다.";
        }
        else
        {
            topic = "기술 문서";
            keywords.AddRange(new[] { "문서", "분석", "시스템" });
            summary = "기술적인 내용을 다루는 구조화된 문서입니다.";
        }

        var response = $"""
        TOPIC: {topic}
        KEYWORDS: {string.Join(", ", keywords)}
        SUMMARY: {summary}
        SECTIONS: 계층화된 섹션 구조로 구성됨
        """;

        return Task.FromResult(response);
    }
}