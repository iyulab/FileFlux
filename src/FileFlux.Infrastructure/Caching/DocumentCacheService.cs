using FileFlux.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace FileFlux.Infrastructure.Caching;

/// <summary>
/// 문서 처리 결과 캐싱 서비스 - 메모리 효율적인 LRU 캐시
/// </summary>
public sealed class DocumentCacheService : IDocumentCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, DateTime> _accessTimes;
    private readonly ILogger<DocumentCacheService> _logger;
    private readonly DocumentCacheOptions _options;
    private readonly object _cleanupLock = new();
    private readonly Timer _cleanupTimer;
    
    public DocumentCacheService(
        DocumentCacheOptions options,
        ILogger<DocumentCacheService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = _options.MaxCacheSize,
            CompactionPercentage = 0.25
        });
        _accessTimes = new ConcurrentDictionary<string, DateTime>();
        
        // Start periodic cleanup
        _cleanupTimer = new Timer(PerformCleanup, null, 
            TimeSpan.FromMinutes(_options.CleanupIntervalMinutes), 
            TimeSpan.FromMinutes(_options.CleanupIntervalMinutes));
        
        _logger.LogInformation("DocumentCacheService initialized with {MaxItems} max items, {MaxMemoryMB}MB limit",
            _options.MaxCacheSize, _options.MaxMemoryUsageMB);
    }

    /// <summary>
    /// 캐시에서 문서 처리 결과 조회
    /// </summary>
    public Task<CachedDocumentResult?> GetAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(cacheKey);
        
        if (_cache.TryGetValue(cacheKey, out var cachedValue) && cachedValue is CachedDocumentResult result)
        {
            // Update access time for LRU
            _accessTimes.AddOrUpdate(cacheKey, DateTime.UtcNow, (_, _) => DateTime.UtcNow);
            
            // Update hit statistics
            result.HitCount++;
            result.LastAccessed = DateTime.UtcNow;
            
            _logger.LogDebug("Cache hit for key: {CacheKey}, chunks: {ChunkCount}", 
                cacheKey, result.Chunks.Count);
            
            return Task.FromResult<CachedDocumentResult?>(result);
        }
        
        _logger.LogDebug("Cache miss for key: {CacheKey}", cacheKey);
        return Task.FromResult<CachedDocumentResult?>(null);
    }

    /// <summary>
    /// 문서 처리 결과를 캐시에 저장
    /// </summary>
    public Task SetAsync(
        string cacheKey, 
        IEnumerable<DocumentChunk> chunks, 
        DocumentMetadata metadata,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(cacheKey);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(metadata);

        var chunkList = chunks.ToList();
        var estimatedSize = EstimateMemoryUsage(chunkList, metadata);
        
        // Check memory limits
        if (estimatedSize > _options.MaxItemSizeMB * 1024 * 1024)
        {
            _logger.LogWarning("Document too large for cache: {SizeMB}MB > {MaxMB}MB", 
                estimatedSize / (1024 * 1024), _options.MaxItemSizeMB);
            return Task.CompletedTask;
        }

        var cachedResult = new CachedDocumentResult
        {
            CacheKey = cacheKey,
            Chunks = chunkList,
            Metadata = metadata,
            CachedAt = DateTime.UtcNow,
            LastAccessed = DateTime.UtcNow,
            EstimatedMemoryBytes = estimatedSize,
            HitCount = 0
        };

        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = expiration.HasValue 
                ? DateTimeOffset.UtcNow.Add(expiration.Value)
                : DateTimeOffset.UtcNow.AddHours(_options.DefaultExpirationHours),
            Size = estimatedSize / 1024, // Size in KB for cache management
            Priority = CacheItemPriority.Normal
        };

        entryOptions.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration
        {
            EvictionCallback = OnCacheItemEvicted
        });

        // Ensure cache size limits before adding
        EnsureCacheSize();
        
        _cache.Set(cacheKey, cachedResult, entryOptions);
        _accessTimes.TryAdd(cacheKey, DateTime.UtcNow);
        
        _logger.LogDebug("Cached document: {CacheKey}, chunks: {ChunkCount}, size: {SizeMB:F2}MB", 
            cacheKey, chunkList.Count, estimatedSize / (1024.0 * 1024.0));
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// 캐시에서 항목 제거
    /// </summary>
    public Task RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(cacheKey);
        
        _cache.Remove(cacheKey);
        _accessTimes.TryRemove(cacheKey, out _);
        
        _logger.LogDebug("Removed from cache: {CacheKey}", cacheKey);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 전체 캐시 클리어
    /// </summary>
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        foreach (var key in _accessTimes.Keys.ToList())
        {
            _cache.Remove(key);
        }
        _accessTimes.Clear();
        
        _logger.LogInformation("Cache cleared");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 캐시 통계 조회
    /// </summary>
    public Task<DocumentCacheStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var itemCount = _accessTimes.Count;
        var totalMemoryUsage = 0L;
        var totalHits = 0;
        var oldestAccess = DateTime.UtcNow;
        var newestAccess = DateTime.MinValue;

        foreach (var kvp in _accessTimes)
        {
            if (_cache.TryGetValue(kvp.Key, out var cachedValue) && cachedValue is CachedDocumentResult item)
            {
                totalMemoryUsage += item.EstimatedMemoryBytes;
                totalHits += item.HitCount;
                
                if (item.LastAccessed < oldestAccess)
                    oldestAccess = item.LastAccessed;
                if (item.LastAccessed > newestAccess)
                    newestAccess = item.LastAccessed;
            }
        }

        var stats = new DocumentCacheStats
        {
            ItemCount = itemCount,
            EstimatedMemoryUsageBytes = totalMemoryUsage,
            TotalHits = totalHits,
            MaxCacheSize = _options.MaxCacheSize,
            MaxMemoryUsageBytes = _options.MaxMemoryUsageMB * 1024L * 1024L,
            OldestItemAge = itemCount > 0 ? DateTime.UtcNow - oldestAccess : TimeSpan.Zero,
            MemoryEfficiency = totalMemoryUsage > 0 ? (double)totalHits / (totalMemoryUsage / (1024.0 * 1024.0)) : 0
        };

        return Task.FromResult(stats);
    }

    /// <summary>
    /// 파일 해시 기반 캐시 키 생성
    /// </summary>
    public Task<string> GenerateCacheKeyAsync(
        string filePath, 
        ChunkingOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentNullException.ThrowIfNull(options);

        // Get file info for cache key generation
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        // Create cache key from file metadata and options
        var keyData = $"{filePath}|{fileInfo.LastWriteTimeUtc:O}|{fileInfo.Length}|{options.Strategy}|{options.MaxChunkSize}|{options.OverlapSize}";
        
        // Generate SHA256 hash for consistent, collision-resistant key
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(keyData));
        var cacheKey = Convert.ToHexString(hashBytes)[..16]; // Use first 16 chars for readability
        
        return Task.FromResult(cacheKey);
    }

    /// <summary>
    /// 메모리 사용량 추정
    /// </summary>
    private static long EstimateMemoryUsage(IEnumerable<DocumentChunk> chunks, DocumentMetadata metadata)
    {
        var totalSize = 0L;
        
        foreach (var chunk in chunks)
        {
            totalSize += chunk.Content.Length * 2; // UTF-16 strings
            totalSize += 200; // Estimated object overhead
        }
        
        totalSize += metadata.FileName?.Length * 2 ?? 0;
        totalSize += 500; // Metadata overhead
        
        return totalSize;
    }

    /// <summary>
    /// 캐시 크기 제한 확보
    /// </summary>
    private void EnsureCacheSize()
    {
        if (_accessTimes.Count < _options.MaxCacheSize) return;

        lock (_cleanupLock)
        {
            if (_accessTimes.Count < _options.MaxCacheSize) return;

            // Remove oldest items (LRU eviction)
            var itemsToRemove = _accessTimes.Count - _options.MaxCacheSize + _options.EvictionBatchSize;
            var oldestItems = _accessTimes
                .OrderBy(kvp => kvp.Value)
                .Take(itemsToRemove)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in oldestItems)
            {
                _cache.Remove(key);
                _accessTimes.TryRemove(key, out _);
            }

            _logger.LogDebug("Evicted {Count} items from cache due to size limit", oldestItems.Count);
        }
    }

    /// <summary>
    /// 주기적인 캐시 정리
    /// </summary>
    private void PerformCleanup(object? state)
    {
        if (!Monitor.TryEnter(_cleanupLock, 100)) return;

        try
        {
            var expiredKeys = new List<string>();
            var expiredCutoff = DateTime.UtcNow.AddHours(-_options.DefaultExpirationHours);

            foreach (var kvp in _accessTimes)
            {
                if (kvp.Value < expiredCutoff)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }

            foreach (var key in expiredKeys)
            {
                _cache.Remove(key);
                _accessTimes.TryRemove(key, out _);
            }

            if (expiredKeys.Count > 0)
            {
                _logger.LogDebug("Cleaned up {Count} expired cache items", expiredKeys.Count);
            }
        }
        finally
        {
            Monitor.Exit(_cleanupLock);
        }
    }

    /// <summary>
    /// 캐시 항목 제거 콜백
    /// </summary>
    private void OnCacheItemEvicted(object key, object? value, EvictionReason reason, object? state)
    {
        if (key is string keyString)
        {
            _accessTimes.TryRemove(keyString, out _);
            
            _logger.LogDebug("Cache item evicted: {Key}, reason: {Reason}", keyString, reason);
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _cache?.Dispose();
        _accessTimes?.Clear();
        GC.SuppressFinalize(this);
    }
}