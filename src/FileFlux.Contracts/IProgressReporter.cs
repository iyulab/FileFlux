namespace FileFlux;

/// <summary>
/// 처리 진행률 보고 인터페이스
/// </summary>
public interface IProgressReporter
{
    /// <summary>
    /// 진행률 업데이트
    /// </summary>
    /// <param name="progress">진행률 정보</param>
    void ReportProgress(ProcessingProgress progress);
}

/// <summary>
/// 처리 진행률 정보
/// </summary>
public class ProcessingProgress
{
    /// <summary>
    /// 현재 단계
    /// </summary>
    public ProcessingStage Stage { get; set; }

    /// <summary>
    /// 전체 진행률 (0.0 ~ 1.0)
    /// </summary>
    public double OverallProgress { get; set; }

    /// <summary>
    /// 현재 단계 진행률 (0.0 ~ 1.0)
    /// </summary>
    public double StageProgress { get; set; }

    /// <summary>
    /// 현재 처리 중인 항목
    /// </summary>
    public string? CurrentItem { get; set; }

    /// <summary>
    /// 처리된 항목 수
    /// </summary>
    public int ProcessedItems { get; set; }

    /// <summary>
    /// 전체 항목 수
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// 예상 남은 시간 (초)
    /// </summary>
    public int? EstimatedRemainingSeconds { get; set; }

    /// <summary>
    /// 현재 단계 메시지
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 처리 시작 시간
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 추가 정보
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 처리 단계
/// </summary>
public enum ProcessingStage
{
    /// <summary>
    /// 초기화 중
    /// </summary>
    Initializing,

    /// <summary>
    /// 파일 읽기 중
    /// </summary>
    ReadingFile,

    /// <summary>
    /// 텍스트 추출 중
    /// </summary>
    ExtractingText,

    /// <summary>
    /// 문서 파싱 중
    /// </summary>
    ParsingDocument,

    /// <summary>
    /// LLM 분석 중
    /// </summary>
    LlmAnalysis,

    /// <summary>
    /// 청킹 전략 적용 중
    /// </summary>
    ApplyingChunkingStrategy,

    /// <summary>
    /// 청크 생성 중
    /// </summary>
    CreatingChunks,

    /// <summary>
    /// 품질 분석 중
    /// </summary>
    AnalyzingQuality,

    /// <summary>
    /// 메타데이터 추출 중
    /// </summary>
    ExtractingMetadata,

    /// <summary>
    /// 후처리 중
    /// </summary>
    PostProcessing,

    /// <summary>
    /// 완료
    /// </summary>
    Completed,

    /// <summary>
    /// 오류 발생
    /// </summary>
    Error
}