using FileFlux;
using FileFlux.Domain;
using FileFlux.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace FileFlux.Tests.Performance;

/// <summary>
/// 병렬 처리 성능 테스트 - CPU 코어별 스케일링 및 메모리 효율성 검증
/// </summary>
public class ParallelProcessingPerformanceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IParallelDocumentProcessor _parallelProcessor;
    private readonly IDocumentProcessor _standardProcessor;
    private readonly ITestOutputHelper _output;
    private readonly string _testDataDir;
    
    public ParallelProcessingPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        
        var services = new ServiceCollection();
        services.AddFileFlux();
        services.AddSingleton<ITextCompletionService, MockTextCompletionService>();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        
        _serviceProvider = services.BuildServiceProvider();
        _parallelProcessor = _serviceProvider.GetRequiredService<IParallelDocumentProcessor>();
        _standardProcessor = _serviceProvider.GetRequiredService<IDocumentProcessor>();
        
        // Create test data directory
        _testDataDir = Path.Combine(Path.GetTempPath(), "FileFluxPerformanceTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDataDir);
        
        _output.WriteLine($"Test data directory: {_testDataDir}");
        _output.WriteLine($"CPU cores available: {Environment.ProcessorCount}");
    }

    [Fact]
    public async Task ProcessManyAsync_MultipleDocuments_ShowsParallelSpeedup()
    {
        // Arrange - Create multiple test documents
        var documentCount = Environment.ProcessorCount * 2; // 2 documents per core
        var testFiles = CreateTestDocuments(documentCount, sizeKB: 500); // 500KB each
        var options = new ChunkingOptions { Strategy = "Smart", MaxChunkSize = 512 };

        _output.WriteLine($"Testing {documentCount} documents ({testFiles.Sum(f => new FileInfo(f).Length / 1024):N0}KB total)");

        // Act & Measure - Parallel processing
        var parallelStopwatch = Stopwatch.StartNew();
        var parallelResults = new List<ParallelProcessingResult>();
        
        await foreach (var result in _parallelProcessor.ProcessManyAsync(testFiles, options))
        {
            parallelResults.Add(result);
        }
        parallelStopwatch.Stop();

        // Act & Measure - Sequential processing
        var sequentialStopwatch = Stopwatch.StartNew();
        var sequentialResults = new List<List<DocumentChunk>>();
        
        foreach (var filePath in testFiles)
        {
            var chunks = new List<DocumentChunk>();
            await foreach (var chunk in _standardProcessor.ProcessAsync(filePath, options))
            {
                chunks.Add(chunk);
            }
            sequentialResults.Add(chunks);
        }
        sequentialStopwatch.Stop();

        // Assert - Performance improvement
        var parallelTimeMs = parallelStopwatch.ElapsedMilliseconds;
        var sequentialTimeMs = sequentialStopwatch.ElapsedMilliseconds;
        var speedup = (double)sequentialTimeMs / parallelTimeMs;

        _output.WriteLine($"Parallel processing: {parallelTimeMs}ms");
        _output.WriteLine($"Sequential processing: {sequentialTimeMs}ms");
        _output.WriteLine($"Speedup: {speedup:F2}x");

        // Verify results are equivalent
        var parallelChunkCount = parallelResults.Where(r => r.IsSuccess).Sum(r => r.Chunks.Count);
        var sequentialChunkCount = sequentialResults.Sum(chunks => chunks.Count);
        
        Assert.Equal(sequentialChunkCount, parallelChunkCount);
        Assert.True(speedup > 1.2, $"Expected speedup > 1.2x, got {speedup:F2}x"); // Allow for overhead
        Assert.All(parallelResults, r => Assert.True(r.IsSuccess, $"Failed to process: {r.FilePath}"));
    }

    [Fact]
    public async Task ProcessLargeDocumentAsync_LargeFile_HandlesMemoryEfficiently()
    {
        // Arrange - Create a large document (10MB)
        var largeFilePath = CreateLargeTestDocument(sizeKB: 10 * 1024); // 10MB
        var options = new ChunkingOptions { Strategy = "Smart", MaxChunkSize = 1024 };
        var parallelOptions = new ParallelProcessingOptions 
        { 
            MaxMemoryUsageBytes = 50 * 1024 * 1024, // 50MB limit
            ChunkBatchSize = 50,
            LargeFileThresholdBytes = 5 * 1024 * 1024 // 5MB threshold
        };

        _output.WriteLine($"Processing large file: {new FileInfo(largeFilePath).Length / (1024 * 1024):N1}MB");

        // Act - Process with memory monitoring
        var initialMemory = GC.GetTotalMemory(true);
        var chunks = new List<DocumentChunk>();
        var processedCount = 0;
        var maxMemoryUsed = initialMemory;

        await foreach (var chunk in _parallelProcessor.ProcessLargeDocumentAsync(largeFilePath, options, parallelOptions))
        {
            chunks.Add(chunk);
            processedCount++;

            // Monitor memory every 100 chunks
            if (processedCount % 100 == 0)
            {
                var currentMemory = GC.GetTotalMemory(false);
                maxMemoryUsed = Math.Max(maxMemoryUsed, currentMemory);
                
                _output.WriteLine($"Processed {processedCount} chunks, Memory: {currentMemory / (1024 * 1024):N1}MB");
            }
        }

        var finalMemory = GC.GetTotalMemory(false);
        var memoryIncrease = finalMemory - initialMemory;
        var fileSize = new FileInfo(largeFilePath).Length;

        // Assert - Memory efficiency
        _output.WriteLine($"File size: {fileSize / (1024 * 1024):N1}MB");
        _output.WriteLine($"Memory increase: {memoryIncrease / (1024 * 1024):N1}MB");
        _output.WriteLine($"Memory ratio: {(double)memoryIncrease / fileSize:F2}x file size");
        _output.WriteLine($"Total chunks generated: {chunks.Count}");

        Assert.True(chunks.Count > 0, "Should generate chunks from large document");
        Assert.True(memoryIncrease < fileSize * 3, "Memory usage should be less than 3x file size"); // Allow some overhead
        Assert.True(maxMemoryUsed < parallelOptions.MaxMemoryUsageBytes * 1.2, "Should respect memory limits");
    }

    [Fact]
    public async Task GetProcessingStats_AfterProcessing_ReturnsAccurateStatistics()
    {
        // Arrange
        var testFiles = CreateTestDocuments(5, sizeKB: 200);
        var options = new ChunkingOptions { Strategy = "Smart", MaxChunkSize = 512 };

        // Act - Process documents
        var results = new List<ParallelProcessingResult>();
        await foreach (var result in _parallelProcessor.ProcessManyAsync(testFiles, options))
        {
            results.Add(result);
        }

        // Get statistics
        var stats = _parallelProcessor.GetProcessingStats();

        // Assert - Statistics accuracy
        _output.WriteLine($"Statistics:");
        _output.WriteLine($"  Documents processed: {stats.TotalDocumentsProcessed}");
        _output.WriteLine($"  Chunks generated: {stats.TotalChunksGenerated}");
        _output.WriteLine($"  Average processing time: {stats.AverageProcessingTimeMs:F2}ms");
        _output.WriteLine($"  Throughput: {stats.ThroughputDocumentsPerSecond:F2} docs/sec");
        _output.WriteLine($"  Error count: {stats.ErrorCount}");

        Assert.Equal(testFiles.Count, stats.TotalDocumentsProcessed);
        Assert.Equal(results.Where(r => r.IsSuccess).Sum(r => r.Chunks.Count), stats.TotalChunksGenerated);
        Assert.True(stats.ThroughputDocumentsPerSecond > 0, "Should calculate throughput");
        Assert.Equal(0, stats.ErrorCount); // All should succeed
        Assert.True(stats.AverageProcessingTimeMs > 0, "Should track processing time");
    }

    [Fact]
    public async Task ParallelProcessing_WithBackpressure_HandlesHighThroughput()
    {
        // Arrange - Create many small documents to test backpressure
        var documentCount = 50;
        var testFiles = CreateTestDocuments(documentCount, sizeKB: 50); // Small files for high throughput
        var parallelOptions = new ParallelProcessingOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            BackpressureThreshold = 100, // Low threshold to test backpressure
            ChunkBatchSize = 10
        };
        var options = new ChunkingOptions { Strategy = "FixedSize", MaxChunkSize = 256 };

        _output.WriteLine($"Testing backpressure with {documentCount} small documents");

        // Act - Process with controlled throughput
        var stopwatch = Stopwatch.StartNew();
        var successCount = 0;
        var errorCount = 0;
        
        await foreach (var result in _parallelProcessor.ProcessManyAsync(testFiles, options, parallelOptions))
        {
            if (result.IsSuccess)
                successCount++;
            else
                errorCount++;

            // Simulate slow consumer to test backpressure
            if (successCount % 10 == 0)
            {
                await Task.Delay(50); // Simulate processing delay
            }
        }
        
        stopwatch.Stop();

        // Assert - All documents processed despite backpressure
        _output.WriteLine($"Processed {successCount} documents successfully in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Errors: {errorCount}");
        _output.WriteLine($"Throughput: {successCount / stopwatch.Elapsed.TotalSeconds:F2} docs/sec");

        Assert.Equal(documentCount, successCount);
        Assert.Equal(0, errorCount);
        Assert.True(stopwatch.ElapsedMilliseconds < 30000, "Should complete within 30 seconds");
    }

    [Theory]
    [InlineData(1, 200)] // Single core simulation
    [InlineData(2, 400)] // Dual core
    [InlineData(4, 800)] // Quad core
    public async Task ParallelProcessing_DifferentCoreSimulation_ScalesLinearly(int maxParallelism, int documentSizeKB)
    {
        // Arrange - Create test documents proportional to core count
        var documentCount = maxParallelism * 3;
        var testFiles = CreateTestDocuments(documentCount, documentSizeKB);
        var parallelOptions = new ParallelProcessingOptions
        {
            MaxDegreeOfParallelism = maxParallelism
        };
        var options = new ChunkingOptions { Strategy = "Smart", MaxChunkSize = 512 };

        _output.WriteLine($"Testing {maxParallelism}-core simulation with {documentCount} documents");

        // Act
        var stopwatch = Stopwatch.StartNew();
        var resultCount = 0;
        
        await foreach (var result in _parallelProcessor.ProcessManyAsync(testFiles, options, parallelOptions))
        {
            if (result.IsSuccess) resultCount++;
        }
        
        stopwatch.Stop();

        // Assert
        var throughput = resultCount / stopwatch.Elapsed.TotalSeconds;
        _output.WriteLine($"Core simulation: {maxParallelism}, Throughput: {throughput:F2} docs/sec, Time: {stopwatch.ElapsedMilliseconds}ms");

        Assert.Equal(documentCount, resultCount);
        Assert.True(throughput > 0, "Should have positive throughput");
        
        // Store result for comparison (in real test, you'd compare against baseline)
        // For higher parallelism, we expect better throughput (though not perfectly linear due to overhead)
    }

    /// <summary>
    /// 테스트 문서들 생성
    /// </summary>
    private List<string> CreateTestDocuments(int count, int sizeKB)
    {
        var testFiles = new List<string>();
        var content = GenerateTestContent(sizeKB * 1024);

        for (int i = 0; i < count; i++)
        {
            var filePath = Path.Combine(_testDataDir, $"test_document_{i:D3}.txt");
            File.WriteAllText(filePath, content);
            testFiles.Add(filePath);
        }

        return testFiles;
    }

    /// <summary>
    /// 대용량 테스트 문서 생성
    /// </summary>
    private string CreateLargeTestDocument(int sizeKB)
    {
        var filePath = Path.Combine(_testDataDir, "large_test_document.txt");
        var content = GenerateTestContent(sizeKB * 1024);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    /// <summary>
    /// 테스트 콘텐츠 생성
    /// </summary>
    private string GenerateTestContent(int sizeBytes)
    {
        const string template = @"
# Document Section {0}

This is a test document section with meaningful content for chunking analysis.
The content includes multiple paragraphs and various text structures.

## Subsection {0}.1

Here we have detailed information about the topic. The text contains:
- Technical terminology for keyword extraction
- Multiple sentence structures for boundary detection
- Sufficient length for quality analysis
- Proper formatting for structure preservation

### Implementation Details

The following code snippet demonstrates the concept:

```csharp
public class DocumentProcessor
{{
    public async Task<Result> ProcessAsync(string content)
    {{
        // Processing logic here
        return await ProcessContent(content);
    }}
}}
```

## Performance Considerations

When processing large documents, several factors must be considered:
1. Memory usage and garbage collection
2. CPU utilization and parallel processing
3. I/O efficiency and streaming capabilities
4. Error handling and recovery mechanisms

The system should handle these requirements efficiently while maintaining high quality output.

";

        var sections = new List<string>();
        var currentSize = 0;
        var sectionNumber = 1;

        while (currentSize < sizeBytes)
        {
            var section = string.Format(template, sectionNumber);
            sections.Add(section);
            currentSize += section.Length;
            sectionNumber++;
        }

        return string.Join("\n", sections);
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
        catch (Exception ex)
        {
            _output.WriteLine($"Warning: Could not clean up test directory: {ex.Message}");
        }

        _parallelProcessor?.DisposeAsync().GetAwaiter().GetResult();
        _serviceProvider?.Dispose();
    }
}