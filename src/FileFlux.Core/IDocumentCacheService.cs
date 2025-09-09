using FileFlux.Domain;

namespace FileFlux;

/// <summary>
/// 문서 처리 결과 캐싱 서비스 인터페이스
/// </summary>
public interface IDocumentCacheService : IDisposable
{
    /// <summary>
    /// 캐시에서 문서 처리 결과 조회
    /// </summary>
    /// <param name="cacheKey">캐시 키</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>캐시된 문서 결과 또는 null</returns>
    Task<CachedDocumentResult?> GetAsync(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 문서 처리 결과를 캐시에 저장
    /// </summary>
    /// <param name="cacheKey">캐시 키</param>
    /// <param name="chunks">문서 청크 목록</param>
    /// <param name="metadata">문서 메타데이터</param>
    /// <param name="expiration">만료 시간 (선택사항)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task SetAsync(
        string cacheKey, 
        IEnumerable<DocumentChunk> chunks, 
        DocumentMetadata metadata,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시에서 항목 제거
    /// </summary>
    /// <param name="cacheKey">캐시 키</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task RemoveAsync(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 전체 캐시 클리어
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시 통계 조회
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>캐시 성능 통계</returns>
    Task<DocumentCacheStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 파일 해시 기반 캐시 키 생성
    /// </summary>
    /// <param name="filePath">파일 경로</param>
    /// <param name="options">청킹 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>생성된 캐시 키</returns>
    Task<string> GenerateCacheKeyAsync(
        string filePath, 
        ChunkingOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 캐시된 문서 처리 결과
/// </summary>
public class CachedDocumentResult
{
    /// <summary>
    /// 캐시 키
    /// </summary>
    public string CacheKey { get; set; } = string.Empty;

    /// <summary>
    /// 문서 청크 목록
    /// </summary>
    public List<DocumentChunk> Chunks { get; set; } = new();

    /// <summary>
    /// 문서 메타데이터
    /// </summary>
    public DocumentMetadata Metadata { get; set; } = new();

    /// <summary>
    /// 캐시된 시간
    /// </summary>
    public DateTime CachedAt { get; set; }

    /// <summary>
    /// 마지막 접근 시간
    /// </summary>
    public DateTime LastAccessed { get; set; }

    /// <summary>
    /// 추정 메모리 사용량 (바이트)
    /// </summary>
    public long EstimatedMemoryBytes { get; set; }

    /// <summary>
    /// 캐시 히트 횟수
    /// </summary>
    public int HitCount { get; set; }
}

/// <summary>
/// 문서 캐시 설정 옵션
/// </summary>
public class DocumentCacheOptions
{
    /// <summary>
    /// 최대 캐시 항목 수 (기본: 1000)
    /// </summary>
    public int MaxCacheSize { get; set; } = 1000;

    /// <summary>
    /// 최대 메모리 사용량 (MB, 기본: 500MB)
    /// </summary>
    public int MaxMemoryUsageMB { get; set; } = 500;

    /// <summary>
    /// 단일 항목 최대 크기 (MB, 기본: 50MB)
    /// </summary>
    public int MaxItemSizeMB { get; set; } = 50;

    /// <summary>
    /// 기본 만료 시간 (시간, 기본: 24시간)
    /// </summary>
    public int DefaultExpirationHours { get; set; } = 24;

    /// <summary>
    /// 정리 주기 (분, 기본: 30분)
    /// </summary>
    public int CleanupIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// 한 번에 제거할 항목 수 (LRU 정리 시)
    /// </summary>
    public int EvictionBatchSize { get; set; } = 50;

    /// <summary>
    /// 캐시 히트율 임계값 (성능 모니터링용)
    /// </summary>
    public double MinHitRatio { get; set; } = 0.7;
}

/// <summary>
/// 문서 캐시 성능 통계
/// </summary>
public class DocumentCacheStats
{
    /// <summary>
    /// 현재 캐시 항목 수
    /// </summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// 추정 메모리 사용량 (바이트)
    /// </summary>
    public long EstimatedMemoryUsageBytes { get; set; }

    /// <summary>
    /// 총 캐시 히트 횟수
    /// </summary>
    public int TotalHits { get; set; }

    /// <summary>
    /// 최대 캐시 크기
    /// </summary>
    public int MaxCacheSize { get; set; }

    /// <summary>
    /// 최대 메모리 사용량 (바이트)
    /// </summary>
    public long MaxMemoryUsageBytes { get; set; }

    /// <summary>
    /// 가장 오래된 항목의 나이
    /// </summary>
    public TimeSpan OldestItemAge { get; set; }

    /// <summary>
    /// 메모리 효율성 (히트/MB)
    /// </summary>
    public double MemoryEfficiency { get; set; }

    /// <summary>
    /// 캐시 사용률 (0.0 ~ 1.0)
    /// </summary>
    public double UsageRatio => MaxCacheSize > 0 ? (double)ItemCount / MaxCacheSize : 0;

    /// <summary>
    /// 메모리 사용률 (0.0 ~ 1.0)
    /// </summary>
    public double MemoryUsageRatio => MaxMemoryUsageBytes > 0 ? (double)EstimatedMemoryUsageBytes / MaxMemoryUsageBytes : 0;
}