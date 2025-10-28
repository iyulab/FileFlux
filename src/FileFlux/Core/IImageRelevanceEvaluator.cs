namespace FileFlux;

/// <summary>
/// 이미지 텍스트의 문서 관련성을 평가하는 서비스 인터페이스
/// 문서에서 추출된 이미지의 텍스트가 실제 문서 내용과 관련이 있는지 평가
/// </summary>
public interface IImageRelevanceEvaluator
{
    /// <summary>
    /// 이미지에서 추출한 텍스트가 문서와 관련이 있는지 평가합니다.
    /// </summary>
    /// <param name="imageText">이미지에서 추출된 텍스트</param>
    /// <param name="documentContext">문서 컨텍스트 정보</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>이미지 관련성 평가 결과</returns>
    Task<ImageRelevanceResult> EvaluateAsync(
        string imageText,
        DocumentContext documentContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 이미지 텍스트를 일괄 평가합니다.
    /// </summary>
    /// <param name="imageTexts">이미지에서 추출된 텍스트 목록</param>
    /// <param name="documentContext">문서 컨텍스트 정보</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>이미지 관련성 평가 결과 목록</returns>
    Task<IEnumerable<ImageRelevanceResult>> EvaluateBatchAsync(
        IEnumerable<string> imageTexts,
        DocumentContext documentContext,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 문서 컨텍스트 정보
/// </summary>
public class DocumentContext
{
    /// <summary>
    /// 문서 제목
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// 문서 전체 텍스트 또는 요약
    /// </summary>
    public string DocumentText { get; set; } = string.Empty;

    /// <summary>
    /// 문서 타입 (PDF, DOCX, PPTX 등)
    /// </summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>
    /// 이미지가 위치한 페이지 번호 (해당되는 경우)
    /// </summary>
    public int? PageNumber { get; set; }

    /// <summary>
    /// 이미지 주변 텍스트 (앞뒤 일정 범위의 텍스트)
    /// </summary>
    public string? SurroundingText { get; set; }

    /// <summary>
    /// 문서의 주요 키워드 목록
    /// </summary>
    public List<string> Keywords { get; set; } = new();

    /// <summary>
    /// 문서 메타데이터
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// 이미지 관련성 평가 결과
/// </summary>
public class ImageRelevanceResult
{
    /// <summary>
    /// 관련성이 있는지 여부
    /// </summary>
    public bool IsRelevant { get; set; }

    /// <summary>
    /// 관련성 점수 (0.0 ~ 1.0)
    /// </summary>
    public double RelevanceScore { get; set; }

    /// <summary>
    /// 관련성 카테고리
    /// </summary>
    public RelevanceCategory Category { get; set; }

    /// <summary>
    /// 평가 이유 설명
    /// </summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>
    /// 처리된 텍스트 (정제되거나 요약된 버전)
    /// </summary>
    public string? ProcessedText { get; set; }

    /// <summary>
    /// 포함 권장 사항
    /// </summary>
    public InclusionRecommendation Recommendation { get; set; }

    /// <summary>
    /// 추출된 핵심 정보
    /// </summary>
    public List<string> ExtractedKeyPoints { get; set; } = new();

    /// <summary>
    /// 평가 메타데이터
    /// </summary>
    public Dictionary<string, object> EvaluationMetadata { get; set; } = new();
}

/// <summary>
/// 관련성 카테고리
/// </summary>
public enum RelevanceCategory
{
    /// <summary>
    /// 핵심 콘텐츠 (차트, 다이어그램, 핵심 정보)
    /// </summary>
    CoreContent,

    /// <summary>
    /// 보조 정보 (캡션, 라벨, 부가 설명)
    /// </summary>
    SupplementaryInfo,

    /// <summary>
    /// 장식적 요소 (로고, 아이콘, 배경)
    /// </summary>
    Decorative,

    /// <summary>
    /// 관련 없음 (광고, 워터마크, 페이지 번호)
    /// </summary>
    Irrelevant,

    /// <summary>
    /// 판단 불가
    /// </summary>
    Uncertain
}

/// <summary>
/// 포함 권장 사항
/// </summary>
public enum InclusionRecommendation
{
    /// <summary>
    /// 반드시 포함 (핵심 정보)
    /// </summary>
    MustInclude,

    /// <summary>
    /// 포함 권장 (유용한 정보)
    /// </summary>
    ShouldInclude,

    /// <summary>
    /// 선택적 포함 (부가 정보)
    /// </summary>
    OptionalInclude,

    /// <summary>
    /// 제외 권장 (관련성 낮음)
    /// </summary>
    ShouldExclude,

    /// <summary>
    /// 반드시 제외 (무관한 정보)
    /// </summary>
    MustExclude
}
