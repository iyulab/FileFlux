using FileFlux;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FileFlux.Domain;
using FileFlux.Tests.Mocks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FileFlux.Tests.Manual;

/// <summary>
/// DocumentProcessor 전체 파이프라인 테스트 (Extract → Parse → Chunk)
/// </summary>
public class DocumentProcessorTest
{
    private readonly ILogger<DocumentProcessorTest> _logger;
    private const string TestDataPath = @"D:\data\FileFlux\test";

    public DocumentProcessorTest()
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<DocumentProcessorTest>();
    }

    [Fact]
    public async Task TestCompleteProcessingPipeline()
    {
        // Arrange: Mock services 생성
        var mockTextCompletion = new MockTextCompletionService();
        var readerFactory = new DocumentReaderFactory();
        var parserFactory = new DocumentParserFactory(mockTextCompletion);
        var chunkingFactory = CreateChunkingFactory();

        IDocumentProcessor processor = new DocumentProcessor(
            readerFactory,
            parserFactory,
            chunkingFactory);

        var testFile = Path.Combine(TestDataPath, "test-markdown", "test.md");
        
        if (!File.Exists(testFile))
        {
            _logger.LogWarning("Test file not found: {TestFile}. Skipping test.", testFile);
            return;
        }

        var options = new ChunkingOptions
        {
            Strategy = ChunkingStrategies.FixedSize,
            MaxChunkSize = 500,
            OverlapSize = 50
        };

        var parsingOptions = new DocumentParsingOptions
        {
            UseLlmParsing = false, // Using basic rule-based parsing
            StructuringLevel = StructuringLevel.Low
        };

        _logger.LogInformation("🧪 Complete pipeline test starting");
        _logger.LogInformation("📄 File: {TestFile}", testFile);
        _logger.LogInformation("📋 Strategy: {Strategy}", options.Strategy);

        // Act: 전체 파이프라인 실행 (새로운 ProcessAsync API 사용)
        var chunks = new List<DocumentChunk>();
        
        foreach (var chunk in await processor.ProcessAsync(testFile, options))
        {
            chunks.Add(chunk);
            _logger.LogInformation("✅ Added chunk #{Index}: {Length} chars", 
                chunk.Index, chunk.Content.Length);
        }
        
        var summaryMessage = $"🎉 Pipeline completed: {chunks.Count} chunks generated";
        _logger.LogInformation(summaryMessage);
        Console.WriteLine(summaryMessage);

        // Assert
        _logger.LogInformation("🎉 Final result: {ChunkCount} chunks generated", chunks.Count);

        // 청크 생성 검증
        Assert.True(chunks.Count > 0, $"Should generate at least one chunk, got {chunks.Count}");
        
        foreach (var chunk in chunks.Take(3))
        {
            Assert.NotNull(chunk.Content);
            Assert.True(chunk.Content.Length > 0);
            _logger.LogInformation("📋 Chunk {Index}: {Preview}...", 
                chunk.Index, 
                chunk.Content.Length > 100 ? string.Concat(chunk.Content.AsSpan(0, 100), "...") : chunk.Content);
        }

        _logger.LogInformation("✅ All assertions passed!");
    }

    private static ChunkingStrategyFactory CreateChunkingFactory()
    {
        var factory = new ChunkingStrategyFactory();
        
        // Register all chunking strategies manually (same as ServiceCollectionExtensions)
        factory.RegisterStrategy(() => new Infrastructure.Strategies.FixedSizeChunkingStrategy());
        factory.RegisterStrategy(() => new Infrastructure.Strategies.SemanticChunkingStrategy());
        factory.RegisterStrategy(() => new Infrastructure.Strategies.ParagraphChunkingStrategy());
        factory.RegisterStrategy(() => new Infrastructure.Strategies.IntelligentChunkingStrategy());
        
        return factory;
    }
}

