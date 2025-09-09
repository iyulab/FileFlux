using FileFlux.Domain;
using System.Runtime.CompilerServices;

namespace FileFlux;

/// <summary>
/// 스트리밍 문서 처리기 인터페이스 - 실시간 청크 스트리밍 및 캐시 통합
/// </summary>
public interface IStreamingDocumentProcessor
{
    /// <summary>
    /// 스트리밍 방식으로 문서 처리 - 청크를 생성 즉시 반환
    /// </summary>
    /// <param name="filePath">처리할 문서 파일 경로</param>
    /// <param name="chunkingOptions">청킹 옵션</param>
    /// <param name="streamingOptions">스트리밍 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>실시간 청크 스트림</returns>
    IAsyncEnumerable<StreamingChunkResult> ProcessStreamAsync(
        string filePath,
        ChunkingOptions? chunkingOptions = null,
        StreamingOptions? streamingOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 다중 파일 스트리밍 처리 - 파일별 결과를 실시간 반환
    /// </summary>
    /// <param name="filePaths">처리할 문서 파일 경로 목록</param>
    /// <param name="chunkingOptions">청킹 옵션</param>
    /// <param name="streamingOptions">스트리밍 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>파일별 처리 결과 스트림</returns>
    IAsyncEnumerable<StreamingBatchResult> ProcessMultipleStreamAsync(
        IEnumerable<string> filePaths,
        ChunkingOptions? chunkingOptions = null,
        StreamingOptions? streamingOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 스트리밍 처리 통계 조회
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>스트리밍 성능 통계</returns>
    Task<StreamingStats> GetStreamingStatsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 스트리밍 청크 처리 결과
/// </summary>
public class StreamingChunkResult
{
    /// <summary>
    /// 생성된 문서 청크
    /// </summary>
    public DocumentChunk Chunk { get; set; } = new();

    /// <summary>
    /// 캐시에서 가져온 결과인지 여부
    /// </summary>
    public bool IsFromCache { get; set; }

    /// <summary>
    /// 처리 시간 (밀리초)
    /// </summary>
    public long ProcessingTimeMs { get; set; }

    /// <summary>
    /// 현재 청크 인덱스
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// 총 예상 청크 수 (알 수 있는 경우)
    /// </summary>
    public int? TotalEstimatedChunks { get; set; }

    /// <summary>
    /// 처리 진행률 (0.0 ~ 1.0, 예상 청크 수가 있는 경우)
    /// </summary>
    public double? Progress => TotalEstimatedChunks.HasValue && TotalEstimatedChunks > 0
        ? (double)(ChunkIndex + 1) / TotalEstimatedChunks.Value
        : null;
}

/// <summary>
/// 스트리밍 배치 처리 결과
/// </summary>
public class StreamingBatchResult
{
    /// <summary>
    /// 처리된 파일 경로
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 파일 인덱스 (배치 내에서의 순서)
    /// </summary>
    public int FileIndex { get; set; }

    /// <summary>
    /// 청크 결과 목록
    /// </summary>
    public List<StreamingChunkResult> ChunkResults { get; set; } = new();

    /// <summary>
    /// 파일 처리 완료 여부
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// 캐시에서 가져온 결과인지 여부
    /// </summary>
    public bool IsFromCache { get; set; }

    /// <summary>
    /// 처리 시간 (밀리초)
    /// </summary>
    public long ProcessingTimeMs { get; set; }

    /// <summary>
    /// 에러 메시지 (실패 시)
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// 파일 처리 성공 여부
    /// </summary>
    public bool IsSuccess => string.IsNullOrEmpty(Error);

    /// <summary>
    /// 총 청크 수
    /// </summary>
    public int TotalChunks => ChunkResults.Count;
}

/// <summary>
/// 스트리밍 처리 옵션
/// </summary>
public class StreamingOptions
{
    /// <summary>
    /// 백프레셔 배치 크기 (이 크기마다 지연 적용)
    /// </summary>
    public int BackpressureBatchSize { get; set; } = 100;

    /// <summary>
    /// 백프레셔 지연 시간 (밀리초)
    /// </summary>
    public int BackpressureDelayMs { get; set; } = 10;

    /// <summary>
    /// 메모리 최적화 활성화 여부
    /// </summary>
    public bool EnableMemoryOptimization { get; set; } = true;

    /// <summary>
    /// 채널 용량 (다중 파일 처리 시)
    /// </summary>
    public int ChannelCapacity { get; set; } = 1000;

    /// <summary>
    /// 최대 동시 파일 처리 수
    /// </summary>
    public int MaxConcurrentFiles { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// 중간 결과 반환 크기 (큰 파일 처리 시)
    /// </summary>
    public int IntermediateYieldSize { get; set; } = 500;

    /// <summary>
    /// 캐시 만료 시간 (시간)
    /// </summary>
    public int CacheExpirationHours { get; set; } = 24;

    /// <summary>
    /// 캐시된 결과 배치 크기
    /// </summary>
    public int CachedResultBatchSize { get; set; } = 50;

    /// <summary>
    /// 캐시된 결과 지연 시간 (밀리초)
    /// </summary>
    public int CachedResultDelayMs { get; set; } = 5;

    /// <summary>
    /// 우선순위 기반 처리 활성화
    /// </summary>
    public bool EnablePriorityProcessing { get; set; } = false;

    /// <summary>
    /// 진행률 보고 간격 (청크 수)
    /// </summary>
    public int ProgressReportInterval { get; set; } = 100;
}

/// <summary>
/// 스트리밍 처리 성능 통계
/// </summary>
public class StreamingStats
{
    /// <summary>
    /// 캐시 히트 횟수
    /// </summary>
    public int CacheHitCount { get; set; }

    /// <summary>
    /// 캐시 항목 수
    /// </summary>
    public int CacheItemCount { get; set; }

    /// <summary>
    /// 캐시 메모리 사용량 (바이트)
    /// </summary>
    public long CacheMemoryUsageBytes { get; set; }

    /// <summary>
    /// 캐시 효율성 (히트/MB)
    /// </summary>
    public double CacheEfficiency { get; set; }

    /// <summary>
    /// 캐시 사용률 (0.0 ~ 1.0)
    /// </summary>
    public double CacheUsageRatio { get; set; }

    /// <summary>
    /// 평균 스트리밍 속도 (청크/초)
    /// </summary>
    public double AverageStreamingSpeed { get; set; }

    /// <summary>
    /// 현재 활성 스트림 수
    /// </summary>
    public int ActiveStreams { get; set; }

    /// <summary>
    /// 총 처리된 청크 수
    /// </summary>
    public long TotalChunksProcessed { get; set; }

    /// <summary>
    /// 마지막 업데이트 시간
    /// </summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// 캐시 메모리 사용량 (MB)
    /// </summary>
    public double CacheMemoryUsageMB => CacheMemoryUsageBytes / (1024.0 * 1024.0);

    /// <summary>
    /// 캐시 효율성 등급
    /// </summary>
    public string CacheEfficiencyGrade => CacheEfficiency switch
    {
        >= 100 => "Excellent",
        >= 50 => "Good", 
        >= 25 => "Fair",
        >= 10 => "Poor",
        _ => "Very Poor"
    };
}