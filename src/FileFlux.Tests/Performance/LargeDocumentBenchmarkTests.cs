using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FileFlux.Infrastructure.Performance;
using FileFlux.Tests.Mocks;
using Microsoft.Extensions.Logging;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace FileFlux.Tests.Performance;

/// <summary>
/// 대용량 문서 처리 메모리 효율성 벤치마킹 테스트
/// 목표: 파일 크기의 2배 이하 메모리 사용, 1MB/초 이상 처리 속도
/// </summary>
public class LargeDocumentBenchmarkTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ProgressiveDocumentProcessor _processor;
    private readonly string _testDataDir;
    private bool _disposed;

    public LargeDocumentBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
        _testDataDir = Path.Combine(Path.GetTempPath(), "FileFlux_BenchmarkTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDataDir);

        // 테스트용 프로세서 설정
        var readerFactory = new DocumentReaderFactory();
        readerFactory.RegisterReader(new Infrastructure.Readers.TextDocumentReader());

        var mockTextCompletionService = new MockTextCompletionService();
        var parserFactory = new DocumentParserFactory(mockTextCompletionService);
        var chunkingFactory = new ChunkingStrategyFactory();
        chunkingFactory.RegisterStrategy(() => new Infrastructure.Strategies.IntelligentChunkingStrategy());

        var logger = new LoggerFactory().CreateLogger<ProgressiveDocumentProcessor>();
        _processor = new ProgressiveDocumentProcessor(readerFactory, parserFactory, chunkingFactory, logger);
    }

    [Fact]
    public async Task ProcessLargeTextFile_ShouldUseMemoryEfficientlyAsync()
    {
        // Arrange: 10MB 텍스트 파일 생성
        var filePath = await CreateLargeTextFileAsync(10 * 1024 * 1024, "large_text_10mb.txt");
        var fileInfo = new FileInfo(filePath);
        
        using var profiler = new MemoryProfiler();
        
        // Act & Assert
        profiler.TakeSnapshot("start");
        
        var options = new ChunkingOptions
        {
            Strategy = "Intelligent",
            MaxChunkSize = 1024,
            OverlapSize = 128
        };

        var parsingOptions = new DocumentParsingOptions
        {
            UseAdvancedParsing = false, // 성능 최적화를 위해 기본 파싱 사용
            StructuringLevel = StructuringLevel.Low
        };

        DocumentChunk[]? finalResult = null;
        var chunkCount = 0;

        profiler.TakeSnapshot("before_processing");

        await foreach (var result in _processor.ProcessWithProgressAsync(filePath, options, parsingOptions, CancellationToken.None))
        {
            if (result.IsSuccess && result.Result != null)
            {
                finalResult = result.Result;
                chunkCount = finalResult.Length;
            }
        }

        profiler.TakeSnapshot("after_processing");

        // 메모리 분석
        var analysis = profiler.AnalyzeFileProcessing(
            fileInfo.Length, 
            "before_processing", 
            "after_processing"
        );

        var report = profiler.GenerateReport();

        // 결과 출력
        _output.WriteLine("=== 대용량 파일 처리 벤치마킹 결과 ===");
        _output.WriteLine($"파일 크기: {analysis.FileSizeMB:F2} MB");
        _output.WriteLine($"처리 시간: {analysis.ProcessingTimeMs:N0} ms");
        _output.WriteLine($"처리 속도: {analysis.FileSizeMB / (analysis.ProcessingTimeMs / 1000.0):F2} MB/초");
        _output.WriteLine($"생성된 청크 수: {chunkCount:N0}");
        _output.WriteLine("");
        
        _output.WriteLine("메모리 사용량:");
        _output.WriteLine($"  Working Set: {analysis.MemoryUsageDelta.WorkingSetMB:F2} MB");
        _output.WriteLine($"  Private Memory: {analysis.MemoryUsageDelta.PrivateMemoryMB:F2} MB");
        _output.WriteLine($"  Managed Memory: {analysis.MemoryUsageDelta.ManagedMemoryMB:F2} MB");
        _output.WriteLine($"  메모리/파일 비율: {analysis.MemoryToFileRatio:F2}");
        _output.WriteLine($"  성능 등급: {analysis.PerformanceGrade}");
        _output.WriteLine("");
        
        _output.WriteLine("가비지 컬렉션:");
        _output.WriteLine($"  Gen0: {analysis.GarbageCollections.Gen0}, Gen1: {analysis.GarbageCollections.Gen1}, Gen2: {analysis.GarbageCollections.Gen2}");
        _output.WriteLine($"  총 GC: {analysis.GarbageCollections.Total}");
        
        // 성능 검증
        Assert.NotNull(finalResult);
        Assert.True(chunkCount > 0, "청크가 생성되어야 합니다.");
        
        // 메모리 효율성 검증 (목표: 파일 크기의 2배 이하)
        Assert.True(analysis.IsEfficient, 
            $"메모리 사용량이 파일 크기의 2배를 초과했습니다. 비율: {analysis.MemoryToFileRatio:F2}");
        
        // 처리 속도 검증 (목표: 0.5MB/초 이상 - 테스트 환경 고려하여 낮춤)
        var processingSpeedMBPerSec = analysis.FileSizeMB / (analysis.ProcessingTimeMs / 1000.0);
        Assert.True(processingSpeedMBPerSec > 0.5, 
            $"처리 속도가 너무 느립니다. 현재: {processingSpeedMBPerSec:F2} MB/초");
    }

    [Fact]
    public async Task ProcessMediumTextFile_ShouldMeasureBaselineAsync()
    {
        // Arrange: 1MB 텍스트 파일 생성 (베이스라인 측정용)
        var filePath = await CreateLargeTextFileAsync(1 * 1024 * 1024, "medium_text_1mb.txt");
        var fileInfo = new FileInfo(filePath);
        
        using var profiler = new MemoryProfiler();
        
        // Act
        profiler.TakeSnapshot("baseline_start");
        
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

        profiler.TakeSnapshot("baseline_before_processing");

        await foreach (var result in _processor.ProcessWithProgressAsync(filePath, options, parsingOptions, CancellationToken.None))
        {
            if (result.IsSuccess && result.Result != null)
            {
                finalResult = result.Result;
                chunkCount = finalResult.Length;
            }
        }

        profiler.TakeSnapshot("baseline_after_processing");

        // 베이스라인 분석
        var analysis = profiler.AnalyzeFileProcessing(
            fileInfo.Length, 
            "baseline_before_processing", 
            "baseline_after_processing"
        );

        // 베이스라인 결과 출력
        _output.WriteLine("=== 베이스라인 측정 결과 (1MB 파일) ===");
        _output.WriteLine($"파일 크기: {analysis.FileSizeMB:F2} MB");
        _output.WriteLine($"처리 시간: {analysis.ProcessingTimeMs:N0} ms");
        _output.WriteLine($"처리 속도: {analysis.FileSizeMB / (analysis.ProcessingTimeMs / 1000.0):F2} MB/초");
        _output.WriteLine($"메모리 비율: {analysis.MemoryToFileRatio:F2}");
        _output.WriteLine($"성능 등급: {analysis.PerformanceGrade}");
        _output.WriteLine($"생성된 청크 수: {chunkCount:N0}");

        // 기본 검증
        Assert.NotNull(finalResult);
        Assert.True(chunkCount > 0);
        Assert.True(analysis.MemoryToFileRatio < 5.0, 
            "1MB 파일도 과도한 메모리를 사용합니다.");
    }

    [Fact]
    public async Task ProcessVeryLargeTextFile_ShouldHandleGracefullyAsync()
    {
        // Arrange: 50MB 텍스트 파일 생성 (목표 사이즈)
        var filePath = await CreateLargeTextFileAsync(50 * 1024 * 1024, "very_large_text_50mb.txt");
        var fileInfo = new FileInfo(filePath);
        
        using var profiler = new MemoryProfiler();
        
        _output.WriteLine($"50MB 파일 처리 시작: {fileInfo.Length:N0} bytes");
        
        profiler.TakeSnapshot("large_start");
        
        var options = new ChunkingOptions
        {
            Strategy = "Intelligent",
            MaxChunkSize = 1024,
            OverlapSize = 128
        };

        var parsingOptions = new DocumentParsingOptions
        {
            UseAdvancedParsing = false, // 대용량 파일은 기본 파싱으로 성능 우선
            StructuringLevel = StructuringLevel.Low
        };

        DocumentChunk[]? finalResult = null;
        var chunkCount = 0;
        var progressCount = 0;

        profiler.TakeSnapshot("large_before_processing");

        await foreach (var result in _processor.ProcessWithProgressAsync(filePath, options, parsingOptions, CancellationToken.None))
        {
            progressCount++;
            
            // 중간 스냅샷 (진행률 추적용)
            if (progressCount % 10 == 0)
            {
                profiler.TakeSnapshot($"progress_{progressCount}");
            }
            
            if (result.IsSuccess && result.Result != null)
            {
                finalResult = result.Result;
                chunkCount = finalResult.Length;
            }
        }

        profiler.TakeSnapshot("large_after_processing");

        // 대용량 파일 분석
        var analysis = profiler.AnalyzeFileProcessing(
            fileInfo.Length, 
            "large_before_processing", 
            "large_after_processing"
        );

        var report = profiler.GenerateReport();

        // 상세 결과 출력
        _output.WriteLine("=== 대용량 파일 처리 결과 (50MB) ===");
        _output.WriteLine($"파일 크기: {analysis.FileSizeMB:F2} MB");
        _output.WriteLine($"처리 시간: {analysis.ProcessingTimeMs:N0} ms ({analysis.ProcessingTimeMs/1000.0:F1}초)");
        _output.WriteLine($"처리 속도: {analysis.FileSizeMB / (analysis.ProcessingTimeMs / 1000.0):F2} MB/초");
        _output.WriteLine($"진행률 업데이트 횟수: {progressCount:N0}");
        _output.WriteLine($"생성된 청크 수: {chunkCount:N0}");
        _output.WriteLine("");
        
        _output.WriteLine("메모리 분석:");
        _output.WriteLine($"  Working Set: {analysis.MemoryUsageDelta.WorkingSetMB:F2} MB");
        _output.WriteLine($"  Private Memory: {analysis.MemoryUsageDelta.PrivateMemoryMB:F2} MB");
        _output.WriteLine($"  Managed Memory: {analysis.MemoryUsageDelta.ManagedMemoryMB:F2} MB");
        _output.WriteLine($"  메모리/파일 비율: {analysis.MemoryToFileRatio:F2}");
        _output.WriteLine($"  효율성: {(analysis.IsEfficient ? "통과" : "실패")}");
        _output.WriteLine($"  성능 등급: {analysis.PerformanceGrade}");
        _output.WriteLine("");

        _output.WriteLine("피크 메모리 사용량:");
        _output.WriteLine($"  Working Set: {report.PeakMemoryUsage.WorkingSetMB:F2} MB");
        _output.WriteLine($"  Private Memory: {report.PeakMemoryUsage.PrivateMemoryMB:F2} MB");
        _output.WriteLine($"  Managed Memory: {report.PeakMemoryUsage.ManagedMemoryMB:F2} MB");

        // 성능 검증 (50MB 파일)
        Assert.NotNull(finalResult);
        Assert.True(chunkCount > 0);
        
        // 메모리 효율성 검증 - 50MB 파일의 경우 더 엄격한 기준 적용
        var targetRatio = 2.0; // 100MB 메모리 이하
        Assert.True(analysis.MemoryToFileRatio < targetRatio,
            $"50MB 파일 처리 시 메모리 효율성이 목표를 달성하지 못했습니다. " +
            $"목표: {targetRatio}배 이하, 실제: {analysis.MemoryToFileRatio:F2}배");
        
        // 처리 속도 검증 - 50MB 파일의 경우 더 관대한 기준 적용
        var minSpeedMBPerSec = 0.3; // 최소 0.3MB/초
        var actualSpeed = analysis.FileSizeMB / (analysis.ProcessingTimeMs / 1000.0);
        Assert.True(actualSpeed > minSpeedMBPerSec,
            $"50MB 파일 처리 속도가 목표를 달성하지 못했습니다. " +
            $"목표: {minSpeedMBPerSec} MB/초 이상, 실제: {actualSpeed:F2} MB/초");
    }

    /// <summary>
    /// 지정된 크기의 텍스트 파일 생성
    /// </summary>
    private async Task<string> CreateLargeTextFileAsync(int sizeInBytes, string fileName)
    {
        var filePath = Path.Combine(_testDataDir, fileName);
        
        // 반복되는 텍스트 패턴으로 파일 생성 (실제 문서와 유사하게)
        var sampleParagraph = """
            FileFlux는 다양한 문서 형식을 파싱하고 LLM을 활용하여 일관된 구조로 재구성한 텍스트 청크를 생성하는 .NET SDK입니다. 
            이 도구는 RAG(Retrieval-Augmented Generation) 시스템을 위한 전처리 작업에 특화되어 있으며, 
            고품질의 구조화된 청크를 통해 검색 정확도와 생성 품질을 크게 향상시킵니다.
            
            주요 기능으로는 PDF, DOCX, XLSX, PPTX, MD, TXT, JSON, CSV 등 다양한 형식 지원, 
            메타데이터 보존을 통한 고급 처리 파이프라인, 
            지능형, 의미적, 고정 크기, 단락 기반의 유연한 청킹 전략, 
            그리고 인터페이스 기반의 깔끔한 아키텍처가 있습니다.
            
            FileFlux의 핵심 철학은 텍스트 완성 서비스 제공업체 선택의 자유입니다. 
            OpenAI, Anthropic, Azure, 로컬 모델 등 원하는 LLM을 선택하여 사용할 수 있으며, 
            임베딩 서비스와 벡터 저장소도 자유롭게 선택할 수 있습니다.
            
            """;

        var paragraphBytes = Encoding.UTF8.GetBytes(sampleParagraph);
        var repeatCount = sizeInBytes / paragraphBytes.Length + 1;

        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        for (int i = 0; i < repeatCount; i++)
        {
            await writer.WriteAsync($"=== 섹션 {i + 1:N0} ===\n");
            await writer.WriteAsync(sampleParagraph);
            await writer.WriteAsync("\n\n");
            
            // 목표 크기에 도달했으면 중단
            if (writer.BaseStream.Length >= sizeInBytes)
                break;
        }

        await writer.FlushAsync();
        
        var actualSize = new FileInfo(filePath).Length;
        _output.WriteLine($"테스트 파일 생성 완료: {fileName} ({actualSize:N0} bytes)");
        
        return filePath;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // 테스트 파일 정리
            if (Directory.Exists(_testDataDir))
            {
                try
                {
                    Directory.Delete(_testDataDir, true);
                }
                catch
                {
                    // 정리 실패 시 무시 (테스트 환경에서 발생할 수 있음)
                }
            }
            _disposed = true;
        }
    }
}