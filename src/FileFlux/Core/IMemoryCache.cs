using System;
using System.Collections.Generic;

namespace FileFlux.Core;

/// <summary>
/// Interface for document caching with LRU eviction policy.
/// Provides efficient caching for processed document chunks and metadata.
/// Note: Renamed from IMemoryCache to avoid conflict with Microsoft.Extensions.Caching.Memory.IMemoryCache
/// </summary>
public interface IDocumentCache
{
    /// <summary>
    /// Gets the current number of items in the cache.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets the cache hit rate (0.0 to 1.0).
    /// </summary>
    double HitRate { get; }

    /// <summary>
    /// Gets the maximum cache size.
    /// </summary>
    int MaxSize { get; }

    /// <summary>
    /// Tries to get a value from the cache.
    /// </summary>
    bool TryGet<T>(string key, out T? value);

    /// <summary>
    /// Sets a value in the cache with optional expiration.
    /// </summary>
    void SetValue<T>(string key, T value, TimeSpan? expiration = null);

    /// <summary>
    /// Removes a specific key from the cache.
    /// </summary>
    bool Remove(string key);

    /// <summary>
    /// Clears all items from the cache.
    /// </summary>
    void Clear();

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    CacheStatistics GetStatistics();
}

/// <summary>
/// Cache statistics for monitoring and optimization.
/// </summary>
public class CacheStatistics
{
    /// <summary>
    /// Total number of cache hits.
    /// </summary>
    public long Hits { get; set; }

    /// <summary>
    /// Total number of cache misses.
    /// </summary>
    public long Misses { get; set; }

    /// <summary>
    /// Number of evictions due to capacity.
    /// </summary>
    public long Evictions { get; set; }

    /// <summary>
    /// Current memory usage in bytes.
    /// </summary>
    public long MemoryUsage { get; set; }

    /// <summary>
    /// Average item size in bytes.
    /// </summary>
    public long AverageItemSize { get; set; }

    /// <summary>
    /// Cache hit rate percentage.
    /// </summary>
    public double HitRatePercentage =>
        (Hits + Misses) > 0 ? (double)Hits / (Hits + Misses) * 100 : 0;
}
