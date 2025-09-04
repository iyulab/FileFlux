using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FileFlux.Infrastructure.Performance;
using Microsoft.Extensions.Logging;
using System.Text;

namespace FileFlux.SampleApp;

/// <summary>
/// 메모리 벤치마킹 실행기 - 콘솔에서 직접 실행 가능한 간단한 성능 측정 도구
/// </summary>
public class MemoryBenchmarkRunner
{
    private readonly ProgressiveDocumentProcessor _processor;
    private readonly ILogger<MemoryBenchmarkRunner> _logger;
    
    public MemoryBenchmarkRunner(ILogger<MemoryBenchmarkRunner> logger)
    {
        _logger = logger;
        
        // 프로세서 설정
        var readerFactory = new DocumentReaderFactory();
        readerFactory.RegisterReader(new Infrastructure.Readers.TextDocumentReader());

        var mockTextCompletionService = new SimpleMockTextCompletionService();
        var parserFactory = new DocumentParserFactory(mockTextCompletionService);
        var chunkingFactory = new ChunkingStrategyFactory();
        chunkingFactory.RegisterStrategy(() => new Infrastructure.Strategies.IntelligentChunkingStrategy());

        var processorLogger = new LoggerFactory().CreateLogger<ProgressiveDocumentProcessor>();
        _processor = new ProgressiveDocumentProcessor(readerFactory, parserFactory, chunkingFactory, processorLogger);
    }

    /// <summary>
    /// 빠른 메모리 벤치마킹 실행
    /// </summary>
    public async Task<MemoryBenchmarkResult> RunQuickBenchmarkAsync()
    {
        _logger.LogInformation("=== FileFlux 메모리 벤치마킹 시작 ===");
        
        // 1MB 테스트 파일 생성
        var testFilePath = await CreateTestFileAsync(1024 * 1024, "quick_benchmark_1mb.txt");
        var fileInfo = new FileInfo(testFilePath);
        
        using var profiler = new MemoryProfiler();
        
        profiler.TakeSnapshot("start");
        
        var options = new ChunkingOptions
        {
            Strategy = "Intelligent",
            MaxChunkSize = 1024,
            OverlapSize = 128
        };

        var parsingOptions = new DocumentParsingOptions
        {
            UseAdvancedParsing = false, // 빠른 테스트를 위해 기본 파싱 사용
            StructuringLevel = StructuringLevel.Low
        };

        DocumentChunk[]? finalResult = null;
        var chunkCount = 0;

        profiler.TakeSnapshot("before_processing");

        try
        {
            await foreach (var result in _processor.ProcessWithProgressAsync(testFilePath, options, parsingOptions, CancellationToken.None))
            {
                if (result.IsSuccess && result.Result != null)
                {
                    finalResult = result.Result;
                    chunkCount = finalResult.Length;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "처리 중 오류 발생");
            throw;
        }

        profiler.TakeSnapshot("after_processing");

        // 메모리 분석
        var analysis = profiler.AnalyzeFileProcessing(
            fileInfo.Length, 
            "before_processing", 
            "after_processing"
        );

        // 결과 출력
        _logger.LogInformation("=== 벤치마킹 결과 ===");
        _logger.LogInformation("파일 크기: {FileSizeMB:F2} MB", analysis.FileSizeMB);
        _logger.LogInformation("처리 시간: {ProcessingTimeMs:N0} ms", analysis.ProcessingTimeMs);
        _logger.LogInformation("처리 속도: {Speed:F2} MB/초", analysis.FileSizeMB / (analysis.ProcessingTimeMs / 1000.0));
        _logger.LogInformation("생성된 청크 수: {ChunkCount:N0}", chunkCount);
        _logger.LogInformation("메모리 사용량: {MemoryMB:F2} MB", analysis.MemoryUsageDelta.WorkingSetMB);
        _logger.LogInformation("메모리/파일 비율: {Ratio:F2}", analysis.MemoryToFileRatio);
        _logger.LogInformation("성능 등급: {Grade}", analysis.PerformanceGrade);
        _logger.LogInformation("효율성: {Efficient}", analysis.IsEfficient ? "통과" : "실패");
        
        // 정리
        if (File.Exists(testFilePath))
        {
            File.Delete(testFilePath);
        }

        return new MemoryBenchmarkResult
        {
            FileSizeMB = analysis.FileSizeMB,
            ProcessingTimeMs = analysis.ProcessingTimeMs,
            ChunkCount = chunkCount,
            MemoryUsageMB = analysis.MemoryUsageDelta.WorkingSetMB,
            MemoryToFileRatio = analysis.MemoryToFileRatio,
            PerformanceGrade = analysis.PerformanceGrade,
            IsEfficient = analysis.IsEfficient
        };
    }

    private async Task<string> CreateTestFileAsync(int sizeInBytes, string fileName)
    {
        var tempDir = Path.GetTempPath();
        var filePath = Path.Combine(tempDir, fileName);
        
        var sampleParagraph = """
            FileFlux는 다양한 문서 형식을 파싱하고 LLM을 활용하여 일관된 구조로 재구성한 텍스트 청크를 생성하는 .NET SDK입니다. 
            이 도구는 RAG(Retrieval-Augmented Generation) 시스템을 위한 전처리 작업에 특화되어 있으며, 
            고품질의 구조화된 청크를 통해 검색 정확도와 생성 품질을 크게 향상시킵니다.
            """;

        var paragraphBytes = Encoding.UTF8.GetBytes(sampleParagraph);
        var repeatCount = sizeInBytes / paragraphBytes.Length + 1;

        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        for (int i = 0; i < repeatCount; i++)
        {
            await writer.WriteAsync($"=== 섹션 {i + 1:N0} ===\n");
            await writer.WriteAsync(sampleParagraph);
            await writer.WriteAsync("\n\n");
            
            if (writer.BaseStream.Length >= sizeInBytes)
                break;
        }

        await writer.FlushAsync();
        
        var actualSize = new FileInfo(filePath).Length;
        _logger.LogInformation("테스트 파일 생성: {FileName} ({Size:N0} bytes)", fileName, actualSize);
        
        return filePath;
    }
}

/// <summary>
/// 메모리 벤치마킹 결과
/// </summary>
public record MemoryBenchmarkResult
{
    public double FileSizeMB { get; init; }
    public long ProcessingTimeMs { get; init; }
    public int ChunkCount { get; init; }
    public double MemoryUsageMB { get; init; }
    public double MemoryToFileRatio { get; init; }
    public string PerformanceGrade { get; init; } = string.Empty;
    public bool IsEfficient { get; init; }
}

/// <summary>
/// 간단한 Mock 텍스트 완성 서비스
/// </summary>
internal class SimpleMockTextCompletionService : ITextCompletionService
{
    public TextCompletionServiceInfo ProviderInfo { get; } = new()
    {
        Name = "Mock Service",
        Type = TextCompletionProviderType.Custom,
        SupportedModels = new[] { "mock-model" },
        MaxContextLength = 4096
    };
    internal static readonly string[] result = new[] { "test" };

