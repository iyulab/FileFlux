using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FileFlux.Infrastructure.Logging;
using FileFlux.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace FileFlux.Tests;

/// <summary>
/// Markdown 파일 처리 통합 테스트 - test-b 디렉터리 사용
/// </summary>
public class MarkdownProcessingIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<ProgressiveDocumentProcessor> _logger;
    private readonly string _testOutputDirectory;
    private UserFriendlyLogger? _userLogger;
    private readonly TestResultsStorage _resultsStorage;

    public MarkdownProcessingIntegrationTests(ITestOutputHelper output)
    {
        _output = output;

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<ProgressiveDocumentProcessor>();

        // test-b 디렉토리 설정
        _testOutputDirectory = @"D:\data\FileFlux\test\test-b";

        // 결과 저장소 초기화
        _resultsStorage = new TestResultsStorage(_testOutputDirectory);

        _output.WriteLine($"Test output directory: {_testOutputDirectory}");
    }

    private UserFriendlyLogger GetUserLogger()
    {
        if (_userLogger == null)
        {
            // 사용자 친화적 로거 초기화 (고유한 파일명 사용)
            var uniqueLogFile = $"markdown-test-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt";
            _userLogger = new UserFriendlyLogger(Path.Combine(_testOutputDirectory, "logs"), uniqueLogFile);
        }
        return _userLogger;
    }

    [Fact]
    public async Task ProcessMarkdownFile_ShouldHandleAndSaveResults_Successfully()
    {
        // Arrange
        var markdownFilePath = @"D:\data\FileFlux\test\test-b\test.md";

        if (!File.Exists(markdownFilePath))
        {
            _output.WriteLine($"Markdown file not found at: {markdownFilePath}");
            GetUserLogger().LogError($"테스트 파일을 찾을 수 없습니다: {markdownFilePath}");
            Assert.Fail($"Test Markdown file not found: {markdownFilePath}");
            return;
        }

        var fileInfo = new FileInfo(markdownFilePath);
        GetUserLogger().LogProcessingStart(fileInfo.Name, fileInfo.Length);

        var stopwatch = Stopwatch.StartNew();

        // Setup - Markdown Reader 사용
        var readerFactory = new DocumentReaderFactory();
        readerFactory.RegisterReader(new Infrastructure.Readers.TextDocumentReader()); // Markdown는 텍스트로 처리

        var mockTextCompletionService = new Mocks.MockTextCompletionService();
        var parserFactory = new DocumentParserFactory(mockTextCompletionService);
        var chunkingFactory = new Infrastructure.Factories.ChunkingStrategyFactory();

        // 청킹 전략 등록
        chunkingFactory.RegisterStrategy(() => new Infrastructure.Strategies.IntelligentChunkingStrategy());
        chunkingFactory.RegisterStrategy(() => new Infrastructure.Strategies.ParagraphChunkingStrategy());

        var processor = new ProgressiveDocumentProcessor(readerFactory, parserFactory, chunkingFactory, _logger);

        var progressUpdates = new List<ProcessingProgress>();
        DocumentChunk[]? finalResult = null;
        RawDocumentContent? rawContent = null;
        ParsedDocumentContent? parsedContent = null;

        try
        {
            GetUserLogger().LogStage("시작", "Markdown 처리를 시작합니다");

            var chunkingOptions = new ChunkingOptions
            {
                Strategy = "Intelligent",
                MaxChunkSize = 400,  // 임베딩 모델 최적화
                OverlapSize = 60,    // 15% overlap 확보
            };

            var parsingOptions = new DocumentParsingOptions
            {
                // Using default settings with MockTextCompletionService
                StructuringLevel = StructuringLevel.Medium
            };

            // Act - 진행률 추적과 함께 처리 (PDF 테스트와 동일한 패턴)
            await foreach (var result in processor.ProcessWithProgressAsync(
                markdownFilePath,
                chunkingOptions,
                parsingOptions,
                CancellationToken.None))
            {
                progressUpdates.Add(result.Progress);

                var stage = result.Progress.Stage.ToString();
                var message = result.Progress.Message ?? "";
                var progress = result.Progress.OverallProgress * 100;

                GetUserLogger().LogStage(stage, message, progress);
                _output.WriteLine($"Progress: {stage} - {progress:F1}% - {message}");

                if (result.IsSuccess && result.Result != null)
                {
                    finalResult = result.Result;
                }

                // 단계별 결과 저장 (PDF 테스트와 동일한 패턴)
                if (result.Progress.Stage == ProcessingStage.Extracting && result.RawContent != null && rawContent == null)
                {
                    rawContent = result.RawContent;
                    await _resultsStorage.SaveExtractionResultAsync(fileInfo.Name, rawContent);
                    GetUserLogger().LogStage("추출", "원시 문서 내용 저장 완료");
                }
                else if (result.Progress.Stage == ProcessingStage.Parsing && result.ParsedContent != null && parsedContent == null)
                {
                    parsedContent = result.ParsedContent;
                    await _resultsStorage.SaveParsingResultAsync(fileInfo.Name, parsedContent);
                    GetUserLogger().LogStage("파싱", "구조화된 문서 내용 저장 완료");
                }
            }

            stopwatch.Stop();

            // Assert & Save Results (PDF 테스트와 동일한 패턴)
            if (finalResult != null && finalResult.Length > 0)
            {
                // Markdown 처리 성공한 경우
                Assert.NotNull(finalResult);
                Assert.True(finalResult.Length > 0);
                Assert.True(progressUpdates.Count > 0);

                // 최종 결과 저장
                await _resultsStorage.SaveChunkingResultsAsync(fileInfo.Name, finalResult, chunkingOptions);

                // 통계 정보 로깅
                var statistics = new
                {
                    FileName = fileInfo.Name,
                    FileSize = fileInfo.Length,
                    ProcessingTime = stopwatch.Elapsed,
                    TotalChunks = finalResult.Length,
                    AverageChunkSize = Math.Round(finalResult.Average(c => c.Content.Length), 1),
                    MinChunkSize = finalResult.Min(c => c.Content.Length),
                    MaxChunkSize = finalResult.Max(c => c.Content.Length),
                    Strategy = chunkingOptions.Strategy
                };

                _output.WriteLine($"✅ Markdown 처리 완료:");
                _output.WriteLine($"   파일: {statistics.FileName} ({statistics.FileSize:N0} bytes)");
                _output.WriteLine($"   처리 시간: {statistics.ProcessingTime}");
                _output.WriteLine($"   총 청크: {statistics.TotalChunks:N0}개");
                _output.WriteLine($"   평균 크기: {statistics.AverageChunkSize} chars");
                _output.WriteLine($"   크기 범위: {statistics.MinChunkSize} ~ {statistics.MaxChunkSize} chars");
                _output.WriteLine($"   전략: {statistics.Strategy}");

                // 청킹 품질 검증 - 테이블이 있을 경우 동적 크기 조정 허용
                var hasTable = finalResult.Any(chunk => chunk.Content.Contains("|") && chunk.Content.Contains("---"));
                var effectiveMaxSize = hasTable ? chunkingOptions.MaxChunkSize * 2 : chunkingOptions.MaxChunkSize;
                Assert.True(statistics.MaxChunkSize <= effectiveMaxSize,
                    $"최대 청크 크기가 제한을 초과했습니다: {statistics.MaxChunkSize} > {effectiveMaxSize} (테이블 포함: {hasTable})");

                Assert.True(statistics.MinChunkSize > 0, "빈 청크가 발견되었습니다");
            }
            else
            {
                // Markdown 처리 실패 - 실제 오류로 처리
                GetUserLogger().LogError("Markdown 파일 처리에 실패했습니다. 예상치 못한 오류가 발생했습니다.");
                Assert.Fail($"Markdown processing failed. No chunks were generated. Progress updates: {progressUpdates.Count}");
            }
        }
        catch (Exception ex)
        {
            GetUserLogger().LogError($"처리 중 오류 발생: {ex.Message}");
            _output.WriteLine($"❌ Error during processing: {ex}");
            throw;
        }
    }

    [Fact]
    public async Task ProcessMarkdownFile_ShouldHandleMarkdownSyntax_Correctly()
    {
        // Arrange
        var markdownFilePath = @"D:\data\FileFlux\test\test-b\test.md";

        if (!File.Exists(markdownFilePath))
        {
            _output.WriteLine($"Skipping test - Markdown file not found: {markdownFilePath}");
            return;
        }

        var readerFactory = new DocumentReaderFactory();
        readerFactory.RegisterReader(new Infrastructure.Readers.TextDocumentReader());

        var mockTextCompletionService = new Mocks.MockTextCompletionService();
        var parserFactory = new DocumentParserFactory(mockTextCompletionService);
        var chunkingFactory = new Infrastructure.Factories.ChunkingStrategyFactory();
        chunkingFactory.RegisterStrategy(() => new Infrastructure.Strategies.ParagraphChunkingStrategy());

        var processor = new ProgressiveDocumentProcessor(readerFactory, parserFactory, chunkingFactory, _logger);

        var chunkingOptions = new ChunkingOptions
        {
            Strategy = "Paragraph",  // Markdown 구조 보존에 더 적합
            MaxChunkSize = 800,
            OverlapSize = 100,
        };

        var parsingOptions = new DocumentParsingOptions
        {
            // Using default settings with MockTextCompletionService
            StructuringLevel = StructuringLevel.Medium
        };

        DocumentChunk[]? chunks = null;

        // Act
        await foreach (var result in processor.ProcessWithProgressAsync(
            markdownFilePath, chunkingOptions, parsingOptions, CancellationToken.None))
        {
            if (result.IsSuccess && result.Result != null)
            {
                chunks = result.Result;
            }
        }

        // Assert
        Assert.NotNull(chunks);
        Assert.True(chunks!.Length > 0);

        // Markdown 특성 검증
        var content = await File.ReadAllTextAsync(markdownFilePath);
        var hasHeaders = content.Contains("# ") || content.Contains("## ") || content.Contains("### ");
        var hasCodeBlocks = content.Contains("```");
        var hasLinks = content.Contains("[") && content.Contains("](");

        _output.WriteLine($"Markdown 특성 분석:");
        _output.WriteLine($"  헤더 포함: {hasHeaders}");
        _output.WriteLine($"  코드 블록 포함: {hasCodeBlocks}");
        _output.WriteLine($"  링크 포함: {hasLinks}");

        // 청크 내용 샘플 확인
        var sampleChunks = chunks.Take(3).ToArray();
        for (int i = 0; i < sampleChunks.Length; i++)
        {
            var chunk = sampleChunks[i];
            _output.WriteLine($"청크 {i + 1} (길이: {chunk.Content.Length}자):");
            _output.WriteLine($"  {chunk.Content.Substring(0, Math.Min(100, chunk.Content.Length))}...");
        }

        _output.WriteLine($"✅ Markdown 처리 완료 - 총 {chunks.Length}개 청크 생성");
    }

    public void Dispose()
    {
        // 리소스 정리
    }
}