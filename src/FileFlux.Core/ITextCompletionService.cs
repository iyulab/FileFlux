using FileFlux.Domain;

namespace FileFlux.Core;

/// <summary>
/// 텍스트 완성 서비스 추상화 인터페이스
/// FileFlux에서 필수 의존성으로 요구됨
/// 
/// 역할:
/// - 문서 구조 분석, 요약, 메타데이터 추출을 위한 LLM 호출
/// - 소비 애플리케이션에서 구현하여 DI를 통해 주입
/// - 모든 LLM 기반 기능은 이 서비스에 의존
/// </summary>
public interface ITextCompletionService
{
    /// <summary>
    /// 문서 구조 분석을 위한 LLM 호출
    /// 소비 애플리케이션에서 구현: 실제 LLM API 호출 로직
    /// </summary>
    /// <param name="prompt">구조 분석용 프롬프트 (FileFlux에서 생성)</param>
    /// <param name="documentType">문서 타입</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>구조 분석 결과</returns>
    Task<StructureAnalysisResult> AnalyzeStructureAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 내용 요약을 위한 LLM 호출
    /// 소비 애플리케이션에서 구현: 실제 LLM API 호출 로직
    /// </summary>
    /// <param name="prompt">내용 요약용 프롬프트 (FileFlux에서 생성)</param>
    /// <param name="maxLength">최대 요약 길이</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>요약 결과</returns>
    Task<ContentSummary> SummarizeContentAsync(
        string prompt,
        int maxLength = 200,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 메타데이터 추출을 위한 LLM 호출
    /// 소비 애플리케이션에서 구현: 실제 LLM API 호출 로직
    /// </summary>
    /// <param name="prompt">메타데이터 추출용 프롬프트 (FileFlux에서 생성)</param>
    /// <param name="documentType">문서 타입</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>메타데이터 추출 결과</returns>
    Task<MetadataExtractionResult> ExtractMetadataAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 품질 평가를 위한 LLM 호출
    /// 소비 애플리케이션에서 구현: 실제 LLM API 호출 로직
    /// </summary>
    /// <param name="prompt">품질 평가용 프롬프트 (FileFlux에서 생성)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>품질 평가 결과</returns>
    Task<QualityAssessment> AssessQualityAsync(
        string prompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 텍스트 완성 서비스 제공업체 정보 (소비 애플리케이션에서 구현)
    /// </summary>
    TextCompletionServiceInfo ProviderInfo { get; }

    /// <summary>
    /// 텍스트 완성 서비스 가용성 확인 (소비 애플리케이션에서 구현)
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>서비스 가용성</returns>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 범용 텍스트 완성 호출 (BasicDocumentParser 호환성을 위한 간단한 인터페이스)
    /// </summary>
    /// <param name="prompt">프롬프트</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>LLM 응답 텍스트</returns>
    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default);
}

/// <summary>
/// 구조 분석 결과
/// </summary>
public class StructureAnalysisResult
{
    /// <summary>
    /// 감지된 문서 타입
    /// </summary>
    public DocumentType DocumentType { get; set; }

    /// <summary>
    /// 섹션 정보 목록
    /// </summary>
    public List<SectionInfo> Sections { get; set; } = new();

    /// <summary>
    /// 문서 구조 트리
    /// </summary>
    public DocumentStructure Structure { get; set; } = new();

    /// <summary>
    /// 분석 신뢰도 (0.0 - 1.0)
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// 텍스트 완성 서비스 응답 원본
    /// </summary>
    public string RawResponse { get; set; } = string.Empty;

    /// <summary>
    /// 사용된 토큰 수
    /// </summary>
    public int TokensUsed { get; set; }
}

/// <summary>
/// 섹션 정보
/// </summary>
public class SectionInfo
{
    /// <summary>
    /// 섹션 타입
    /// </summary>
    public SectionType Type { get; set; }

    /// <summary>
    /// 섹션 제목
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 시작 위치
    /// </summary>
    public int StartPosition { get; set; }

    /// <summary>
    /// 종료 위치
    /// </summary>
    public int EndPosition { get; set; }

    /// <summary>
    /// 계층 레벨
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// 상위 섹션 ID
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// 섹션 중요도 (0.0 - 1.0)
    /// </summary>
    public double Importance { get; set; }
}

/// <summary>
/// 문서 구조 트리
/// </summary>
public class DocumentStructure
{
    /// <summary>
    /// 루트 섹션
    /// </summary>
    public SectionInfo Root { get; set; } = new();

    /// <summary>
    /// 모든 섹션의 플랫 목록
    /// </summary>
    public List<SectionInfo> AllSections { get; set; } = new();

    /// <summary>
    /// 섹션 간 관계 매핑
    /// </summary>
    public Dictionary<string, List<string>> SectionRelations { get; set; } = new();

