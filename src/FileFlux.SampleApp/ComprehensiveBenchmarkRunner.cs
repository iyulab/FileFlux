using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FileFlux.Infrastructure.Performance;
using FileFlux.Infrastructure.Readers;
using Microsoft.Extensions.Logging;
using System.Text;

namespace FileFlux.SampleApp;

/// <summary>
/// 실제 테스트 파일들을 대상으로 하는 종합적인 벤치마크 실행기
/// </summary>
public class ComprehensiveBenchmarkRunner
{
    private readonly ProgressiveDocumentProcessor _processor;
    private readonly ILogger<ComprehensiveBenchmarkRunner> _logger;
    
    public ComprehensiveBenchmarkRunner(ILogger<ComprehensiveBenchmarkRunner> logger)
    {
        _logger = logger;
        
        // 모든 Document Reader 등록
        var readerFactory = new DocumentReaderFactory();
        readerFactory.RegisterReader(new TextDocumentReader());
        readerFactory.RegisterReader(new MarkdownDocumentReader());
        readerFactory.RegisterReader(new PdfDocumentReader());
        readerFactory.RegisterReader(new WordDocumentReader());
        readerFactory.RegisterReader(new ExcelDocumentReader());
        readerFactory.RegisterReader(new PowerPointDocumentReader());
        readerFactory.RegisterReader(new HtmlDocumentReader());

        var mockTextCompletionService = new SimpleMockTextCompletionService();
        var parserFactory = new DocumentParserFactory(mockTextCompletionService);
        var chunkingFactory = new ChunkingStrategyFactory();
        chunkingFactory.RegisterStrategy(() => new Infrastructure.Strategies.IntelligentChunkingStrategy());

        var processorLogger = new LoggerFactory().CreateLogger<ProgressiveDocumentProcessor>();
        _processor = new ProgressiveDocumentProcessor(readerFactory, parserFactory, chunkingFactory, processorLogger);
    }

    /// <summary>
    /// 모든 테스트 파일에 대해 종합 벤치마크 실행
    /// </summary>
    public async Task<BenchmarkReport> RunComprehensiveBenchmarkAsync(string testDirectory = @"D:\data\FileFlux\test")
    {
        _logger.LogInformation("=== FileFlux 종합 벤치마크 시작 ===");
        
        var report = new BenchmarkReport();
        var testFiles = new List<TestFile>();
        
        // 테스트 파일 발견
        if (Directory.Exists(Path.Combine(testDirectory, "test-pdf")))
        {
            var pdfFiles = Directory.GetFiles(Path.Combine(testDirectory, "test-pdf"), "*.pdf");
            testFiles.AddRange(pdfFiles.Select(f => new TestFile { FilePath = f, FileType = "PDF" }));
        }
        
        if (Directory.Exists(Path.Combine(testDirectory, "test-docx")))
        {
            var docxFiles = Directory.GetFiles(Path.Combine(testDirectory, "test-docx"), "*.docx");
            testFiles.AddRange(docxFiles.Select(f => new TestFile { FilePath = f, FileType = "DOCX" }));
        }
        
        if (Directory.Exists(Path.Combine(testDirectory, "test-xlsx")))
        {
            var xlsFiles = Directory.GetFiles(Path.Combine(testDirectory, "test-xlsx"), "*.xls*");
            testFiles.AddRange(xlsFiles.Select(f => new TestFile { FilePath = f, FileType = "Excel" }));
        }
        
        if (Directory.Exists(Path.Combine(testDirectory, "test-pptx")))
        {
            var pptxFiles = Directory.GetFiles(Path.Combine(testDirectory, "test-pptx"), "*.pptx");
            testFiles.AddRange(pptxFiles.Select(f => new TestFile { FilePath = f, FileType = "PPTX" }));
        }
        
        if (Directory.Exists(Path.Combine(testDirectory, "test-markdown")))
        {
            var mdFiles = Directory.GetFiles(Path.Combine(testDirectory, "test-markdown"), "*.md");
            testFiles.AddRange(mdFiles.Select(f => new TestFile { FilePath = f, FileType = "Markdown" }));
        }

        _logger.LogInformation($"발견된 테스트 파일: {testFiles.Count}개");
        
        // 각 파일에 대해 벤치마크 실행
        foreach (var testFile in testFiles)
        {
            try
            {
                var result = await BenchmarkSingleFileAsync(testFile);
                report.Results.Add(result);
                
                _logger.LogInformation($"✅ {testFile.FileType} 파일 처리 완료: {Path.GetFileName(testFile.FilePath)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ 파일 처리 실패: {testFile.FilePath}");
                
                report.Results.Add(new FileBenchmarkResult
                {
                    FileName = Path.GetFileName(testFile.FilePath),
                    FileType = testFile.FileType,
                    FileSizeMB = GetFileSizeMB(testFile.FilePath),
                    IsSuccessful = false,
                    ErrorMessage = ex.Message
                });
            }
        }
        
        // 보고서 생성
        GenerateReportSummary(report);
        
        return report;
    }

