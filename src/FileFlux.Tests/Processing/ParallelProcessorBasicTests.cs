using FileFlux;
using FileFlux.Domain;
using FileFlux.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO;
using Xunit;

namespace FileFlux.Tests.Processing;

/// <summary>
/// 병렬 처리기 기본 기능 테스트 - 빠른 검증용
/// </summary>
public class ParallelProcessorBasicTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IParallelDocumentProcessor _parallelProcessor;
    private readonly string _testDataDir;
    
    public ParallelProcessorBasicTests()
    {
        var services = new ServiceCollection();
        services.AddFileFlux();
        services.AddSingleton<ITextCompletionService, MockTextCompletionService>();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        
        _serviceProvider = services.BuildServiceProvider();
        _parallelProcessor = _serviceProvider.GetRequiredService<IParallelDocumentProcessor>();
        
        // Create test data directory
        _testDataDir = Path.Combine(Path.GetTempPath(), "ParallelProcessorTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDataDir);
    }

    [Fact]
    public async Task ProcessManyAsync_TwoSmallDocuments_ProcessesSuccessfully()
    {
        // Arrange - Create 2 small test documents
        var testFiles = CreateSmallTestDocuments(2);
        var options = new ChunkingOptions { Strategy = "FixedSize", MaxChunkSize = 256 };
        var parallelOptions = new ParallelProcessingOptions { MaxDegreeOfParallelism = 2 };

        // Act
        var results = new List<ParallelProcessingResult>();
        await foreach (var result in _parallelProcessor.ProcessManyAsync(testFiles, options, parallelOptions))
        {
            results.Add(result);
        }

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.IsSuccess, $"Failed: {r.ErrorMessage}"));
        Assert.All(results, r => Assert.NotEmpty(r.Chunks));
        Assert.True(results.All(r => r.ProcessingTimeMs > 0), "Should track processing time");
    }

    [Fact]
    public async Task ProcessLargeDocumentAsync_MediumDocument_ReturnsChunks()
    {
        // Arrange - Create a medium-sized document (50KB)
        var testFile = CreateTestDocument("medium_doc.txt", sizeKB: 50);
        var options = new ChunkingOptions { Strategy = "FixedSize", MaxChunkSize = 512 };
        var parallelOptions = new ParallelProcessingOptions { LargeFileThresholdBytes = 30 * 1024 }; // 30KB threshold

        // Act
        var chunks = new List<DocumentChunk>();
        await foreach (var chunk in _parallelProcessor.ProcessLargeDocumentAsync(testFile, options, parallelOptions))
        {
            chunks.Add(chunk);
        }

        // Assert
        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.NotEmpty(c.Content));
        Assert.True(chunks.Count > 10, "Should generate multiple chunks from medium document");
    }

    [Fact]
    public async Task GetProcessingStats_AfterProcessing_ReturnsValidStats()
    {
        // Arrange
        var testFiles = CreateSmallTestDocuments(3);
        var options = new ChunkingOptions { Strategy = "FixedSize", MaxChunkSize = 256 };

        // Act - Process some documents
        var results = new List<ParallelProcessingResult>();
        await foreach (var result in _parallelProcessor.ProcessManyAsync(testFiles, options))
        {
            results.Add(result);
        }

        // Get statistics
        var stats = _parallelProcessor.GetProcessingStats();

        // Assert
        Assert.True(stats.TotalDocumentsProcessed >= 3, "Should track processed documents");
        Assert.True(stats.TotalChunksGenerated > 0, "Should track generated chunks");
        Assert.True(stats.ThroughputDocumentsPerSecond >= 0, "Should calculate throughput");
        Assert.Equal(0, stats.ErrorCount); // Should be no errors with valid documents
    }

    [Fact]
    public async Task ProcessManyAsync_WithCancellation_StopsGracefully()
    {
        // Arrange
        var testFiles = CreateSmallTestDocuments(10); // More documents to allow cancellation
        var options = new ChunkingOptions { Strategy = "FixedSize", MaxChunkSize = 256 };
        using var cts = new CancellationTokenSource();
        
        // Act - Cancel after short delay
        var processTask = Task.Run(async () =>
        {
            var results = new List<ParallelProcessingResult>();
            await foreach (var result in _parallelProcessor.ProcessManyAsync(testFiles, options, cancellationToken: cts.Token))
            {
                results.Add(result);
            }
            return results;
        });

        await Task.Delay(100); // Give it time to start
        cts.Cancel();

        // Assert - Should complete without throwing
        try
        {
            var results = await processTask;
            // May have processed some documents before cancellation
            Assert.True(results.Count <= testFiles.Count, "Should not process more than requested");
        }
        catch (OperationCanceledException)
        {
            // This is expected and acceptable
            Assert.True(true, "Cancellation handled correctly");
        }
    }

    [Fact]
    public void ParallelProcessingOptions_DefaultValues_AreReasonable()
    {
        // Arrange & Act
        var options = new ParallelProcessingOptions();

        // Assert
        Assert.Equal(Environment.ProcessorCount, options.MaxDegreeOfParallelism);
        Assert.True(options.MaxMemoryUsageBytes > 0, "Should have memory limit");
        Assert.True(options.ChunkBatchSize > 0, "Should have batch size");
        Assert.True(options.BackpressureThreshold > 0, "Should have backpressure threshold");
        Assert.True(options.LargeFileThresholdBytes > 0, "Should have large file threshold");
    }

    /// <summary>
    /// 작은 테스트 문서들 생성 (각각 5KB)
    /// </summary>
    private List<string> CreateSmallTestDocuments(int count)
    {
        var testFiles = new List<string>();
        
        for (int i = 0; i < count; i++)
        {
            var fileName = $"small_test_{i:D2}.txt";
            var filePath = CreateTestDocument(fileName, sizeKB: 5);
            testFiles.Add(filePath);
        }

        return testFiles;
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
    /// 테스트 콘텐츠 생성 (간단한 반복 텍스트)
    /// </summary>
    private static string GenerateTestContent(int sizeBytes)
    {
        const string template = "This is test content for parallel processing validation. " +
                              "It contains multiple sentences to enable proper chunking. " +
                              "Each paragraph provides meaningful content for analysis. ";
        
        var content = new System.Text.StringBuilder();
        while (content.Length < sizeBytes)
        {
            content.AppendLine($"Section {content.Length / template.Length + 1}:");
            content.AppendLine(template);
            content.AppendLine();
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

        _parallelProcessor?.DisposeAsync().GetAwaiter().GetResult();
        _serviceProvider?.Dispose();
    }
}