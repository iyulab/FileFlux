namespace FileFlux.Domain;

/// <summary>
/// 문서 처리 진행 상태를 나타내는 열거형
/// </summary>
public enum ProcessingStage
{
    /// <summary>문서 읽기 시작</summary>
    Reading,

    /// <summary>텍스트 추출 중</summary>
    Extracting,

    /// <summary>구문 분석 중</summary>
    Parsing,

    /// <summary>청킹 중</summary>
    Chunking,

    /// <summary>검증 중</summary>
    Validating,

    /// <summary>완료</summary>
    Completed,

    /// <summary>오류 발생</summary>
    Error
}

/// <summary>
/// 문서 처리 진행률 정보
/// </summary>
public class ProcessingProgress
{
    /// <summary>
    /// 파일 경로
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 현재 처리 단계
    /// </summary>
    public ProcessingStage Stage { get; set; }

    /// <summary>
    /// 전체 진행률 (0.0 - 1.0)
    /// </summary>
    public double OverallProgress { get; set; }

    /// <summary>
    /// 현재 단계의 진행률 (0.0 - 1.0)
    /// </summary>
    public double StageProgress { get; set; }

    /// <summary>
    /// 진행률 메시지
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 처리된 바이트 수
    /// </summary>
    public long ProcessedBytes { get; set; }

    /// <summary>
    /// 전체 바이트 수
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// 처리된 청크 수 (청킹 단계에서 사용)
    /// </summary>
    public int ProcessedChunks { get; set; }

    /// <summary>
    /// 예상 청크 수 (청킹 단계에서 사용)
    /// </summary>
    public int EstimatedChunks { get; set; }

    /// <summary>
    /// 처리 시작 시간
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 현재 시간
    /// </summary>
    public DateTime CurrentTime { get; set; }

    /// <summary>
    /// 예상 완료 시간 (nullable)
    /// </summary>
    public DateTime? EstimatedCompletion { get; set; }

    /// <summary>
    /// 오류 정보 (오류 발생 시)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 경과 시간
    /// </summary>
    public TimeSpan ElapsedTime => CurrentTime - StartTime;

    /// <summary>
    /// 진행률을 백분율 문자열로 반환
    /// </summary>
    public string ProgressPercentage => $"{OverallProgress:P1}";

    /// <summary>
    /// 진행률 정보를 생성하는 정적 메서드들
    /// </summary>
    public static class Factory
    {
        /// <summary>
        /// 새 진행률 정보를 생성합니다
        /// </summary>
        public static ProcessingProgress Create(string filePath, ProcessingStage stage, double progress, string message = "")
        {
            return new ProcessingProgress
            {
                FilePath = filePath,
                Stage = stage,
                OverallProgress = progress,
                StageProgress = progress,
                Message = message,
                StartTime = DateTime.UtcNow,
                CurrentTime = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 오류 진행률 정보를 생성합니다
        /// </summary>
        public static ProcessingProgress CreateError(string filePath, string errorMessage)
        {
            return new ProcessingProgress
            {
                FilePath = filePath,
                Stage = ProcessingStage.Error,
                OverallProgress = 0.0,
                Message = "처리 중 오류 발생",
                ErrorMessage = errorMessage,
                StartTime = DateTime.UtcNow,
                CurrentTime = DateTime.UtcNow
            };
        }
    }
}

/// <summary>
/// 문서 처리 결과와 진행률을 포함하는 래퍼 클래스
/// </summary>
/// <typeparam name="T">결과 타입</typeparam>
public class ProcessingResult<T>
{
    /// <summary>
    /// 처리 결과
    /// </summary>
    public T? Result { get; set; }

    /// <summary>
    /// 진행률 정보
    /// </summary>
    public ProcessingProgress Progress { get; set; } = new();

    /// <summary>
    /// 추출된 원시 문서 내용 (Extracting 단계에서 설정)
    /// </summary>
    public RawContent? RawContent { get; set; }

    /// <summary>
    /// 파싱된 문서 내용 (Parsing 단계에서 설정)
    /// </summary>
    public ParsedContent? ParsedContent { get; set; }

    /// <summary>
    /// 처리 성공 여부
    /// </summary>
    public bool IsSuccess => Result != null && Progress.Stage != ProcessingStage.Error;

    /// <summary>
    /// 오류 여부
    /// </summary>
    public bool IsError => Progress.Stage == ProcessingStage.Error;

    /// <summary>
    /// 처리 중 여부
    /// </summary>
    public bool IsProcessing => Progress.Stage != ProcessingStage.Completed && Progress.Stage != ProcessingStage.Error;

    /// <summary>
    /// 오류 메시지 (오류 발생 시)
    /// </summary>
    public string? ErrorMessage => Progress.ErrorMessage;

    /// <summary>
    /// 성공적인 처리 결과를 생성합니다
    /// </summary>
    public static ProcessingResult<T> Success(T result, ProcessingProgress progress)
    {
        return new ProcessingResult<T>
        {
            Result = result,
            Progress = progress
        };
    }

    /// <summary>
    /// 진행 중 상태를 생성합니다
    /// </summary>
    public static ProcessingResult<T> InProgress(ProcessingProgress progress)
    {
        return new ProcessingResult<T>
        {
            Progress = progress
        };
    }

    /// <summary>
    /// 오류 상태를 생성합니다
    /// </summary>
    public static ProcessingResult<T> Error(string errorMessage, ProcessingProgress? progress = null)
    {
        progress ??= new ProcessingProgress();
        progress.Stage = ProcessingStage.Error;
        progress.ErrorMessage = errorMessage;
        progress.Message = "처리 중 오류 발생";

        return new ProcessingResult<T>
        {
            Progress = progress
        };
    }
}