    private async Task<FileBenchmarkResult> BenchmarkSingleFileAsync(TestFile testFile)
    {
        var fileInfo = new FileInfo(testFile.FilePath);
        
        using var profiler = new MemoryProfiler();
        
        var startTime = DateTime.UtcNow;
        profiler.TakeSnapshot("start");
        profiler.TakeSnapshot("before_processing");

        var options = new ChunkingOptions
        {
            Strategy = "Intelligent",
            MaxChunkSize = 1024,
            OverlapSize = 128
        };

        var parsingOptions = new DocumentParsingOptions
        {
            UseAdvancedParsing = false,
            StructuringLevel = StructuringLevel.Low
        };

        DocumentChunk[]? finalResult = null;
        var chunkCount = 0;
        var textLength = 0;

        await foreach (var result in _processor.ProcessWithProgressAsync(testFile.FilePath, options, parsingOptions, CancellationToken.None))
        {
            if (result.IsSuccess && result.Result != null)
            {
                finalResult = result.Result;
                chunkCount = finalResult.Length;
                textLength = finalResult.Sum(c => c.Content?.Length ?? 0);
            }
        }

        var endTime = DateTime.UtcNow;
        profiler.TakeSnapshot("after_processing");

        var analysis = profiler.AnalyzeFileProcessing(
            fileInfo.Length,
            "before_processing",
            "after_processing"
        );

        return new FileBenchmarkResult
        {
            FileName = fileInfo.Name,
            FileType = testFile.FileType,
            FileSizeMB = analysis.FileSizeMB,
            ProcessingTimeMs = (endTime - startTime).TotalMilliseconds,
            ChunkCount = chunkCount,
            ExtractedTextLength = textLength,
            MemoryUsageMB = analysis.MemoryUsageDelta.WorkingSetMB,
            MemoryToFileRatio = analysis.MemoryToFileRatio,
            PerformanceGrade = analysis.PerformanceGrade,
            IsEfficient = analysis.IsEfficient,
            IsSuccessful = true,
            ProcessingSpeedMBps = analysis.FileSizeMB / ((endTime - startTime).TotalMilliseconds / 1000.0)
        };
    }

    private void GenerateReportSummary(BenchmarkReport report)
    {
        var successful = report.Results.Where(r => r.IsSuccessful).ToList();
        var failed = report.Results.Where(r => !r.IsSuccessful).ToList();
        
        _logger.LogInformation("\n=== 벤치마크 종합 결과 ===");
        _logger.LogInformation($"총 테스트 파일: {report.Results.Count}개");
        _logger.LogInformation($"성공: {successful.Count}개, 실패: {failed.Count}개");
        
        if (successful.Any())
        {
            _logger.LogInformation("\n📊 성능 통계:");
            _logger.LogInformation($"평균 처리 속도: {successful.Average(r => r.ProcessingSpeedMBps):F2} MB/초");
            _logger.LogInformation($"평균 메모리 사용률: {successful.Average(r => r.MemoryToFileRatio):F2}배");
            _logger.LogInformation($"평균 청킹 수: {successful.Average(r => r.ChunkCount):F0}개");
            
            // 파일 형식별 통계
            var byType = successful.GroupBy(r => r.FileType).ToList();
            _logger.LogInformation("\n📋 파일 형식별 결과:");
            
            foreach (var group in byType)
            {
                var items = group.ToList();
                _logger.LogInformation($"  {group.Key}: {items.Count}개");
                _logger.LogInformation($"    - 평균 처리 속도: {items.Average(r => r.ProcessingSpeedMBps):F2} MB/초");
                _logger.LogInformation($"    - 평균 메모리 효율: {items.Average(r => r.MemoryToFileRatio):F2}배");
                _logger.LogInformation($"    - 성능 등급 분포: {string.Join(", ", items.GroupBy(r => r.PerformanceGrade).Select(g => $"{g.Key}({g.Count()})"))}");
            }
        }
        
        if (failed.Any())
        {
            _logger.LogError("\n❌ 실패한 파일들:");
            foreach (var failure in failed)
            {
                _logger.LogError($"  - {failure.FileName} ({failure.FileType}): {failure.ErrorMessage}");
            }
        }
        
        report.Summary = new BenchmarkSummary
        {
            TotalFiles = report.Results.Count,
            SuccessfulFiles = successful.Count,
            FailedFiles = failed.Count,
            AverageProcessingSpeedMBps = successful.Any() ? successful.Average(r => r.ProcessingSpeedMBps) : 0,
            AverageMemoryRatio = successful.Any() ? successful.Average(r => r.MemoryToFileRatio) : 0,
            AverageChunkCount = successful.Any() ? successful.Average(r => r.ChunkCount) : 0,
            ExecutionTime = DateTime.UtcNow
        };
    }

    private static double GetFileSizeMB(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        return fileInfo.Length / (1024.0 * 1024.0);
    }
}

public class TestFile
{
    public required string FilePath { get; set; }
    public required string FileType { get; set; }
}

public class BenchmarkReport
{
    public List<FileBenchmarkResult> Results { get; set; } = new();
    public BenchmarkSummary? Summary { get; set; }
}

public class FileBenchmarkResult
{
    public required string FileName { get; set; }
    public required string FileType { get; set; }
    public double FileSizeMB { get; set; }
    public double ProcessingTimeMs { get; set; }
    public int ChunkCount { get; set; }
    public int ExtractedTextLength { get; set; }
    public double MemoryUsageMB { get; set; }
    public double MemoryToFileRatio { get; set; }
    public string PerformanceGrade { get; set; } = string.Empty;
    public bool IsEfficient { get; set; }
    public bool IsSuccessful { get; set; }
    public double ProcessingSpeedMBps { get; set; }
    public string? ErrorMessage { get; set; }
}

public class BenchmarkSummary
{
    public int TotalFiles { get; set; }
    public int SuccessfulFiles { get; set; }
    public int FailedFiles { get; set; }
    public double AverageProcessingSpeedMBps { get; set; }
    public double AverageMemoryRatio { get; set; }
    public double AverageChunkCount { get; set; }
    public DateTime ExecutionTime { get; set; }
}

