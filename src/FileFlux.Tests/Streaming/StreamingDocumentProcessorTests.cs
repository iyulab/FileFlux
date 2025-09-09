using FileFlux;
using FileFlux.Domain;
using FileFlux.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO;
using Xunit;

namespace FileFlux.Tests.Streaming;

/// <summary>
/// 스트리밍 문서 처리기 테스트 - 캐시 및 백프레셔 검증
/// </summary>
public class StreamingDocumentProcessorTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IStreamingDocumentProcessor _streamingProcessor;
    private readonly IDocumentCacheService _cacheService;
    private readonly string _testDataDir;
    
    public StreamingDocumentProcessorTests()
    {
        var services = new ServiceCollection();
        services.AddFileFlux();
        services.AddSingleton<ITextCompletionService, MockTextCompletionService>();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        
        _serviceProvider = services.BuildServiceProvider();
        _streamingProcessor = _serviceProvider.GetRequiredService<IStreamingDocumentProcessor>();
        _cacheService = _serviceProvider.GetRequiredService<IDocumentCacheService>();
        
        // Create test data directory
        _testDataDir = Path.Combine(Path.GetTempPath(), "StreamingProcessorTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDataDir);
    }

    [Fact]
    public async Task ProcessStreamAsync_SmallDocument_StreamsChunksRealTime()
    {
        // Arrange - Create small test document
        var testFile = CreateTestDocument("small_test.txt", sizeKB: 10);
        var options = new ChunkingOptions { Strategy = "FixedSize", MaxChunkSize = 256 };
        
        // Act
        var chunks = new List<StreamingChunkResult>();
        await foreach (var chunkResult in _streamingProcessor.ProcessStreamAsync(testFile, options))
        {
            chunks.Add(chunkResult);
        }
        
        // Assert
        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.NotNull(c.Chunk));
        Assert.All(chunks, c => Assert.False(c.IsFromCache)); // First time processing
        Assert.True(chunks.Count > 5, "Should generate multiple chunks");
        
        // Verify chunk indices are sequential
        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
        }
    }

    [Fact]
    public async Task ProcessStreamAsync_SameDocumentTwice_UsesCache()
    {
        // Arrange
        var testFile = CreateTestDocument("cache_test.txt", sizeKB: 5);
        var options = new ChunkingOptions { Strategy = "FixedSize", MaxChunkSize = 256 };
        
        // Act 1: First processing (should cache)
        var firstRun = new List<StreamingChunkResult>();
        await foreach (var chunkResult in _streamingProcessor.ProcessStreamAsync(testFile, options))
        {
            firstRun.Add(chunkResult);
        }
        
        // Act 2: Second processing (should use cache)
        var secondRun = new List<StreamingChunkResult>();
        await foreach (var chunkResult in _streamingProcessor.ProcessStreamAsync(testFile, options))
        {
            secondRun.Add(chunkResult);
        }
        
        // Assert
        Assert.Equal(firstRun.Count, secondRun.Count);
        Assert.All(firstRun, c => Assert.False(c.IsFromCache));
        Assert.All(secondRun, c => Assert.True(c.IsFromCache));
        
        // Content should be identical
        for (int i = 0; i < firstRun.Count; i++)
        {
            Assert.Equal(firstRun[i].Chunk.Content, secondRun[i].Chunk.Content);
        }
    }

    [Fact]
    public async Task ProcessMultipleStreamAsync_MultipleFiles_StreamsInParallel()
    {
        // Arrange - Create multiple test files
        var testFiles = CreateMultipleTestDocuments(3, sizeKB: 8);
        var options = new ChunkingOptions { Strategy = "FixedSize", MaxChunkSize = 256 };
        var streamingOptions = new StreamingOptions { MaxConcurrentFiles = 2 };
        
        // Act
        var batchResults = new List<StreamingBatchResult>();
        await foreach (var batchResult in _streamingProcessor.ProcessMultipleStreamAsync(testFiles, options, streamingOptions))
        {
            batchResults.Add(batchResult);
        }
        
        // Assert
        var completedResults = batchResults.Where(r => r.IsCompleted).ToList();
        Assert.Equal(testFiles.Count, completedResults.Count);
        Assert.All(completedResults, r => Assert.True(r.IsSuccess));
        Assert.All(completedResults, r => Assert.NotEmpty(r.ChunkResults));
        
        // Verify all files were processed
        var processedFiles = completedResults.Select(r => r.FilePath).ToHashSet();
        Assert.Equal(testFiles.Count, processedFiles.Count);
    }

    [Fact]
    public async Task GetStreamingStatsAsync_AfterProcessing_ReturnsValidStats()
    {
        // Arrange & Act - Process a document to generate stats
        var testFile = CreateTestDocument("stats_test.txt", sizeKB: 5);
        var options = new ChunkingOptions { Strategy = "FixedSize", MaxChunkSize = 256 };
        
        await foreach (var _ in _streamingProcessor.ProcessStreamAsync(testFile, options))
        {
            // Just consume the stream
        }
        
        // Get statistics
        var stats = await _streamingProcessor.GetStreamingStatsAsync();
        
        // Assert
        Assert.True(stats.CacheItemCount >= 0);
        Assert.True(stats.CacheMemoryUsageBytes >= 0);
        Assert.NotEqual(DateTime.MinValue, stats.LastUpdated);
    }

    [Fact]
    public async Task ProcessStreamAsync_WithCustomStreamingOptions_RespectsOptions()
    {
        // Arrange
        var testFile = CreateTestDocument("options_test.txt", sizeKB: 20);
        var chunkingOptions = new ChunkingOptions { Strategy = "FixedSize", MaxChunkSize = 256 };
        var streamingOptions = new StreamingOptions 
        { 
            BackpressureBatchSize = 5,
            BackpressureDelayMs = 1,
            EnableMemoryOptimization = true
        };
        
        // Act
        var chunks = new List<StreamingChunkResult>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        await foreach (var chunkResult in _streamingProcessor.ProcessStreamAsync(testFile, chunkingOptions, streamingOptions))
        {
            chunks.Add(chunkResult);
        }
        
        stopwatch.Stop();
        
        // Assert
        Assert.NotEmpty(chunks);
        // With backpressure, processing should take some minimum time
        Assert.True(stopwatch.ElapsedMilliseconds > 10, "Backpressure should add some delay");
    }

    /// <summary>
    /// 테스트 문서 생성
    /// </summary>
    private string CreateTestDocument(string fileName, int sizeKB)
    {
        var filePath = Path.Combine(_testDataDir, fileName);
        var content = GenerateTestContent(sizeKB * 1024);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    /// <summary>
    /// 여러 테스트 문서 생성
    /// </summary>
    private List<string> CreateMultipleTestDocuments(int count, int sizeKB)
    {
        var files = new List<string>();
        for (int i = 0; i < count; i++)
        {
            var fileName = $"multi_test_{i:D2}.txt";
            files.Add(CreateTestDocument(fileName, sizeKB));
        }
        return files;
    }

    /// <summary>
    /// 테스트 콘텐츠 생성
    /// </summary>
    private static string GenerateTestContent(int sizeBytes)
    {
        const string template = "This is streaming test content for validation. " +
                              "It contains multiple sentences for proper chunking. " +
                              "Each section provides structured content for analysis. ";
        
        var content = new System.Text.StringBuilder();
        var sectionNumber = 1;
        
        while (content.Length < sizeBytes)
        {
            content.AppendLine($"## Section {sectionNumber}");
            content.AppendLine(template);
            content.AppendLine($"Additional content for section {sectionNumber} with detailed information.");
            content.AppendLine();
            sectionNumber++;
        }

        return content.ToString()[..Math.Min(content.Length, sizeBytes)];
    }

    public void Dispose()
    {
        // Clean up test files
        try
        {
            if (Directory.Exists(_testDataDir))
            {
                Directory.Delete(_testDataDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests
        }

        _cacheService?.Dispose();
        _serviceProvider?.Dispose();
    }
}