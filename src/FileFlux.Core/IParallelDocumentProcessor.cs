using FileFlux.Domain;

namespace FileFlux;

/// <summary>
/// 병렬 문서 처리기 인터페이스 - 대용량 문서 및 다중 문서 동시 처리
/// </summary>
public interface IParallelDocumentProcessor
{
    /// <summary>
    /// 여러 문서를 병렬로 처리하여 청크 스트림 반환
    /// </summary>
    /// <param name="filePaths">처리할 문서 파일 경로 목록</param>
    /// <param name="options">청킹 옵션</param>
    /// <param name="parallelOptions">병렬 처리 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>문서별 청크 결과 스트림</returns>
    IAsyncEnumerable<ParallelProcessingResult> ProcessManyAsync(
        IEnumerable<string> filePaths,
        ChunkingOptions? options = null,
        ParallelProcessingOptions? parallelOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 단일 대용량 문서를 청크 단위로 병렬 처리
    /// </summary>
    /// <param name="filePath">처리할 문서 파일 경로</param>
    /// <param name="options">청킹 옵션</param>
    /// <param name="parallelOptions">병렬 처리 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>청크 스트림</returns>
    IAsyncEnumerable<DocumentChunk> ProcessLargeDocumentAsync(
        string filePath,
        ChunkingOptions? options = null,
        ParallelProcessingOptions? parallelOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 병렬 처리 성능 통계 조회
    /// </summary>
    /// <returns>성능 통계 정보</returns>
    ParallelProcessingStats GetProcessingStats();

    /// <summary>
    /// 병렬 처리기 리소스 정리
    /// </summary>
    Task DisposeAsync();
}

/// <summary>
/// 병렬 처리 결과
/// </summary>
public class ParallelProcessingResult
{
    /// <summary>
    /// 처리된 파일 경로
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 처리 성공 여부
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 생성된 청크 목록
    /// </summary>
    public List<DocumentChunk> Chunks { get; set; } = new();

    /// <summary>
    /// 처리 시간 (밀리초)
    /// </summary>
    public long ProcessingTimeMs { get; set; }

    /// <summary>
    /// 사용된 워커 스레드 수
    /// </summary>
    public int WorkerThreadCount { get; set; }

    /// <summary>
    /// 에러 메시지 (실패 시)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 추가 메타데이터
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 병렬 처리 옵션
/// </summary>
public class ParallelProcessingOptions
{
    /// <summary>
    /// 최대 병렬 처리 수 (기본값: CPU 코어 수)
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// 메모리 사용량 제한 (바이트, 기본값: 1GB)
    /// </summary>
    public long MaxMemoryUsageBytes { get; set; } = 1024 * 1024 * 1024;

    /// <summary>
    /// 청크 배치 크기 (스트리밍 효율성)
    /// </summary>
    public int ChunkBatchSize { get; set; } = 100;

    /// <summary>
    /// 백프레셔 임계값 (대기 중인 아이템 수)
    /// </summary>
    public int BackpressureThreshold { get; set; } = 1000;

    /// <summary>
    /// 대용량 파일 임계값 (바이트, 이 값을 초과하면 특별 처리)
    /// </summary>
    public long LargeFileThresholdBytes { get; set; } = 100 * 1024 * 1024; // 100MB

    /// <summary>
    /// 진행률 보고 간격 (밀리초)
    /// </summary>
    public int ProgressReportIntervalMs { get; set; } = 1000;

    /// <summary>
    /// 에러 발생 시 재시도 횟수
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// 워커 스레드 유지 시간 (밀리초)
    /// </summary>
    public int WorkerKeepAliveMs { get; set; } = 30000;
}

/// <summary>
/// 병렬 처리 성능 통계
/// </summary>
public class ParallelProcessingStats
{
    /// <summary>
    /// 총 처리된 문서 수
    /// </summary>
    public int TotalDocumentsProcessed { get; set; }

    /// <summary>
    /// 총 생성된 청크 수
    /// </summary>
    public int TotalChunksGenerated { get; set; }

    /// <summary>
    /// 평균 처리 시간 (밀리초)
    /// </summary>
    public double AverageProcessingTimeMs { get; set; }

    /// <summary>
    /// 현재 활성 워커 스레드 수
    /// </summary>
    public int ActiveWorkerThreads { get; set; }

    /// <summary>
    /// 최대 동시 처리된 문서 수
    /// </summary>
    public int PeakConcurrentDocuments { get; set; }

    /// <summary>
    /// 현재 메모리 사용량 (바이트)
    /// </summary>
    public long CurrentMemoryUsageBytes { get; set; }

    /// <summary>
    /// 최대 메모리 사용량 (바이트)
    /// </summary>
    public long PeakMemoryUsageBytes { get; set; }

    /// <summary>
    /// 에러 발생 횟수
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// 평균 CPU 사용률 (0.0 ~ 1.0)
    /// </summary>
    public double AverageCpuUtilization { get; set; }

    /// <summary>
    /// 처리량 (문서/초)
    /// </summary>
    public double ThroughputDocumentsPerSecond { get; set; }

    /// <summary>
    /// 통계 수집 시작 시간
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 마지막 업데이트 시간
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}