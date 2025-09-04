using FileFlux.Core;
using FileFlux.Infrastructure.Strategies;
using FileFlux.Domain;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FileFlux.Tests.Manual;

/// <summary>
/// 청킹 이슈 디버깅을 위한 간단 테스트
/// </summary>
public class ChunkingDebugTest
{
    private readonly ILogger<ChunkingDebugTest> _logger;
    private const string TestDataPath = @"D:\data\FileFlux\test";

    public ChunkingDebugTest()
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<ChunkingDebugTest>();
    }

    [Fact]
    public async Task TestFixedSizeChunkingDirectly()
    {
        // Arrange
        var strategy = new FixedSizeChunkingStrategy();
        var testFile = Path.Combine(TestDataPath, "test-markdown", "test.md");
        
        if (!File.Exists(testFile))
        {
            _logger.LogWarning("Test file not found: {TestFile}. Skipping test.", testFile);
            return;
        }

        // 텍스트 직접 읽기
        var text = await File.ReadAllTextAsync(testFile);
        _logger.LogInformation("📄 Read text: {Length} characters", text.Length);
        
        var documentContent = new DocumentContent
        {
            Text = text,
            Metadata = new DocumentMetadata 
            { 
                FileName = "test.md", 
                FileType = "Markdown",
                PageCount = 1,
                ProcessedAt = DateTime.UtcNow
            }
        };

        var options = new ChunkingOptions
        {
            Strategy = ChunkingStrategies.FixedSize,
            MaxChunkSize = 500,
            OverlapSize = 50
        };

        _logger.LogInformation("🧪 Starting direct chunking test");
        _logger.LogInformation("Strategy: {Strategy}, MaxSize: {MaxSize}, Overlap: {Overlap}", 
            options.Strategy, options.MaxChunkSize, options.OverlapSize);

        // Act
        var chunks = await strategy.ChunkAsync(documentContent, options);
        var chunkList = chunks.ToList();

        // Assert
        _logger.LogInformation("🎉 Final result: {ChunkCount} chunks generated", chunkList.Count);
        
        Assert.True(chunkList.Count > 0, "Should generate at least one chunk");
        
        foreach (var chunk in chunkList.Take(3)) // Show first 3 chunks
        {
            _logger.LogInformation("📋 Chunk {Index}: {Preview}...", 
                chunk.ChunkIndex, 
                chunk.Content.Length > 100 ? string.Concat(chunk.Content.AsSpan(0, 100), "...") : chunk.Content);
        }
    }

    [Fact]
    public async Task TestParagraphChunkingDirectly()
    {
        // Arrange
        var strategy = new ParagraphChunkingStrategy();
        var testFile = Path.Combine(TestDataPath, "test-markdown", "test.md");
        
        if (!File.Exists(testFile))
        {
            _logger.LogWarning("Test file not found: {TestFile}. Skipping test.", testFile);
            return;
        }

        // 텍스트 직접 읽기
        var text = await File.ReadAllTextAsync(testFile);
        _logger.LogInformation("📄 Read text: {Length} characters", text.Length);
        
        var documentContent = new DocumentContent
        {
            Text = text,
            Metadata = new DocumentMetadata 
            { 
                FileName = "test.md", 
                FileType = "Markdown",
                PageCount = 1,
                ProcessedAt = DateTime.UtcNow
            }
        };

        var options = new ChunkingOptions
        {
            Strategy = ChunkingStrategies.Paragraph,
            MaxChunkSize = 1000,
            OverlapSize = 100
        };

        _logger.LogInformation("🧪 Starting paragraph chunking test");

        // Act
        var chunks = await strategy.ChunkAsync(documentContent, options);
        var chunkList = chunks.ToList();

        // Assert
        _logger.LogInformation("🎉 Final result: {ChunkCount} chunks generated", chunkList.Count);
        
        Assert.True(chunkList.Count > 0, "Should generate at least one chunk");
        
        foreach (var chunk in chunkList.Take(3)) // Show first 3 chunks
        {
            _logger.LogInformation("📋 Chunk {Index}: {Preview}...", 
                chunk.ChunkIndex, 
                chunk.Content.Length > 100 ? string.Concat(chunk.Content.AsSpan(0, 100), "...") : chunk.Content);
        }
    }
}