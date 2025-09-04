using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FileFlux.Tests.Mocks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace FileFlux.Tests;

/// <summary>
/// LLM 최적화 메타데이터 생성 테스트
/// </summary>
public class LlmMetadataTests
{
    private readonly ITestOutputHelper _output;

    public LlmMetadataTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task IntelligentChunking_ShouldGenerateLlmOptimizedMetadata()
    {
        // Arrange
        var readerFactory = new DocumentReaderFactory();
        readerFactory.RegisterReader(new Infrastructure.Readers.TextDocumentReader());

        var mockTextCompletionService = new MockTextCompletionService();
        var parserFactory = new DocumentParserFactory(mockTextCompletionService);
        var chunkingFactory = new ChunkingStrategyFactory();
        chunkingFactory.RegisterStrategy(() => new Infrastructure.Strategies.IntelligentChunkingStrategy());

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ProgressiveDocumentProcessor>();
        var processor = new ProgressiveDocumentProcessor(readerFactory, parserFactory, chunkingFactory, logger);

        var chunkingOptions = new ChunkingOptions
        {
            Strategy = "Intelligent",
            MaxChunkSize = 400
        };

        var testFile = @"D:\data\FileFlux\test\test-b\test.md";
        DocumentChunk[]? chunks = null;

        // Act
        await foreach (var result in processor.ProcessWithProgressAsync(
            testFile, chunkingOptions, new DocumentParsingOptions(), CancellationToken.None))
        {
            if (result.IsSuccess && result.Result != null)
            {
                chunks = result.Result;
            }
        }

        // Assert
        Assert.NotNull(chunks);
        Assert.True(chunks!.Length > 0);

        var firstChunk = chunks[0];

        // LLM 최적화 메타데이터 검증
        _output.WriteLine("=== LLM 최적화 메타데이터 ===");
        _output.WriteLine($"ContextualHeader: {firstChunk.ContextualHeader}");
        _output.WriteLine($"DocumentDomain: {firstChunk.DocumentDomain}");
        _output.WriteLine($"TechnicalKeywords: [{string.Join(", ", firstChunk.TechnicalKeywords)}]");
        _output.WriteLine($"StructuralRole: {firstChunk.StructuralRole}");
        _output.WriteLine("");

        // 기본 검증
        Assert.NotNull(firstChunk.DocumentDomain);
        Assert.NotEqual("General", firstChunk.DocumentDomain); // 기술 문서이므로 General이 아니어야 함
        Assert.NotNull(firstChunk.StructuralRole);

        // 기술 문서 특성 검증
        Assert.True(firstChunk.TechnicalKeywords.Count > 0, "기술 키워드가 탐지되어야 함");

        // ContextualHeader 존재 검증
        Assert.NotNull(firstChunk.ContextualHeader);
        Assert.Contains("Tech:", firstChunk.ContextualHeader); // 기술 키워드가 포함되어야 함

        _output.WriteLine($"✅ 테스트 통과 - 총 {chunks.Length}개 청크에 LLM 메타데이터 생성됨");
    }
}