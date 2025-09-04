namespace FileFlux.Domain;

/// <summary>
/// RAG 시스템에 최적화된 문서 청크
/// </summary>
public class DocumentChunk
{
    /// <summary>
    /// 청크 고유 식별자
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 청크 내용
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 원본 문서 메타데이터
    /// </summary>
    public DocumentMetadata Metadata { get; set; } = new();

    /// <summary>
    /// 원본 문서에서의 시작 위치
    /// </summary>
    public int StartPosition { get; set; }

    /// <summary>
    /// 원본 문서에서의 종료 위치
    /// </summary>
    public int EndPosition { get; set; }

    /// <summary>
    /// 청크 순서 번호
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// 청크가 속한 페이지 번호 (해당되는 경우)
    /// </summary>
    public int? PageNumber { get; set; }

    /// <summary>
    /// 청크가 속한 섹션/챕터 정보
    /// </summary>
    public string? Section { get; set; }

    /// <summary>
    /// 청크의 의미적 중요도 (0.0 ~ 1.0)
    /// </summary>
    public double Importance { get; set; } = 0.5;

    /// <summary>
    /// 청크 생성에 사용된 전략
    /// </summary>
    public string Strategy { get; set; } = string.Empty;

    /// <summary>
    /// 청크별 사용자 정의 속성
    /// </summary>
    public Dictionary<string, object> Properties { get; set; } = new();

    /// <summary>
    /// 청크 생성 일시
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 텍스트 토큰 수 (추정치)
    /// </summary>
    public int EstimatedTokens { get; set; }

    // 고급 메타데이터 - RAG 품질 향상을 위한 확장

    /// <summary>
    /// 콘텐츠 타입 분류
    /// </summary>
    public string ContentType { get; set; } = "text"; // "text", "table", "code", "list", "heading"

    /// <summary>
    /// 청크 품질 점수 (0.0-1.0)
    /// </summary>
    public double QualityScore { get; set; } = 0.5;

    /// <summary>
    /// 문서 맥락 관련성 점수 (0.0-1.0)
    /// </summary>
    public double RelevanceScore { get; set; } = 0.5;

    /// <summary>
    /// 구조적 역할 분류
    /// </summary>
    public string StructuralRole { get; set; } = "content"; // "title", "content", "code_block", "table_cell", "list_item"

    /// <summary>
    /// 주제 분류
    /// </summary>
    public string? TopicCategory { get; set; }

    /// <summary>
    /// 구조적 경계 마커 - 청크의 구조적 요소 표시
    /// </summary>
    public string? BoundaryMarkers { get; set; }

    /// <summary>
    /// 다양한 맥락 점수들 - 확장 가능한 품질 지표
    /// </summary>
    public Dictionary<string, double> ContextualScores { get; set; } = new();

    /// <summary>
    /// 정보 밀도 (단위 길이당 정보량)
    /// </summary>
    public double InformationDensity { get; set; } = 0.5;

    // LLM 최적화 메타데이터

    /// <summary>
    /// LLM용 구조적 컨텍스트 헤더 - 검색 정확도 향상
    /// </summary>
    public string? ContextualHeader { get; set; }

    /// <summary>
    /// 문서 도메인 (Technical, Business, Academic 등)
    /// </summary>
    public string DocumentDomain { get; set; } = "General";

    /// <summary>
    /// 감지된 기술 키워드 (기술 문서용)
    /// </summary>
    public List<string> TechnicalKeywords { get; set; } = new();
}