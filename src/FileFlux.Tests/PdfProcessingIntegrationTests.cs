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
/// PDF 파일 처리 통합 테스트 - 실제 파일 처리 및 결과 저장
/// </summary>
public class PdfProcessingIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<ProgressiveDocumentProcessor> _logger;
    private readonly string _testOutputDirectory;
    private readonly UserFriendlyLogger _userLogger;
    private readonly TestResultsStorage _resultsStorage;

    public PdfProcessingIntegrationTests(ITestOutputHelper output)
    {
        _output = output;

        // 테스트용 로거 설정
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<ProgressiveDocumentProcessor>();

        // 테스트 출력 디렉토리 설정
        _testOutputDirectory = @"D:\data\FileFlux\test\test-a";

        // 사용자 친화적 로거 초기화
        _userLogger = new UserFriendlyLogger(Path.Combine(_testOutputDirectory, "logs"));

        // 결과 저장소 초기화
        _resultsStorage = new TestResultsStorage(_testOutputDirectory);

        _output.WriteLine($"Test output directory: {_testOutputDirectory}");
    }

    [Fact]
    public async Task ProcessLargePdfFile_ShouldHandleAndSaveResults_Successfully()
    {
        // Arrange
        var pdfFilePath = @"D:\data\FileFlux\test\test-a\oai_gpt-oss_model_card.pdf";

        if (!File.Exists(pdfFilePath))
        {
            _output.WriteLine($"PDF file not found at: {pdfFilePath}");
            _userLogger.LogError($"테스트 파일을 찾을 수 없습니다: {pdfFilePath}");
            Assert.Fail($"Test PDF file not found: {pdfFilePath}");
            return;
        }

        var fileInfo = new FileInfo(pdfFilePath);
        _userLogger.LogProcessingStart(fileInfo.Name, fileInfo.Length);

        var stopwatch = Stopwatch.StartNew();

        // Setup - 실제 PDF Reader 사용
        var readerFactory = new DocumentReaderFactory();
        readerFactory.RegisterReader(new Infrastructure.Readers.PdfDocumentReader());

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
            _userLogger.LogStage("시작", "대용량 PDF 처리를 시작합니다");

            var chunkingOptions = new ChunkingOptions
            {
                Strategy = "Intelligent",
                MaxChunkSize = 400,  // 임베딩 모델 최적화 (256 토큰 목표)
                OverlapSize = 60,    // 15% overlap 확보
                PreserveStructure = true
            };

            var parsingOptions = new DocumentParsingOptions
            {
                UseLlm = false,  // LLM 없이 테스트
                StructuringLevel = StructuringLevel.Medium
            };

            // Act - 진행률 추적과 함께 처리
            await foreach (var result in processor.ProcessWithProgressAsync(
                pdfFilePath,
                chunkingOptions,
                parsingOptions,
                CancellationToken.None))
            {
                progressUpdates.Add(result.Progress);

                var stage = result.Progress.Stage.ToString();
                var message = result.Progress.Message ?? "";
                var progress = result.Progress.OverallProgress * 100;

                _userLogger.LogStage(stage, message, progress);
                _output.WriteLine($"Progress: {stage} - {progress:F1}% - {message}");

                if (result.IsSuccess && result.Result != null)
                {
                    finalResult = result.Result;
                }

                // 단계별 결과 저장
                if (result.Progress.Stage == ProcessingStage.Extracting && result.RawContent != null && rawContent == null)
                {
                    rawContent = result.RawContent;
                    await _resultsStorage.SaveExtractionResultAsync(fileInfo.Name, rawContent);
                    _userLogger.LogStage("추출", "원시 문서 내용 저장 완료");
                }
                else if (result.Progress.Stage == ProcessingStage.Parsing && result.ParsedContent != null && parsedContent == null)
                {
                    parsedContent = result.ParsedContent;
                    await _resultsStorage.SaveParsingResultAsync(fileInfo.Name, parsedContent);
                    _userLogger.LogStage("파싱", "구조화된 문서 내용 저장 완료");
                }
            }

            stopwatch.Stop();

            // Assert & Save Results
            if (finalResult != null && finalResult.Length > 0)
            {
                // PDF 처리 성공한 경우
                Assert.NotNull(finalResult);
                Assert.True(finalResult.Length > 0);
                Assert.True(progressUpdates.Count > 0);
            }
            else
            {
                // PDF 처리 실패 - 실제 오류로 처리
                _userLogger.LogError("PDF 파일 처리에 실패했습니다. 예상치 못한 오류가 발생했습니다.");
                Assert.Fail($"PDF processing failed. No chunks were generated. Progress updates: {progressUpdates.Count}");
            }

            // 최종 결과 저장
            await _resultsStorage.SaveChunkingResultsAsync(fileInfo.Name, finalResult, chunkingOptions);

            // 통계 정보 로깅
            var statistics = new
            {
                FileName = fileInfo.Name,
                FileSize = FormatFileSize(fileInfo.Length),
                ProcessingTime = FormatDuration(stopwatch.Elapsed),
                TotalChunks = finalResult.Length,
                AverageChunkSize = Math.Round(finalResult.Average(c => c.Content.Length), 1),
                MinChunkSize = finalResult.Min(c => c.Content.Length),
                MaxChunkSize = finalResult.Max(c => c.Content.Length),
                TotalCharacters = finalResult.Sum(c => c.Content.Length),
                ChunkingStrategy = chunkingOptions.Strategy,
                MaxConfiguredChunkSize = chunkingOptions.MaxChunkSize,
                OverlapSize = chunkingOptions.OverlapSize
            };

            _userLogger.LogStatistics(statistics);
            _userLogger.LogProcessingComplete(fileInfo.Name, finalResult.Length, stopwatch.Elapsed);

            // 성공 로그
            _userLogger.LogSuccess($"PDF 처리 성공: {finalResult.Length}개 청크 생성");

            // 테스트 출력
            _output.WriteLine($"Final result: {finalResult.Length} chunks created");
            _output.WriteLine($"Processing time: {stopwatch.Elapsed.TotalSeconds:F1} seconds");
            _output.WriteLine($"Average chunk size: {statistics.AverageChunkSize} characters");
            _output.WriteLine($"Results saved to: {_testOutputDirectory}");

            // 청크 내용 미리보기 (처음 3개)
            for (int i = 0; i < Math.Min(3, finalResult.Length); i++)
            {
                var chunk = finalResult[i];
                var preview = chunk.Content.Length > 100
                    ? chunk.Content.Substring(0, 100) + "..."
                    : chunk.Content;
                _output.WriteLine($"Chunk {i + 1} ({chunk.Content.Length} chars): {preview}");
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _userLogger.LogError($"PDF 처리 중 오류 발생: {ex.Message}", ex);
            _output.WriteLine($"Error during processing: {ex.Message}");

            // PDF 처리 실패 - 반드시 테스트 실패로 처리
            _output.WriteLine($"Processing failed after {stopwatch.Elapsed.TotalSeconds:F1} seconds");
            _userLogger.LogError($"PDF 파일 처리 실패: {ex.Message}", ex);

            // 실제 오류로 테스트 실패시킴
            throw;
        }
    }


    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes >= 1)
        {
            return $"{duration.TotalMinutes:F1}분";
        }
        else
        {
            return $"{duration.TotalSeconds:F1}초";
        }
    }

    public void Dispose()
    {
        _userLogger?.Dispose();

        _output.WriteLine($"Test completed. Results saved to: {_testOutputDirectory}");
        _output.WriteLine("Check the following directories for detailed results:");
        _output.WriteLine($"- Logs: {Path.Combine(_testOutputDirectory, "logs")}");
        _output.WriteLine($"- Extraction: {Path.Combine(_testOutputDirectory, "extraction-results")}");
        _output.WriteLine($"- Parsing: {Path.Combine(_testOutputDirectory, "parsing-results")}");
        _output.WriteLine($"- Chunking: {Path.Combine(_testOutputDirectory, "chunking-results")}");
    }
}