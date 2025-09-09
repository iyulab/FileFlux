namespace FileFlux;

/// <summary>
/// 처리된 문서 응답 - 소비 어플리케이션용 출력 모델
/// </summary>
public class ProcessedDocument
{
    /// <summary>
    /// 원본 문서 정보
    /// </summary>
    public DocumentInfo Document { get; set; } = null!;

    /// <summary>
    /// 생성된 청크 목록
    /// </summary>
    public List<ProcessedChunk> Chunks { get; set; } = new();

    /// <summary>
    /// 처리 통계
    /// </summary>
    public ProcessingStatistics Statistics { get; set; } = null!;

    /// <summary>
    /// 품질 메트릭
    /// </summary>
    public QualityMetrics Quality { get; set; } = null!;

    /// <summary>
    /// 처리 완료 시간
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 사용된 처리 옵션
    /// </summary>
    public DocumentProcessingOptions UsedOptions { get; set; } = null!;
}

/// <summary>
/// 문서 기본 정보
/// </summary>
public class DocumentInfo
{
    /// <summary>
    /// 파일명
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 파일 크기 (바이트)
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// 파일 타입 (MIME type)
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// 지원 파일 형식 여부
    /// </summary>
    public bool IsSupported { get; set; } = true;

    /// <summary>
    /// 원본 텍스트 길이
    /// </summary>
    public int OriginalTextLength { get; set; }

    /// <summary>
    /// 추출된 메타데이터
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 처리된 청크 - Context7 스타일 메타데이터 포함
/// </summary>
public class ProcessedChunk
{
    /// <summary>
    /// 청크 고유 ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 청크 텍스트 내용
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 청크 인덱스 (0부터 시작)
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// 원본 문서에서의 시작 위치
    /// </summary>
    public int StartPosition { get; set; }

    /// <summary>
    /// 원본 문서에서의 끝 위치
    /// </summary>
    public int EndPosition { get; set; }

    /// <summary>
    /// 콘텐츠 타입 (text, table, code, list, heading)
    /// </summary>
    public string ContentType { get; set; } = "text";

    /// <summary>
    /// 구조적 역할 (content, heading, code_block, table_row)
    /// </summary>
    public string StructuralRole { get; set; } = "content";

    /// <summary>
    /// 품질 점수 (0.0 ~ 1.0)
    /// </summary>
    public double QualityScore { get; set; }

    /// <summary>
    /// 완성도 점수 (0.0 ~ 1.0, Smart 전략에서 70% 이상 보장)
    /// </summary>
    public double CompletenessScore { get; set; }

    /// <summary>
    /// 문서 내 관련성 점수 (0.0 ~ 1.0)
    /// </summary>
    public double RelevanceScore { get; set; }

    /// <summary>
    /// 주제별 점수 (주제명 -> 점수)
    /// </summary>
    public Dictionary<string, double> TopicScores { get; set; } = new();

    /// <summary>
    /// 기술 키워드 목록
    /// </summary>
    public List<string> TechnicalKeywords { get; set; } = new();

    /// <summary>
    /// 오버랩 여부
    /// </summary>
    public bool HasOverlap { get; set; }

    /// <summary>
    /// 추가 속성
    /// </summary>
    public Dictionary<string, object> Properties { get; set; } = new();
}

/// <summary>
/// 처리 통계
/// </summary>
public class ProcessingStatistics
{
    /// <summary>
    /// 생성된 총 청크 수
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// 평균 청크 크기 (문자 수)
    /// </summary>
    public double AverageChunkSize { get; set; }

    /// <summary>
    /// 최소 청크 크기
    /// </summary>
    public int MinChunkSize { get; set; }

    /// <summary>
    /// 최대 청크 크기
    /// </summary>
    public int MaxChunkSize { get; set; }

    /// <summary>
    /// 처리 시간 (밀리초)
    /// </summary>
    public long ProcessingTimeMs { get; set; }

    /// <summary>
    /// 메모리 사용량 (바이트)
    /// </summary>
    public long MemoryUsageBytes { get; set; }

    /// <summary>
    /// 사용된 전략
    /// </summary>
    public string UsedStrategy { get; set; } = string.Empty;

    /// <summary>
    /// LLM API 호출 횟수
    /// </summary>
    public int LlmApiCalls { get; set; }

    /// <summary>
    /// 처리 오류/경고 수
    /// </summary>
    public int WarningCount { get; set; }
}

/// <summary>
/// 품질 메트릭
/// </summary>
public class QualityMetrics
{
    /// <summary>
    /// 전체 평균 품질 점수 (0.0 ~ 1.0)
    /// </summary>
    public double AverageQualityScore { get; set; }

    /// <summary>
    /// 전체 평균 완성도 점수 (0.0 ~ 1.0)
    /// </summary>
    public double AverageCompletenessScore { get; set; }

    /// <summary>
    /// 70% 이상 완성도 청크 비율
    /// </summary>
    public double HighQualityChunkRatio { get; set; }

    /// <summary>
    /// 문장 경계 보존율 (0.0 ~ 1.0)
    /// </summary>
    public double SentenceBoundaryPreservation { get; set; }

    /// <summary>
    /// 정보 밀도 (단위 길이당 정보량)
    /// </summary>
    public double InformationDensity { get; set; }

    /// <summary>
    /// 의미적 일관성 점수 (0.0 ~ 1.0)
    /// </summary>
    public double SemanticCoherence { get; set; }

    /// <summary>
    /// RAG 적합성 점수 (0.0 ~ 1.0)
    /// </summary>
    public double RagSuitability { get; set; }

    /// <summary>
    /// 품질 등급 (A, B, C, D, F)
    /// </summary>
    public string QualityGrade { get; set; } = "C";
}