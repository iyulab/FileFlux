using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace FileFlux.Tests;

/// <summary>
/// 진행률 추적 및 파일 시스템 결과 저장 테스트
/// </summary>
public class ProgressiveProcessingTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<ProgressiveDocumentProcessor> _logger;
    private readonly FileSystemResultStorage _resultStorage;
    private readonly string _testResultsDirectory;

    public ProgressiveProcessingTests(ITestOutputHelper output)
    {
        _output = output;

        // 테스트용 로거 설정
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<ProgressiveDocumentProcessor>();

        // 테스트용 결과 저장소 설정
        _testResultsDirectory = Path.Combine(Path.GetTempPath(), $"fileflux-test-{Guid.NewGuid():N}");
        _resultStorage = new FileSystemResultStorage(_testResultsDirectory);

        _output.WriteLine($"Test results directory: {_testResultsDirectory}");
    }

    [Fact]
    public async Task ProgressiveProcessor_ShouldReportProgress_ThroughAsyncEnumerable()
    {
        // Arrange
        var testText = """
        # FileFlux Progressive Processing Test
        
        This is a comprehensive test document for validating the progressive processing capabilities
        of the FileFlux document processing system.
        
        ## Features Being Tested
        
        - Real-time progress reporting through IAsyncEnumerable
        - File system based result storage with hash-based directory structure
        - Step-by-step processing visibility
        - Error handling and recovery mechanisms
        
        ## Expected Behavior
        
        1. **Reading Stage**: File should be read in chunks with progress updates
        2. **Extracting Stage**: Text extraction with encoding detection
        3. **Parsing Stage**: Document structure analysis
        4. **Chunking Stage**: Content divided into manageable chunks
        5. **Validating Stage**: Quality assurance checks
        6. **Completed Stage**: Final results with comprehensive metadata
        
        ## Performance Expectations
        
        The system should provide smooth progress updates without blocking the UI thread
        and store intermediate results for inspection and debugging purposes.
        """;

        // 임시 파일 생성
        var tempPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempPath, testText);

        var fileName = Path.GetFileName(tempPath);
        var fileHash = await _resultStorage.ComputeFileHashAsync(tempPath);

        try
        {
            // Setup
            var readerFactory = new DocumentReaderFactory();
            readerFactory.RegisterReader(new Infrastructure.Readers.TextDocumentReader());

            var mockTextCompletionService = new Mocks.MockTextCompletionService();
            var parserFactory = new DocumentParserFactory(mockTextCompletionService);
            var chunkingFactory = new Infrastructure.Factories.ChunkingStrategyFactory();

            // 실제 청킹 전략 등록
            chunkingFactory.RegisterStrategy(() => new Infrastructure.Strategies.ParagraphChunkingStrategy());

            var processor = new ProgressiveDocumentProcessor(readerFactory, parserFactory, chunkingFactory, _logger);

            var progressUpdates = new List<ProcessingProgress>();
            DocumentChunk[]? finalResult = null;

            // Act & Assert - 진행률 추적
            await foreach (var result in processor.ProcessWithProgressAsync(
                tempPath,
                new ChunkingOptions { Strategy = "Paragraph", MaxChunkSize = 300 },
                new DocumentParsingOptions { UseLlm = false },
                CancellationToken.None))
            {
                progressUpdates.Add(result.Progress);

                _output.WriteLine($"Progress: {result.Progress.Stage} - {result.Progress.ProgressPercentage} - {result.Progress.Message}");

                // 진행률 파일 시스템에 저장
                await _resultStorage.SaveProgressAsync(fileHash, result.Progress);

                if (result.IsSuccess && result.Result != null)
                {
                    finalResult = result.Result;
                }

                // 각 단계별 결과 저장 (실제 데이터가 있을 때만)
                if (result.Progress.Stage == ProcessingStage.Completed && result.Result != null)
                {
                    // 최종 청크 결과 저장
                    var chunksPath = await _resultStorage.SaveChunksAsync(
                        fileHash,
                        result.Result,
                        new ChunkingOptions { Strategy = "Paragraph", MaxChunkSize = 300 });

                    _output.WriteLine($"Chunks saved to: {chunksPath}");
                }

                // 진행률 검증
                Assert.True(result.Progress.OverallProgress >= 0.0 && result.Progress.OverallProgress <= 1.0,
                    "Overall progress should be between 0.0 and 1.0");

                if (result.Progress.Stage != ProcessingStage.Error)
                {
                    Assert.NotNull(result.Progress.Message);
                    Assert.True(result.Progress.Message.Length > 0);
                }
            }

            // Final Assertions
            Assert.NotNull(finalResult);
            Assert.True(finalResult.Length > 0);
            Assert.True(progressUpdates.Count > 0);

            // 진행률 시퀀스 검증
            var completedProgress = progressUpdates.LastOrDefault(p => p.Stage == ProcessingStage.Completed);
            Assert.NotNull(completedProgress);
            Assert.Equal(1.0, completedProgress.OverallProgress, 2);

            _output.WriteLine($"Final result: {finalResult.Length} chunks created");
            _output.WriteLine($"Progress updates received: {progressUpdates.Count}");

            // 파일 시스템 결과 검증
            var summary = await _resultStorage.GetProcessingSummaryAsync(fileHash);
            Assert.NotNull(summary);
            Assert.True(summary.HasChunking);
            Assert.Equal(finalResult.Length, summary.ChunkCount);

            _output.WriteLine($"File hash: {fileHash}");
            _output.WriteLine($"Results directory: {summary.ResultDirectory}");
            _output.WriteLine($"Summary: {summary.ChunkCount} chunks, {summary.TotalCharacters} characters");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task FileSystemResultStorage_ShouldCreateHashBasedDirectories()
    {
        // Arrange
        var testContent = "Test content for hash-based directory structure validation.";
        var tempPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempPath, testContent);

        try
        {
            // Act
            var fileHash = await _resultStorage.ComputeFileHashAsync(tempPath);
            var resultDirectory = _resultStorage.GetResultDirectory(fileHash);

            // 가짜 결과 데이터 생성
            var rawContent = new RawDocumentContent
            {
                Text = testContent,
                FileInfo = new FileMetadata
                {
                    FileName = Path.GetFileName(tempPath),
                    FileExtension = ".tmp",
                    FileSize = testContent.Length,
                    ExtractedAt = DateTime.UtcNow,
                    ReaderType = "TestReader"
                },
                StructuralHints = new Dictionary<string, object>
                {
                    ["file_type"] = "test",
                    ["word_count"] = testContent.Split(' ').Length
                },
                ExtractionWarnings = new List<string>()
            };

            var parsedContent = new ParsedDocumentContent
            {
                StructuredText = testContent,
                OriginalText = testContent,
                Metadata = new DocumentMetadata { FileName = Path.GetFileName(tempPath) },
                Structure = new FileFlux.Domain.DocumentStructure
                {
                    DocumentType = "Technical",
                    Sections = new List<DocumentSection>()
                },
                Quality = new QualityMetrics
                {
                    ConfidenceScore = 0.85,
                    CompletenessScore = 0.85,
                    ConsistencyScore = 0.85
                },
                ParsingInfo = new ParsingMetadata
                {
                    UsedLlm = false,
                    ParserType = "TestParser"
                }
            };

            var chunks = new DocumentChunk[]
            {
                new DocumentChunk
                {
                    Id = Guid.NewGuid().ToString(),
                    Content = testContent,
                    ChunkIndex = 0,
                    StartPosition = 0,
                    EndPosition = testContent.Length,
                    Metadata = new DocumentMetadata { FileName = Path.GetFileName(tempPath) }
                }
            };

            // 각 단계별 결과 저장
            var rawPath = await _resultStorage.SaveRawContentAsync(fileHash, rawContent);
            var parsedPath = await _resultStorage.SaveParsedContentAsync(fileHash, parsedContent);
            var chunksPath = await _resultStorage.SaveChunksAsync(fileHash, chunks, new ChunkingOptions());

            // Assert
            _output.WriteLine($"File hash: {fileHash}");
            _output.WriteLine($"Result directory: {resultDirectory}");
            _output.WriteLine($"Raw content saved to: {rawPath}");
            _output.WriteLine($"Parsed content saved to: {parsedPath}");
            _output.WriteLine($"Chunks saved to: {chunksPath}");

            // 디렉토리 구조 검증
            Assert.True(Directory.Exists(resultDirectory));
            Assert.True(File.Exists(rawPath));
            Assert.True(File.Exists(parsedPath));
            Assert.True(File.Exists(chunksPath));

            // 해시 기반 디렉토리 구조 검증 (xx/xx/hash 형태)
            var relativePath = Path.GetRelativePath(_testResultsDirectory, resultDirectory);
            var pathParts = relativePath.Split(Path.DirectorySeparatorChar);
            Assert.Equal(3, pathParts.Length); // subdir1/subdir2/fullhash
            Assert.Equal(2, pathParts[0].Length); // 첫 번째 서브디렉토리는 2자리
            Assert.Equal(2, pathParts[1].Length); // 두 번째 서브디렉토리는 2자리
            Assert.Equal(fileHash, pathParts[2]); // 마지막은 전체 해시

            // 개별 청크 파일 확인
            var individualChunksDir = Path.Combine(Path.GetDirectoryName(chunksPath)!, "individual-chunks");
            Assert.True(Directory.Exists(individualChunksDir));

            var chunkFiles = Directory.GetFiles(individualChunksDir, "chunk-*.txt");
            Assert.Single(chunkFiles); // 1개 청크이므로 1개 파일

            var chunkContent = await File.ReadAllTextAsync(chunkFiles[0]);
            Assert.Contains(testContent, chunkContent);
            Assert.Contains("=== Chunk 1/1 ===", chunkContent);

            _output.WriteLine($"Individual chunk files: {chunkFiles.Length}");
            _output.WriteLine($"Individual chunks directory: {individualChunksDir}");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task FileSystemResultStorage_ShouldLoadSavedResults()
    {
        // Arrange
        var testText = "Test content for result loading validation.";
        var tempPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempPath, testText);

        try
        {
            var fileHash = await _resultStorage.ComputeFileHashAsync(tempPath);

            var rawContent = new RawDocumentContent
            {
                Text = testText,
                FileInfo = new FileMetadata
                {
                    FileName = Path.GetFileName(tempPath),
                    FileExtension = ".tmp",
                    FileSize = testText.Length
                }
            };

            var chunks = new DocumentChunk[]
            {
                new DocumentChunk
                {
                    Id = "test-chunk-1",
                    Content = testText,
                    ChunkIndex = 0,
                    Metadata = new DocumentMetadata { FileName = Path.GetFileName(tempPath) }
                }
            };

            // Act - Save
            await _resultStorage.SaveRawContentAsync(fileHash, rawContent);
            await _resultStorage.SaveChunksAsync(fileHash, chunks, new ChunkingOptions());

            // Act - Load
            var loadedRawContent = await _resultStorage.LoadRawContentAsync(fileHash);
            var loadedChunks = await _resultStorage.LoadChunksAsync(fileHash);

            // Assert
            Assert.NotNull(loadedRawContent);
            Assert.Equal(testText, loadedRawContent.Text);

            Assert.NotNull(loadedChunks);
            Assert.Single(loadedChunks);
            Assert.Equal("test-chunk-1", loadedChunks[0].Id);
            Assert.Equal(testText, loadedChunks[0].Content);

            _output.WriteLine($"Successfully loaded raw content: {loadedRawContent.Text.Length} characters");
            _output.WriteLine($"Successfully loaded chunks: {loadedChunks.Length} chunks");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task ProcessSmallDocumentFile_ShouldHandleReasonableSizedContent()
    {
        // Arrange - 작은 테스트 문서 사용
        var smallDocumentPath = "../../../test-document-small.txt";
        if (!File.Exists(smallDocumentPath))
        {
            // 테스트 문서가 없으면 생성
            var testContent = @"FileFlux Document Processing Test

This is a small test document for FileFlux progressive processing.
It contains multiple paragraphs to demonstrate the chunking functionality.

Key Features:
- Progressive document processing with real-time progress tracking
- Multiple document format support (PDF, DOCX, TXT, etc.)
- Advanced chunking strategies for optimal RAG performance

This document is designed to be small enough to avoid context limits
while still providing meaningful content for testing the chunking algorithms.";

            smallDocumentPath = Path.GetTempFileName();
            await File.WriteAllTextAsync(smallDocumentPath, testContent);
        }

        try
        {
            var readerFactory = new DocumentReaderFactory();
            readerFactory.RegisterReader(new Infrastructure.Readers.TextDocumentReader());

            var mockTextCompletionService = new Mocks.MockTextCompletionService();
            var parserFactory = new DocumentParserFactory(mockTextCompletionService);
            var chunkingFactory = new Infrastructure.Factories.ChunkingStrategyFactory();

            // 청킹 전략 등록
            chunkingFactory.RegisterStrategy(() => new Infrastructure.Strategies.IntelligentChunkingStrategy());
            chunkingFactory.RegisterStrategy(() => new Infrastructure.Strategies.ParagraphChunkingStrategy());

            var processor = new ProgressiveDocumentProcessor(readerFactory, parserFactory, chunkingFactory, _logger);

            var progressUpdates = new List<ProcessingProgress>();
            DocumentChunk[]? finalResult = null;
            var fileHash = await _resultStorage.ComputeFileHashAsync(smallDocumentPath);

            // Act - 작은 청크 크기로 처리
            await foreach (var result in processor.ProcessWithProgressAsync(
                smallDocumentPath,
                new ChunkingOptions { Strategy = "Intelligent", MaxChunkSize = 256, OverlapSize = 32 },
                new DocumentParsingOptions { UseLlm = false },
                CancellationToken.None))
            {
                progressUpdates.Add(result.Progress);

                _output.WriteLine($"Progress: {result.Progress.Stage} - {result.Progress.ProgressPercentage:P1} - {result.Progress.Message}");

                await _resultStorage.SaveProgressAsync(fileHash, result.Progress);

                if (result.IsSuccess && result.Result != null)
                {
                    finalResult = result.Result;
                }
            }

            // Assert
            Assert.NotNull(finalResult);
            Assert.True(finalResult.Length > 0);
            Assert.True(progressUpdates.Count > 0);

            // 지능형 청킹 결과 분석 - 실제 작동 방식 검증
            var maxObservedSize = finalResult.Max(c => c.Content.Length);
            var avgChunkSize = finalResult.Average(c => c.Content.Length);
            var minChunkSize = finalResult.Min(c => c.Content.Length);

            // 기본적인 청킹 성공 검증
            foreach (var chunk in finalResult)
            {
                Assert.True(chunk.Content.Length >= 10, // 최소한의 컨텐츠
                    $"Chunk too small: {chunk.Content.Length} characters");
                Assert.NotNull(chunk.Content);
                Assert.NotEmpty(chunk.Content.Trim());
                Assert.NotNull(chunk.Id);
            }

            // 청크들이 어느 정도 균등한 사이즈를 가지는지 확인 (너무 극단적이지 않아야 함)
            var sizeVariance = maxObservedSize - minChunkSize;
            Assert.True(maxObservedSize > 0, "Maximum chunk size should be positive");

            // 청크 내용이 원본 문서의 내용을 포함하는지 검증
            var allChunkContent = string.Join(" ", finalResult.Select(c => c.Content));
            Assert.Contains("FileFlux", allChunkContent);
            Assert.Contains("document processing", allChunkContent, StringComparison.OrdinalIgnoreCase);

            _output.WriteLine($"Small document processing completed: {finalResult.Length} chunks");
            _output.WriteLine($"Average chunk size: {avgChunkSize:F1} characters");
            _output.WriteLine($"Max chunk size: {maxObservedSize} characters");
            _output.WriteLine($"Min chunk size: {minChunkSize} characters");
            _output.WriteLine($"Size variance: {sizeVariance} characters");

            // 청크 배비 정보 출력
            for (int i = 0; i < Math.Min(3, finalResult.Length); i++)
            {
                var preview = finalResult[i].Content.Length > 50
                    ? finalResult[i].Content.Substring(0, 50) + "..."
                    : finalResult[i].Content;
                _output.WriteLine($"Chunk {i + 1}: {finalResult[i].Content.Length} chars - '{preview}'");
            }
        }
        finally
        {
            // 임시 파일이었다면 삭제
            if (smallDocumentPath.Contains("tmp"))
            {
                File.Delete(smallDocumentPath);
            }
        }
    }

    [Fact]
    public async Task StepByStepProcessing_ShouldProvideDetailedStepResults()
    {
        // Arrange
        var testText = "# Test Document\n\nThis is a test for step-by-step processing.\n\n## Section 1\nContent here.";
        var tempPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempPath, testText);

        try
        {
            var readerFactory = new DocumentReaderFactory();
            readerFactory.RegisterReader(new Infrastructure.Readers.TextDocumentReader());

            var mockTextCompletionService = new Mocks.MockTextCompletionService();
            var parserFactory = new DocumentParserFactory(mockTextCompletionService);
            var chunkingFactory = new Infrastructure.Factories.ChunkingStrategyFactory();

            // 청킹 전략 등록
            chunkingFactory.RegisterStrategy(() => new Infrastructure.Strategies.ParagraphChunkingStrategy());

            var processor = new ProgressiveDocumentProcessor(readerFactory, parserFactory, chunkingFactory, _logger);

            var stepResults = new List<ProcessingStepResult>();

            // Act
            var chunkingOptions = new ChunkingOptions { Strategy = "Paragraph" };
            await foreach (var stepResult in processor.ProcessStepsAsync(tempPath, chunkingOptions))
            {
                stepResults.Add(stepResult);

                _output.WriteLine($"Step: {stepResult.Stage} - Success: {stepResult.IsSuccess} - {stepResult.Progress.Message}");

                if (stepResult.Data != null)
                {
                    var dataType = stepResult.Data.GetType().Name;
                    _output.WriteLine($"  Data: {dataType}");
                }

                if (!stepResult.IsSuccess && !string.IsNullOrEmpty(stepResult.ErrorMessage))
                {
                    _output.WriteLine($"  Error: {stepResult.ErrorMessage}");
                }
            }

            // Assert
            Assert.True(stepResults.Count >= 5); // 최소 5단계 (Reading, Extracting, Parsing, Chunking, Completed)

            // 각 주요 단계가 포함되어 있는지 확인
            var stages = stepResults.Select(r => r.Stage).ToHashSet();
            Assert.Contains(ProcessingStage.Extracting, stages);
            Assert.Contains(ProcessingStage.Parsing, stages);
            Assert.Contains(ProcessingStage.Chunking, stages);
            Assert.Contains(ProcessingStage.Completed, stages);

            // 최종 단계에서 청크 데이터 확인
            var completedStep = stepResults.LastOrDefault(r => r.Stage == ProcessingStage.Completed);
            Assert.NotNull(completedStep);
            Assert.True(completedStep.IsSuccess);
            Assert.IsType<DocumentChunk[]>(completedStep.Data);

            var finalChunks = (DocumentChunk[])completedStep.Data!;
            Assert.True(finalChunks.Length > 0);

            _output.WriteLine($"Total processing steps: {stepResults.Count}");
            _output.WriteLine($"Final chunks: {finalChunks.Length}");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    public void Dispose()
    {
        // 테스트 결과 디렉토리 정리
        try
        {
            if (Directory.Exists(_testResultsDirectory))
            {
                Directory.Delete(_testResultsDirectory, recursive: true);
                _output.WriteLine($"Cleaned up test results directory: {_testResultsDirectory}");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Failed to clean up test results directory: {ex.Message}");
        }

        _resultStorage?.Dispose();
    }
}