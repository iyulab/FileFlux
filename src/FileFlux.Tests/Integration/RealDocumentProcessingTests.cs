using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FileFlux.Tests.Mocks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace FileFlux.Tests.Integration;

/// <summary>
/// 실제 문서 파일을 이용한 통합 테스트
/// </summary>
public class RealDocumentProcessingTests
{
    private readonly ITestOutputHelper _output;
    private readonly ProgressiveDocumentProcessor _processor;

    public RealDocumentProcessingTests(ITestOutputHelper output)
    {
        _output = output;

        var readerFactory = new DocumentReaderFactory();
        readerFactory.RegisterReader(new Infrastructure.Readers.TextDocumentReader());

        var mockTextCompletionService = new MockTextCompletionService();
        var parserFactory = new DocumentParserFactory(mockTextCompletionService);
        var chunkingFactory = new ChunkingStrategyFactory();
        chunkingFactory.RegisterStrategy(() => new Infrastructure.Strategies.IntelligentChunkingStrategy());

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ProgressiveDocumentProcessor>();
        _processor = new ProgressiveDocumentProcessor(readerFactory, parserFactory, chunkingFactory, logger);
    }

    [Fact]
    public async Task ProcessRealTechnicalDocument_ShouldGenerateOptimizedChunks()
    {
        // Arrange
        var testFilePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "test", "test-b", "test.md");
        testFilePath = Path.GetFullPath(testFilePath);

        // 파일 존재 확인
        if (!File.Exists(testFilePath))
        {
            _output.WriteLine($"테스트 파일이 존재하지 않습니다: {testFilePath}");
            return; // 파일이 없으면 테스트 건너뜀
        }

        var chunkingOptions = new ChunkingOptions
        {
            Strategy = "Intelligent",
            MaxChunkSize = 1024,  // 적당한 크기로 설정
            OverlapSize = 128
        };

        DocumentChunk[]? chunks = null;

        // Act
        await foreach (var result in _processor.ProcessWithProgressAsync(
            testFilePath, chunkingOptions, new DocumentParsingOptions(), CancellationToken.None))
        {
            if (result.IsSuccess && result.Result != null)
            {
                chunks = result.Result;
            }
        }

        // Assert
        Assert.NotNull(chunks);
        Assert.NotEmpty(chunks);

        _output.WriteLine($"총 {chunks.Length}개의 청크가 생성되었습니다");

        // LLM 최적화 메타데이터 검증
        var chunksWithOptimization = chunks.Where(c => !string.IsNullOrEmpty(c.ContextualHeader)).ToArray();
        Assert.NotEmpty(chunksWithOptimization);

        // 기술 문서로 분류되었는지 확인
        var technicalChunks = chunks.Where(c => c.DocumentDomain == "Technical").ToArray();
        Assert.NotEmpty(technicalChunks);

        _output.WriteLine($"Technical 도메인으로 분류된 청크: {technicalChunks.Length}개");

        // 기술 키워드 검출 확인
        var chunksWithKeywords = chunks.Where(c => c.TechnicalKeywords.Count != 0).ToArray();
        Assert.NotEmpty(chunksWithKeywords);

        _output.WriteLine($"기술 키워드가 검출된 청크: {chunksWithKeywords.Length}개");

        // 샘플 청크 정보 출력
        for (int i = 0; i < Math.Min(5, chunks.Length); i++)
        {
            var chunk = chunks[i];
            _output.WriteLine($"\n=== 청크 {i + 1} ===");
            _output.WriteLine($"도메인: {chunk.DocumentDomain}");
            _output.WriteLine($"ContextualHeader: {chunk.ContextualHeader ?? "null"}");
            _output.WriteLine($"기술키워드: [{string.Join(", ", chunk.TechnicalKeywords)}]");
            _output.WriteLine($"내용 (100자): {chunk.Content.Substring(0, Math.Min(100, chunk.Content.Length))}...");
        }
    }
}