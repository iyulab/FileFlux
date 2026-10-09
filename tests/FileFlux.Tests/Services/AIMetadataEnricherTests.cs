using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace FileFlux.Tests.Services;

/// <summary>
/// <see cref="AIMetadataEnricher"/> against a scripted analysis service: cancellation, replies without a confidence,
/// the retry delay and the cache key.
/// </summary>
public sealed class AIMetadataEnricherTests
{
    private const string Text = "FileFlux turns documents into chunks for retrieval. It reads PDF, Word and Markdown files.";

    private static AIMetadataEnricher Create(ScriptedAnalysisService service, IMemoryCache? cache = null) =>
        new(new RuleBasedMetadataExtractor(), cache ?? new MemoryCache(new MemoryCacheOptions()), service);

    [Fact]
    public async Task A_cancelled_caller_gets_OperationCanceledException_not_the_rule_based_result()
    {
        var service = new ScriptedAnalysisService(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return "{}";
        });
        var enricher = Create(service);
        using var cts = new CancellationTokenSource();
        service.Started = () => cts.Cancel();

        var options = new MetadataEnrichmentOptions { MaxRetries = 0, ContinueOnEnrichmentFailure = true, TimeoutMs = 60_000 };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => enricher.EnrichAsync(Text, MetadataSchema.General, options, cts.Token));
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task A_timed_out_call_still_falls_back_when_the_caller_did_not_cancel()
    {
        var service = new ScriptedAnalysisService(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return "{}";
        });
        var enricher = Create(service);

        var options = new MetadataEnrichmentOptions { MaxRetries = 0, ContinueOnEnrichmentFailure = true, TimeoutMs = 50 };
        var metadata = await enricher.EnrichAsync(Text, MetadataSchema.General, options, TestContext.Current.CancellationToken);

        Assert.Equal("rule-based", metadata["extractionMethod"]);
    }

    [Fact]
    public async Task A_reply_without_confidence_is_used_with_the_parse_failure_default()
    {
        var service = new ScriptedAnalysisService((_, _) =>
            Task.FromResult("""{"topics":["Chunking"],"keywords":["chunks","retrieval"],"description":"About chunking"}"""));
        var enricher = Create(service);

        var options = new MetadataEnrichmentOptions { MinConfidence = 0.4, MaxRetries = 2, RetryDelayMs = 0 };
        var metadata = await enricher.EnrichAsync(Text, MetadataSchema.General, options, TestContext.Current.CancellationToken);

        Assert.Equal("ai", metadata["extractionMethod"]);
        Assert.Equal(0.5, Assert.IsType<double>(metadata["confidence"]));
        Assert.Equal("About chunking", metadata["description"]);
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task A_reply_without_confidence_is_merged_below_the_default_threshold()
    {
        var service = new ScriptedAnalysisService((_, _) =>
            Task.FromResult("""{"topics":["Chunking"],"description":"About chunking"}"""));
        var enricher = Create(service);

        var metadata = await enricher.EnrichAsync(Text, MetadataSchema.General,
            new MetadataEnrichmentOptions { RetryDelayMs = 0 }, TestContext.Current.CancellationToken);

        Assert.Equal("hybrid", metadata["extractionMethod"]);
        Assert.Equal("About chunking", metadata["description"]);
        Assert.Equal(1, service.Calls);
    }

    [Theory]
    [InlineData(1000, 1, 1000)]
    [InlineData(1000, 2, 2000)]
    [InlineData(1000, 3, 4000)]
    [InlineData(250, 4, 2000)]
    [InlineData(0, 3, 0)]
    public void The_retry_delay_doubles_with_each_retry(int retryDelayMs, int retry, int expected)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(expected), AIMetadataEnricher.RetryDelay(retryDelayMs, retry));
    }

    [Fact]
    public void The_retry_delay_does_not_overflow()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(int.MaxValue), AIMetadataEnricher.RetryDelay(int.MaxValue, 40));
    }

    [Fact]
    public async Task Cached_results_are_not_shared_between_different_prompts()
    {
        var service = new ScriptedAnalysisService((prompt, _) => Task.FromResult(
            prompt.Contains("PROMPT-A", StringComparison.Ordinal)
                ? """{"description":"from A","confidence":0.9}"""
                : """{"description":"from B","confidence":0.9}"""));
        var enricher = Create(service);
        var ct = TestContext.Current.CancellationToken;

        var a = await enricher.EnrichWithCacheAsync(Text, "doc-1", MetadataSchema.General,
            new MetadataEnrichmentOptions { CustomPrompt = "PROMPT-A" }, ct);
        var b = await enricher.EnrichWithCacheAsync(Text, "doc-1", MetadataSchema.General,
            new MetadataEnrichmentOptions { CustomPrompt = "PROMPT-B" }, ct);
        var aAgain = await enricher.EnrichWithCacheAsync(Text, "doc-1", MetadataSchema.General,
            new MetadataEnrichmentOptions { CustomPrompt = "PROMPT-A" }, ct);

        Assert.Equal("from A", a["description"]);
        Assert.Equal("from B", b["description"]);
        Assert.Equal("from A", aAgain["description"]);
        Assert.Equal(2, service.Calls);
    }

    [Fact]
    public async Task Cached_results_are_not_shared_between_extraction_strategies_or_schemas()
    {
        var service = new ScriptedAnalysisService((_, _) => Task.FromResult("""{"description":"d","confidence":0.9}"""));
        var enricher = Create(service);
        var ct = TestContext.Current.CancellationToken;

        await enricher.EnrichWithCacheAsync(Text, "doc-1", MetadataSchema.General,
            new MetadataEnrichmentOptions { ExtractionStrategy = MetadataExtractionStrategy.Fast }, ct);
        await enricher.EnrichWithCacheAsync(Text, "doc-1", MetadataSchema.General,
            new MetadataEnrichmentOptions { ExtractionStrategy = MetadataExtractionStrategy.Deep }, ct);
        await enricher.EnrichWithCacheAsync(Text, "doc-1", MetadataSchema.TechnicalDoc,
            new MetadataEnrichmentOptions { ExtractionStrategy = MetadataExtractionStrategy.Deep }, ct);
        await enricher.EnrichWithCacheAsync(Text, "doc-1", MetadataSchema.TechnicalDoc,
            new MetadataEnrichmentOptions { ExtractionStrategy = MetadataExtractionStrategy.Deep }, ct);

        Assert.Equal(3, service.Calls);
    }

    [Fact]
    public async Task Batch_cache_entries_are_not_shared_between_different_prompts()
    {
        var service = new ScriptedAnalysisService((_, _) => Task.FromResult("""{"description":"d","confidence":0.9}"""));
        var enricher = Create(service);
        var ct = TestContext.Current.CancellationToken;
        BatchMetadataRequest[] requests = [new() { DocumentId = "1", Content = Text, CacheKey = "doc-1" }];

        await enricher.EnrichBatchAsync(requests, MetadataSchema.General, new MetadataEnrichmentOptions { CustomPrompt = "A" }, ct);
        var second = await enricher.EnrichBatchAsync(requests, MetadataSchema.General, new MetadataEnrichmentOptions { CustomPrompt = "B" }, ct);
        var third = await enricher.EnrichBatchAsync(requests, MetadataSchema.General, new MetadataEnrichmentOptions { CustomPrompt = "B" }, ct);

        Assert.False(second[0].FromCache);
        Assert.True(third[0].FromCache);
        Assert.Equal(2, service.Calls);
    }

    /// <summary>An analysis service whose <see cref="GenerateAsync(string, CancellationToken)"/> runs a script.</summary>
    private sealed class ScriptedAnalysisService(Func<string, CancellationToken, Task<string>> generate) : IDocumentAnalysisService
    {
        private int _calls;

        public int Calls => _calls;

        public Action? Started { get; set; }

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            var task = generate(prompt, cancellationToken);
            Started?.Invoke();
            return task;
        }

        public DocumentAnalysisServiceInfo ProviderInfo { get; } = new() { Name = "scripted" };

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
