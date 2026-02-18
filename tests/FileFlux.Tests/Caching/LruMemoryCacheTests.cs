using FileFlux.Core;
using FileFlux.Infrastructure.Caching;
using FluentAssertions;

namespace FileFlux.Tests.Caching;

public class LruMemoryCacheTests
{
    #region Constructor

    [Fact]
    public void Constructor_Default_MaxSize1000()
    {
        var cache = new LruMemoryCache();

        cache.MaxSize.Should().Be(1000);
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void Constructor_CustomMaxSize_SetsMaxSize()
    {
        var cache = new LruMemoryCache(50);

        cache.MaxSize.Should().Be(50);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_InvalidMaxSize_ThrowsArgumentException(int maxSize)
    {
        var act = () => new LruMemoryCache(maxSize);

        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region SetValue / TryGet

    [Fact]
    public void SetValue_ThenTryGet_ReturnsValue()
    {
        var cache = new LruMemoryCache();

        cache.SetValue("key1", "value1");
        var found = cache.TryGet<string>("key1", out var value);

        found.Should().BeTrue();
        value.Should().Be("value1");
        cache.Count.Should().Be(1);
    }

    [Fact]
    public void TryGet_NonExistentKey_ReturnsFalse()
    {
        var cache = new LruMemoryCache();

        var found = cache.TryGet<string>("missing", out var value);

        found.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void SetValue_NullValue_ThrowsArgumentNullException()
    {
        var cache = new LruMemoryCache();

        var act = () => cache.SetValue<string>("key1", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SetValue_SameKey_UpdatesValue()
    {
        var cache = new LruMemoryCache();

        cache.SetValue("key1", "old");
        cache.SetValue("key1", "new");

        cache.TryGet<string>("key1", out var value).Should().BeTrue();
        value.Should().Be("new");
        cache.Count.Should().Be(1);
    }

    [Fact]
    public void SetValue_DifferentTypes_WorksCorrectly()
    {
        var cache = new LruMemoryCache();

        cache.SetValue("int_key", 42);
        cache.SetValue("string_key", "hello");

        cache.TryGet<int>("int_key", out var intVal).Should().BeTrue();
        intVal.Should().Be(42);

        cache.TryGet<string>("string_key", out var strVal).Should().BeTrue();
        strVal.Should().Be("hello");
    }

    #endregion

    #region LRU Eviction

    [Fact]
    public void SetValue_ExceedsMaxSize_EvictsLeastRecentlyUsed()
    {
        var cache = new LruMemoryCache(3);

        cache.SetValue("a", "val_a");
        cache.SetValue("b", "val_b");
        cache.SetValue("c", "val_c");
        // Cache is full at 3, adding 4th should evict "a" (oldest)
        cache.SetValue("d", "val_d");

        cache.Count.Should().Be(3);
        cache.TryGet<string>("a", out _).Should().BeFalse();
        cache.TryGet<string>("b", out _).Should().BeTrue();
        cache.TryGet<string>("c", out _).Should().BeTrue();
        cache.TryGet<string>("d", out _).Should().BeTrue();
    }

    [Fact]
    public void TryGet_PromotesItemToMostRecent()
    {
        var cache = new LruMemoryCache(3);

        cache.SetValue("a", "val_a");
        cache.SetValue("b", "val_b");
        cache.SetValue("c", "val_c");

        // Access "a" to promote it
        cache.TryGet<string>("a", out _);

        // "b" is now least recently used, should be evicted
        cache.SetValue("d", "val_d");

        cache.TryGet<string>("a", out _).Should().BeTrue();
        cache.TryGet<string>("b", out _).Should().BeFalse();
        cache.TryGet<string>("c", out _).Should().BeTrue();
        cache.TryGet<string>("d", out _).Should().BeTrue();
    }

    [Fact]
    public void SetValue_Update_PromotesItem()
    {
        var cache = new LruMemoryCache(3);

        cache.SetValue("a", "val_a");
        cache.SetValue("b", "val_b");
        cache.SetValue("c", "val_c");

        // Update "a" to promote it
        cache.SetValue("a", "updated_a");

        // "b" is now least recently used
        cache.SetValue("d", "val_d");

        cache.TryGet<string>("a", out var val).Should().BeTrue();
        val.Should().Be("updated_a");
        cache.TryGet<string>("b", out _).Should().BeFalse();
    }

    #endregion

    #region Expiration

    [Fact]
    public void SetValue_WithExpiration_ExpiresAfterTimeout()
    {
        var cache = new LruMemoryCache();

        cache.SetValue("key1", "value1", TimeSpan.FromMilliseconds(1));

        // Wait for expiration
        Thread.Sleep(50);

        cache.TryGet<string>("key1", out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void SetValue_WithoutExpiration_DoesNotExpire()
    {
        var cache = new LruMemoryCache();

        cache.SetValue("key1", "value1");

        // Should still be there
        cache.TryGet<string>("key1", out var value).Should().BeTrue();
        value.Should().Be("value1");
    }

    [Fact]
    public void RemoveExpired_RemovesOnlyExpiredEntries()
    {
        var cache = new LruMemoryCache();

        cache.SetValue("expires", "val1", TimeSpan.FromMilliseconds(1));
        cache.SetValue("stays", "val2"); // no expiration

        Thread.Sleep(50);
        cache.RemoveExpired();

        cache.Count.Should().Be(1);
        cache.TryGet<string>("stays", out _).Should().BeTrue();
        cache.TryGet<string>("expires", out _).Should().BeFalse();
    }

    #endregion

    #region Remove

    [Fact]
    public void Remove_ExistingKey_ReturnsTrue()
    {
        var cache = new LruMemoryCache();
        cache.SetValue("key1", "value1");

        cache.Remove("key1").Should().BeTrue();
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void Remove_NonExistentKey_ReturnsFalse()
    {
        var cache = new LruMemoryCache();

        cache.Remove("missing").Should().BeFalse();
    }

    #endregion

    #region Clear

    [Fact]
    public void Clear_RemovesAllItems()
    {
        var cache = new LruMemoryCache();
        cache.SetValue("a", "1");
        cache.SetValue("b", "2");
        cache.SetValue("c", "3");

        cache.Clear();

        cache.Count.Should().Be(0);
        cache.TryGet<string>("a", out _).Should().BeFalse();
    }

    [Fact]
    public void Clear_ResetsStatistics()
    {
        var cache = new LruMemoryCache();
        cache.SetValue("a", "1");
        cache.TryGet<string>("a", out _);
        cache.TryGet<string>("missing", out _);

        cache.Clear();
        var stats = cache.GetStatistics();

        stats.Hits.Should().Be(0);
        stats.Misses.Should().Be(0);
        stats.Evictions.Should().Be(0);
    }

    #endregion

    #region HitRate & Statistics

    [Fact]
    public void HitRate_NoAccess_ReturnsZero()
    {
        var cache = new LruMemoryCache();

        cache.HitRate.Should().Be(0);
    }

    [Fact]
    public void HitRate_AllHits_ReturnsOne()
    {
        var cache = new LruMemoryCache();
        cache.SetValue("key", "val");

        cache.TryGet<string>("key", out _);
        cache.TryGet<string>("key", out _);

        cache.HitRate.Should().Be(1.0);
    }

    [Fact]
    public void HitRate_MixedAccess_CalculatesCorrectly()
    {
        var cache = new LruMemoryCache();
        cache.SetValue("key", "val");

        cache.TryGet<string>("key", out _);    // hit
        cache.TryGet<string>("miss1", out _);   // miss
        cache.TryGet<string>("key", out _);    // hit
        cache.TryGet<string>("miss2", out _);   // miss

        cache.HitRate.Should().Be(0.5);
    }

    [Fact]
    public void GetStatistics_TracksEvictions()
    {
        var cache = new LruMemoryCache(2);
        cache.SetValue("a", "1");
        cache.SetValue("b", "2");
        cache.SetValue("c", "3"); // evicts "a"

        var stats = cache.GetStatistics();

        stats.Evictions.Should().Be(1);
    }

    [Fact]
    public void GetStatistics_StringMemoryUsage_CalculatesCorrectly()
    {
        var cache = new LruMemoryCache();
        cache.SetValue("key", "hello"); // 5 chars * 2 bytes = 10 bytes

        var stats = cache.GetStatistics();

        stats.MemoryUsage.Should().Be(10);
        stats.AverageItemSize.Should().Be(10);
    }

    [Fact]
    public void GetStatistics_ByteArrayMemoryUsage()
    {
        var cache = new LruMemoryCache();
        cache.SetValue("key", new byte[100]);

        var stats = cache.GetStatistics();

        stats.MemoryUsage.Should().Be(100);
    }

    [Fact]
    public void GetStatistics_GenericObjectMemoryUsage_DefaultsTo64()
    {
        var cache = new LruMemoryCache();
        cache.SetValue("key", new { Name = "test" });

        var stats = cache.GetStatistics();

        stats.MemoryUsage.Should().Be(64);
    }

    #endregion

    #region Extensions

    [Fact]
    public void WarmCache_PopulatesCacheFromKeyValuePairs()
    {
        var cache = new LruMemoryCache();
        var items = new Dictionary<string, string>
        {
            ["a"] = "1",
            ["b"] = "2",
            ["c"] = "3"
        };

        cache.WarmCache(items);

        cache.Count.Should().Be(3);
        cache.TryGet<string>("a", out var val).Should().BeTrue();
        val.Should().Be("1");
    }

    [Fact]
    public void GetMany_ReturnsOnlyCachedItems()
    {
        var cache = new LruMemoryCache();
        cache.SetValue("a", "1");
        cache.SetValue("b", "2");

        var results = cache.GetMany<string>(["a", "b", "missing"]);

        results.Should().HaveCount(2);
        results["a"].Should().Be("1");
        results["b"].Should().Be("2");
    }

    [Fact]
    public void GetMany_EmptyKeys_ReturnsEmpty()
    {
        var cache = new LruMemoryCache();

        var results = cache.GetMany<string>(Array.Empty<string>());

        results.Should().BeEmpty();
    }

    #endregion
}
