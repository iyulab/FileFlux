using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FileFlux.Core;
using FileFlux.Domain;

namespace FileFlux.Infrastructure.Caching;

/// <summary>
/// Thread-safe LRU (Least Recently Used) memory cache implementation.
/// Optimized for high-throughput document chunk caching.
/// </summary>
public class LruMemoryCache : IDocumentCache
{
    private readonly Dictionary<string, CacheNode> _cache;
    private readonly LinkedList<CacheNode> _lruList;
    private readonly object _lock = new();
    private readonly int _maxSize;
    private long _hits;
    private long _misses;
    private long _evictions;

    public int Count => _cache.Count;
    public int MaxSize => _maxSize;
    public double HitRate => (_hits + _misses) > 0 ? (double)_hits / (_hits + _misses) : 0;

    public LruMemoryCache(int maxSize = 1000)
    {
        if (maxSize <= 0)
            throw new ArgumentException("Max size must be greater than 0", nameof(maxSize));

        _maxSize = maxSize;
        _cache = new Dictionary<string, CacheNode>(maxSize);
        _lruList = new LinkedList<CacheNode>();
    }

    /// <summary>
    /// Tries to get a value from the cache, moving it to the front if found.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet<T>(string key, out T? value)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                // Check expiration
                if (node.ExpiresAt.HasValue && node.ExpiresAt.Value < DateTime.UtcNow)
                {
                    RemoveNode(node);
                    _misses++;
                    value = default;
                    return false;
                }

                // Move to front (most recently used)
                _lruList.Remove(node.ListNode!);
                _lruList.AddFirst(node.ListNode!);

                _hits++;
                value = (T)node.Value;
                return true;
            }

            _misses++;
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Sets a value in the cache, evicting LRU items if necessary.
    /// </summary>
    public void SetValue<T>(string key, T value, TimeSpan? expiration = null)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        lock (_lock)
        {
            // Update existing
            if (_cache.TryGetValue(key, out var existingNode))
            {
                existingNode.Value = value!;
                existingNode.ExpiresAt = expiration.HasValue
                    ? DateTime.UtcNow.Add(expiration.Value)
                    : null;

                // Move to front
                _lruList.Remove(existingNode.ListNode!);
                _lruList.AddFirst(existingNode.ListNode!);
                return;
            }

            // Evict if at capacity
            while (_cache.Count >= _maxSize)
            {
                var lru = _lruList.Last;
                if (lru != null)
                {
                    RemoveNode(lru.Value);
                    _evictions++;
                }
            }

            // Add new item
            var newNode = new CacheNode
            {
                Key = key,
                Value = value!,
                ExpiresAt = expiration.HasValue
                    ? DateTime.UtcNow.Add(expiration.Value)
                    : null
            };

            var listNode = _lruList.AddFirst(newNode);
            newNode.ListNode = listNode;
            _cache[key] = newNode;
        }
    }

    /// <summary>
    /// Removes a specific key from the cache.
    /// </summary>
    public bool Remove(string key)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                RemoveNode(node);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Clears all items from the cache.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
            _lruList.Clear();
            _hits = 0;
            _misses = 0;
            _evictions = 0;
        }
    }

    /// <summary>
    /// Gets detailed cache statistics.
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        lock (_lock)
        {
            long memoryUsage = 0;

            // Estimate memory usage
            foreach (var node in _cache.Values)
            {
                if (node.Value is string str)
                    memoryUsage += str.Length * 2; // 2 bytes per char
                else if (node.Value is byte[] bytes)
                    memoryUsage += bytes.Length;
                else if (node.Value is DocumentChunk[] chunks)
                {
                    foreach (var chunk in chunks)
                    {
                        memoryUsage += chunk.Content?.Length * 2 ?? 0;
                    }
                }
                else
                    memoryUsage += 64; // Default estimate for objects
            }

            return new CacheStatistics
            {
                Hits = _hits,
                Misses = _misses,
                Evictions = _evictions,
                MemoryUsage = memoryUsage,
                AverageItemSize = _cache.Count > 0 ? memoryUsage / _cache.Count : 0
            };
        }
    }

    /// <summary>
    /// Removes expired entries from the cache.
    /// </summary>
    public void RemoveExpired()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var nodesToRemove = new List<CacheNode>();

            foreach (var node in _cache.Values)
            {
                if (node.ExpiresAt.HasValue && node.ExpiresAt.Value < now)
                {
                    nodesToRemove.Add(node);
                }
            }

            foreach (var node in nodesToRemove)
            {
                RemoveNode(node);
            }
        }
    }

    private void RemoveNode(CacheNode node)
    {
        _cache.Remove(node.Key);
        if (node.ListNode != null)
        {
            _lruList.Remove(node.ListNode);
        }
    }

    private class CacheNode
    {
        public string Key { get; set; } = string.Empty;
        public object Value { get; set; } = null!;
        public DateTime? ExpiresAt { get; set; }
        public LinkedListNode<CacheNode>? ListNode { get; set; }
    }
}

/// <summary>
/// Extension methods for cache warming and batch operations.
/// </summary>
public static class LruMemoryCacheExtensions
{
    /// <summary>
    /// Warms the cache with frequently accessed items.
    /// </summary>
    public static void WarmCache<T>(this IDocumentCache cache, IEnumerable<KeyValuePair<string, T>> items)
    {
        foreach (var item in items)
        {
            cache.SetValue(item.Key, item.Value);
        }
    }

    /// <summary>
    /// Gets multiple items from the cache in a single operation.
    /// </summary>
    public static Dictionary<string, T> GetMany<T>(this IDocumentCache cache, IEnumerable<string> keys)
    {
        var result = new Dictionary<string, T>();

        foreach (var key in keys)
        {
            if (cache.TryGet<T>(key, out var value) && value != null)
            {
                result[key] = value;
            }
        }

        return result;
    }
}
