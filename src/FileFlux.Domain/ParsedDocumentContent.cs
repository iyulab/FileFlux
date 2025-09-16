namespace FileFlux.Domain;

/// <summary>
/// Parser가 구조화한 최종 문서 내용
/// LLM 기반 분석과 메타데이터 추출이 완료된 상태
/// </summary>
public class ParsedDocumentContent
{
    /// <summary>
    /// 구조화된 텍스트 내용
    /// </summary>
    public string StructuredText { get; set; } = string.Empty;

    /// <summary>
    /// 원본 원시 텍스트 (참조용)
    /// </summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>
    /// 풍부한 문서 메타데이터
    /// </summary>
    public DocumentMetadata Metadata { get; set; } = new();

    /// <summary>
    /// 문서 구조 정보 (LLM이 분석한 의미적 구조)
    /// </summary>
    public DocumentStructure Structure { get; set; } = new();

    /// <summary>
    /// 구조화 품질 지표
    /// </summary>
    public QualityMetrics Quality { get; set; } = new();

    /// <summary>
    /// 파싱 과정 정보
    /// </summary>
    public ParsingMetadata ParsingInfo { get; set; } = new();
}

/// <summary>
/// LLM이 분석한 문서 구조
/// </summary>
public class DocumentStructure
{
    /// <summary>
    /// 문서 유형 (Technical, Business, Legal, Academic 등)
    /// </summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>
    /// 문서 주제/도메인
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// 문서 요약
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// 주요 키워드
    /// </summary>
    public List<string> Keywords { get; set; } = new();

    /// <summary>
    /// 구조화된 섹션들
    /// </summary>
    public List<DocumentSection> Sections { get; set; } = new();

    /// <summary>
    /// 문서 내 엔티티 (인물, 장소, 조직 등)
    /// </summary>
    public List<DocumentEntity> Entities { get; set; } = new();
}

/// <summary>
/// 문서 섹션
/// </summary>
public class DocumentSection
{
    /// <summary>
    /// 섹션 ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 섹션 제목
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 섹션 유형 (Header, Paragraph, List, Table, Code 등)
    /// </summary>
    public string SectionType { get; set; } = string.Empty;

    /// <summary>
    /// 섹션 내용
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 계층 레벨 (1=최상위, 2=하위 등)
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// 원본 문서에서의 위치
    /// </summary>
    public int StartPosition { get; set; }

    /// <summary>
    /// 원본 문서에서의 끝 위치
    /// </summary>
    public int EndPosition { get; set; }

    /// <summary>
    /// 하위 섹션들
    /// </summary>
    public List<DocumentSection> SubSections { get; } = new();
}

/// <summary>
/// 문서 엔티티
/// </summary>
public class DocumentEntity
{
    /// <summary>
    /// 엔티티 텍스트
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 엔티티 유형 (Person, Organization, Location, Date 등)
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// 신뢰도 점수 (0.0 ~ 1.0)
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// 구조화 품질 지표
/// </summary>
public class QualityMetrics
{
    /// <summary>
    /// 구조화 점수 (0.0 ~ 1.0)
    /// </summary>
    public double StructureScore { get; set; }
    
    /// <summary>
    /// 일관성 점수 (0.0 ~ 1.0)
    /// </summary>
    public double ConsistencyScore { get; set; }
    
    /// <summary>
    /// 정보 보존 점수 (0.0 ~ 1.0)
    /// </summary>
    public double InformationRetentionScore { get; set; }
    
    /// <summary>
    /// 신뢰도 점수 (0.0 ~ 1.0)
    /// </summary>
    public double ConfidenceScore { get; set; }
    
    /// <summary>
    /// 완전성 점수 (0.0 ~ 1.0)
    /// </summary>
    public double CompletenessScore { get; set; }
    
    /// <summary>
    /// 구조 신뢰도 (0.0 ~ 1.0)
    /// </summary>
    public double StructureConfidence { get; set; }
    
    /// <summary>
    /// 상세한 메트릭 정보
    /// </summary>
    public Dictionary<string, object> DetailedMetrics { get; set; } = new();
    
    /// <summary>
    /// 전체 품질 점수 (위 점수들의 가중 평균)
    /// </summary>
    public double OverallScore => (StructureScore + ConsistencyScore + InformationRetentionScore) / 3.0;
}

/// <summary>
/// 파싱 과정 메타데이터
/// </summary>
public class ParsingMetadata
{
    /// <summary>
    /// 사용된 Parser 타입
    /// </summary>
    public string ParserType { get; set; } = string.Empty;

    /// <summary>
    /// LLM 사용 여부
    /// </summary>
    public bool UsedLlm { get; set; }

    /// <summary>
    /// 파싱 시작 시간
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// 파싱 완료 시간
    /// </summary>
    public DateTime CompletedAt { get; set; }

    /// <summary>
    /// 파싱 소요 시간
    /// </summary>
    public TimeSpan Duration => CompletedAt - StartedAt;

    /// <summary>
    /// 파싱 과정에서 발생한 경고나 이슈
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// LLM 호출 통계 (횟수, 토큰 사용량 등)
    /// </summary>
    public Dictionary<string, object> LlmStats { get; } = new();
}