    public Task<StructureAnalysisResult> AnalyzeStructureAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new StructureAnalysisResult
        {
            DocumentType = documentType,
            Sections = new List<SectionInfo>(),
            Structure = new FileFlux.Core.DocumentStructure
            {
                Root = new SectionInfo { Type = SectionType.HEADING_L1, Title = "Root" },
                AllSections = new List<SectionInfo>()
            },
            Confidence = 0.8,
            TokensUsed = 100
        });
    }

    public Task<ContentSummary> SummarizeContentAsync(
        string prompt,
        int maxLength = 200,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ContentSummary
        {
            Summary = "Mock summary",
            Keywords = result,
            Confidence = 0.8,
            OriginalLength = prompt.Length,
            TokensUsed = 50
        });
    }

    public Task<MetadataExtractionResult> ExtractMetadataAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new MetadataExtractionResult
        {
            Keywords = result,
            Language = "en",
            Categories = new[] { "test" },
            Entities = new Dictionary<string, string[]>(),
            TechnicalMetadata = new Dictionary<string, string>(),
            Confidence = 0.9,
            TokensUsed = 75
        });
    }

    public Task<QualityAssessment> AssessQualityAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new QualityAssessment
        {
            ConfidenceScore = 0.85,
            CompletenessScore = 0.8,
            ConsistencyScore = 0.9,
            Recommendations = new List<QualityRecommendation>(),
            Explanation = "Mock quality assessment",
            TokensUsed = 60
        });
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"Mock response to: {prompt.Substring(0, Math.Min(50, prompt.Length))}...");
    }
}