    /// <summary>
    /// 총 섹션 개수
    /// </summary>
    public int TotalSections => AllSections.Count;
}

/// <summary>
/// 내용 요약 결과
/// </summary>
public class ContentSummary
{
    /// <summary>
    /// 요약 텍스트
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// 핵심 키워드
    /// </summary>
    public string[] Keywords { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 요약 신뢰도 (0.0 - 1.0)
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// 원본 텍스트 길이
    /// </summary>
    public int OriginalLength { get; set; }

    /// <summary>
    /// 요약 텍스트 길이
    /// </summary>
    public int SummaryLength => Summary.Length;

    /// <summary>
    /// 압축 비율
    /// </summary>
    public double CompressionRatio => OriginalLength > 0 ? (double)SummaryLength / OriginalLength : 0;

    /// <summary>
    /// 사용된 토큰 수
    /// </summary>
    public int TokensUsed { get; set; }
}

/// <summary>
/// 메타데이터 추출 결과
/// </summary>
public class MetadataExtractionResult
{
    /// <summary>
    /// 추출된 키워드
    /// </summary>
    public string[] Keywords { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 감지된 언어
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// 컨텐츠 카테고리
    /// </summary>
    public string[] Categories { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 감지된 엔티티 (인물, 장소, 조직 등)
    /// </summary>
    public Dictionary<string, string[]> Entities { get; set; } = new();

    /// <summary>
    /// 기술적 메타데이터 (코드 언어, 프레임워크 등)
    /// </summary>
    public Dictionary<string, string> TechnicalMetadata { get; set; } = new();

    /// <summary>
    /// 추출 신뢰도 (0.0 - 1.0)
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// 사용된 토큰 수
    /// </summary>
    public int TokensUsed { get; set; }
}

/// <summary>
/// 품질 평가 결과
/// </summary>
public class QualityAssessment
{
    /// <summary>
    /// 신뢰성 점수 (0.0 - 1.0)
    /// </summary>
    public double ConfidenceScore { get; set; }

    /// <summary>
    /// 완성도 점수 (0.0 - 1.0)
    /// </summary>
    public double CompletenessScore { get; set; }

    /// <summary>
    /// 일관성 점수 (0.0 - 1.0)
    /// </summary>
    public double ConsistencyScore { get; set; }

    /// <summary>
    /// 전체 품질 점수
    /// </summary>
    public double OverallScore => (ConfidenceScore + CompletenessScore + ConsistencyScore) / 3.0;

    /// <summary>
    /// 품질 개선 제안
    /// </summary>
    public List<QualityRecommendation> Recommendations { get; set; } = new();

    /// <summary>
    /// 평가 설명
    /// </summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>
    /// 사용된 토큰 수
    /// </summary>
    public int TokensUsed { get; set; }
}

/// <summary>
/// 품질 개선 제안
/// </summary>
public class QualityRecommendation
{
    /// <summary>
    /// 제안 타입
    /// </summary>
    public RecommendationType Type { get; set; }

    /// <summary>
    /// 제안 설명
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 제안된 값 (해당하는 경우)
    /// </summary>
    public string? SuggestedValue { get; set; }

    /// <summary>
    /// 우선순위 (1-10)
    /// </summary>
    public int Priority { get; set; } = 5;
}

/// <summary>
/// 제안 타입
/// </summary>
public enum RecommendationType
{
    /// <summary>
    /// 청크 크기 최적화
    /// </summary>
    CHUNK_SIZE_OPTIMIZATION,

    /// <summary>
    /// 메타데이터 보완
    /// </summary>
    METADATA_ENHANCEMENT,

    /// <summary>
    /// 제목 개선
    /// </summary>
    TITLE_IMPROVEMENT,

    /// <summary>
    /// 설명 보완
    /// </summary>
    DESCRIPTION_ENHANCEMENT,

    /// <summary>
    /// 컨텍스트 정보 추가
    /// </summary>
    CONTEXT_ADDITION,

    /// <summary>
    /// 구조화 개선
    /// </summary>
    STRUCTURE_IMPROVEMENT
}

/// <summary>
/// 텍스트 완성 서비스 제공업체 정보
/// </summary>
public class TextCompletionServiceInfo
{
    /// <summary>
    /// 제공업체명
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 제공업체 타입
    /// </summary>
    public TextCompletionProviderType Type { get; set; }

    /// <summary>
    /// 지원하는 모델 목록
    /// </summary>
    public string[] SupportedModels { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 최대 컨텍스트 길이
    /// </summary>
    public int MaxContextLength { get; set; }

    /// <summary>
    /// 토큰당 비용 (입력)
    /// </summary>
    public decimal InputTokenCost { get; set; }

    /// <summary>
    /// 토큰당 비용 (출력)
    /// </summary>
    public decimal OutputTokenCost { get; set; }

    /// <summary>
    /// API 버전
    /// </summary>
    public string ApiVersion { get; set; } = string.Empty;
}