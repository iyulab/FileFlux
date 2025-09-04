using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure.Factories;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text;

namespace FileFlux.Infrastructure;

/// <summary>
/// IAsyncEnumerable을 활용한 진행률 추적 가능한 문서 처리기
/// </summary>
public class ProgressiveDocumentProcessor : IProgressiveDocumentProcessor
{
    private readonly IDocumentReaderFactory _readerFactory;
    private readonly IDocumentParserFactory _parserFactory;
    private readonly IChunkingStrategyFactory _chunkingStrategyFactory;
    private readonly ILogger<ProgressiveDocumentProcessor> _logger;

    /// <summary>
    /// ProgressiveDocumentProcessor 인스턴스를 초기화합니다
    /// </summary>
    public ProgressiveDocumentProcessor(
        IDocumentReaderFactory readerFactory,
        IDocumentParserFactory parserFactory,
        IChunkingStrategyFactory chunkingStrategyFactory,
        ILogger<ProgressiveDocumentProcessor> logger)
    {
        _readerFactory = readerFactory ?? throw new ArgumentNullException(nameof(readerFactory));
        _parserFactory = parserFactory ?? throw new ArgumentNullException(nameof(parserFactory));
        _chunkingStrategyFactory = chunkingStrategyFactory ?? throw new ArgumentNullException(nameof(chunkingStrategyFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 문서를 비동기 스트림으로 처리하며 진행률을 실시간으로 보고합니다
    /// </summary>
    public async IAsyncEnumerable<ProcessingResult<DocumentChunk[]>> ProcessWithProgressAsync(
        string filePath,
        ChunkingOptions? chunkingOptions = null,
        DocumentParsingOptions? parsingOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var fileName = Path.GetFileName(filePath);

        _logger.LogInformation("Starting progressive processing for file: {FilePath}", filePath);

        // 1. 시작 진행률 보고
        yield return new ProcessingResult<DocumentChunk[]>
        {
            Progress = ProcessingProgress.Factory.Create(
                filePath,
                ProcessingStage.Reading,
                0.0,
                $"파일 처리 시작: {fileName}")
        };

        DocumentChunk[]? finalResult = null;
        RawDocumentContent? rawContent = null;
        ParsedDocumentContent? parsedContent = null;
        var hasError = false;

        // 2. 각 단계별로 진행률과 함께 처리
        await foreach (var stepResult in ProcessStepsAsync(filePath, chunkingOptions, parsingOptions, cancellationToken))
        {
            if (!stepResult.IsSuccess)
            {
                hasError = true;
                var errorProgress = ProcessingProgress.Factory.CreateError(filePath, stepResult.ErrorMessage ?? "Unknown error");

                yield return new ProcessingResult<DocumentChunk[]>
                {
                    Progress = errorProgress,
                    RawContent = rawContent,
                    ParsedContent = parsedContent
                };
                break;
            }

            // 단계별 진행률 계산
            var currentProgress = stepResult.Stage switch
            {
                ProcessingStage.Extracting => 0.25,  // 25%
                ProcessingStage.Parsing => 0.50,     // 50%
                ProcessingStage.Chunking => 0.75,    // 75%
                ProcessingStage.Validating => 0.90,  // 90%
                ProcessingStage.Completed => 1.0,    // 100%
                _ => 0.0
            };

            // 진행률 업데이트
            stepResult.Progress.OverallProgress = currentProgress;
            stepResult.Progress.StartTime = startTime;
            stepResult.Progress.CurrentTime = DateTime.UtcNow;

            // 단계별 데이터 저장
            if (stepResult.Stage == ProcessingStage.Extracting && stepResult.Data is RawDocumentContent raw)
            {
                rawContent = raw;
            }
            else if (stepResult.Stage == ProcessingStage.Parsing && stepResult.Data is ParsedDocumentContent parsed)
            {
                parsedContent = parsed;
            }
            else if (stepResult.Stage == ProcessingStage.Completed && stepResult.Data is DocumentChunk[] chunks)
            {
                finalResult = chunks;
            }

            yield return new ProcessingResult<DocumentChunk[]>
            {
                Result = finalResult,
                Progress = stepResult.Progress,
                RawContent = rawContent,
                ParsedContent = parsedContent
            };
        }

        // 3. 완료 보고
        if (!hasError && finalResult != null)
        {
            _logger.LogInformation("Processing completed successfully for file: {FilePath}. Generated {ChunkCount} chunks.",
                filePath, finalResult.Length);
        }
    }

    /// <summary>
    /// 스트림을 비동기 스트림으로 처리하며 진행률을 실시간으로 보고합니다
    /// 대용량 파일의 경우 메모리 효율적 처리를 위해 청킹된 처리를 수행합니다
    /// </summary>
    public async IAsyncEnumerable<ProcessingResult<DocumentChunk[]>> ProcessWithProgressAsync(
        Stream stream,
        string fileName,
        ChunkingOptions? chunkingOptions = null,
        DocumentParsingOptions? parsingOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const int MAX_MEMORY_SIZE = 50 * 1024 * 1024; // 50MB

        // 스트림 크기 확인
        long streamSize = 0;
        try
        {
            streamSize = stream.Length;
        }
        catch
        {
            // Length를 지원하지 않는 스트림의 경우 기존 방식 사용
            streamSize = MAX_MEMORY_SIZE + 1;
        }

        if (streamSize <= MAX_MEMORY_SIZE)
        {
            // 작은 파일의 경우 기존 방식 사용
            await foreach (var result in ProcessSmallStreamAsync(stream, fileName, chunkingOptions, parsingOptions, cancellationToken))
            {
                yield return result;
            }
        }
        else
        {
            // 대용량 파일의 경우 스트리밍 처리
            await foreach (var result in ProcessLargeStreamAsync(stream, fileName, chunkingOptions, parsingOptions, cancellationToken))
            {
                yield return result;
            }
        }
    }

    /// <summary>
    /// 작은 스트림을 임시 파일로 처리
    /// </summary>
    private async IAsyncEnumerable<ProcessingResult<DocumentChunk[]>> ProcessSmallStreamAsync(
        Stream stream,
        string fileName,
        ChunkingOptions? chunkingOptions,
        DocumentParsingOptions? parsingOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var tempPath = Path.GetTempFileName();
        Exception? cleanupException = null;

        try
        {
            // 스트림 내용을 임시 파일에 복사
            using (var fileStream = File.Create(tempPath))
            {
                await stream.CopyToAsync(fileStream, cancellationToken);
            }

            // 파일 기반 처리 위임
            await foreach (var result in ProcessWithProgressAsync(tempPath, chunkingOptions, parsingOptions, cancellationToken))
            {
                yield return result;
            }
        }
        finally
        {
            // 임시 파일 정리
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception ex)
            {
                cleanupException = ex;
                _logger.LogWarning(ex, "Failed to cleanup temporary file: {TempPath}", tempPath);
            }
        }

        if (cleanupException != null)
        {
            yield return new ProcessingResult<DocumentChunk[]>
            {
                Progress = ProcessingProgress.Factory.CreateError(fileName, $"임시 파일 정리 실패: {cleanupException.Message}")
            };
        }
    }

    /// <summary>
    /// 대용량 스트림을 청킹하여 처리
    /// </summary>
    private async IAsyncEnumerable<ProcessingResult<DocumentChunk[]>> ProcessLargeStreamAsync(
        Stream stream,
        string fileName,
        ChunkingOptions? chunkingOptions,
        DocumentParsingOptions? parsingOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const int CHUNK_SIZE = 10 * 1024 * 1024; // 10MB 청크

        _logger.LogInformation("Processing large stream in chunks: {FileName}, Size: {StreamSize}", fileName, stream.Length);

        yield return new ProcessingResult<DocumentChunk[]>
        {
            Progress = ProcessingProgress.Factory.Create(
                fileName,
                ProcessingStage.Reading,
                0.0,
                $"대용량 파일 스트리밍 처리 시작: {fileName}")
        };

        var allChunks = new List<DocumentChunk>();
        var buffer = new byte[CHUNK_SIZE];
        var totalBytesRead = 0L;
        var chunkIndex = 0;

        // C# yield return 에서 try-catch 사용으로 인한 제약 해결
        var hasError = false;
        string? errorMessage = null;

        while (true)
        {
            int bytesRead = 0;

            try
            {
                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead == 0) break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading from large stream: {FileName}", fileName);
                hasError = true;
                errorMessage = ex.Message;
                break;
            }

            totalBytesRead += bytesRead;
            chunkIndex++;

            try
            {
                // 텍스트 추출 시도
                var text = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);

                // 간단한 청킹 (실제로는 더 정교한 로직 필요)
                var simpleChunks = SplitTextIntoChunks(text, fileName, chunkIndex, chunkingOptions ?? new ChunkingOptions());
                allChunks.AddRange(simpleChunks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing chunk {ChunkIndex} from stream: {FileName}", chunkIndex, fileName);
                hasError = true;
                errorMessage = ex.Message;
                break;
            }

            var progress = stream.Length > 0 ? (double)totalBytesRead / stream.Length : 0.5;

            yield return new ProcessingResult<DocumentChunk[]>
            {
                Result = allChunks.ToArray(),
                Progress = ProcessingProgress.Factory.Create(
                    fileName,
                    ProcessingStage.Chunking,
                    progress,
                    $"청크 {chunkIndex} 처리 완료 ({allChunks.Count}개 청크 생성)")
            };
        }

        // 오류 처리
        if (hasError)
        {
            yield return new ProcessingResult<DocumentChunk[]>
            {
                Progress = ProcessingProgress.Factory.CreateError(fileName, $"대용량 스트림 처리 오류: {errorMessage}")
            };
        }
        else
        {
            // 최종 완료
            yield return new ProcessingResult<DocumentChunk[]>
            {
                Result = allChunks.ToArray(),
                Progress = ProcessingProgress.Factory.Create(
                    fileName,
                    ProcessingStage.Completed,
                    1.0,
                    $"대용량 파일 처리 완료: {allChunks.Count}개 청크 생성")
            };
        }
    }

    /// <summary>
    /// 텍스트를 간단한 청킹 규칙으로 분할
    /// </summary>
    private IEnumerable<DocumentChunk> SplitTextIntoChunks(string text, string fileName, int batchIndex, ChunkingOptions options)
    {
        var chunks = new List<DocumentChunk>();
        var sentences = text.Split(new[] { '.', '!', '?', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        var currentChunk = new StringBuilder();
        var chunkIndex = 0;

        foreach (var sentence in sentences)
        {
            var trimmedSentence = sentence.Trim();
            if (string.IsNullOrEmpty(trimmedSentence)) continue;

            if (currentChunk.Length + trimmedSentence.Length > options.MaxChunkSize && currentChunk.Length > 0)
            {
                // 현재 청크 완성
                chunks.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid().ToString(),
                    Content = currentChunk.ToString().Trim(),
                    ChunkIndex = (batchIndex * 1000) + chunkIndex,
                    Metadata = new DocumentMetadata
                    {
                        FileName = fileName,
                        ProcessedAt = DateTime.UtcNow
                    },
                    Properties = new Dictionary<string, object>
                    {
                        ["BatchIndex"] = batchIndex,
                        ["IsStreamProcessed"] = true
                    }
                });

                chunkIndex++;
                currentChunk.Clear();
            }

            currentChunk.AppendLine(trimmedSentence);
        }

        // 마지막 청크 처리
        if (currentChunk.Length > 0)
        {
            chunks.Add(new DocumentChunk
            {
                Id = Guid.NewGuid().ToString(),
                Content = currentChunk.ToString().Trim(),
                ChunkIndex = (batchIndex * 1000) + chunkIndex,
                Metadata = new DocumentMetadata
                {
                    FileName = fileName,
                    ProcessedAt = DateTime.UtcNow
                },
                Properties = new Dictionary<string, object>
                {
                    ["BatchIndex"] = batchIndex,
                    ["IsStreamProcessed"] = true
                }
            });
        }

        return chunks;
    }

    /// <summary>
    /// 단계별 처리를 위한 메서드
    /// </summary>
    public async IAsyncEnumerable<ProcessingStepResult> ProcessStepsAsync(
        string filePath,
        ChunkingOptions? chunkingOptions = null,
        DocumentParsingOptions? parsingOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        chunkingOptions ??= new ChunkingOptions();
        parsingOptions ??= new DocumentParsingOptions();

        // 단계별 데이터를 저장하면서 처리 
        RawDocumentContent? rawContent = null;
        ParsedDocumentContent? parsedContent = null;
        DocumentChunk[]? chunks = null;

        // 1. Extraction 단계
        ProcessingStepResult extractionResult;
        try
        {
            extractionResult = await ProcessExtractionStageAsync(filePath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Extraction stage: {FilePath}", filePath);
            extractionResult = new ProcessingStepResult
            {
                Stage = ProcessingStage.Error,
                Progress = ProcessingProgress.Factory.CreateError(filePath, ex.Message),
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }

        if (!extractionResult.IsSuccess)
        {
            yield return extractionResult;
            yield break;
        }
        rawContent = extractionResult.Data as RawDocumentContent;
        yield return extractionResult;

        // 2. Parsing 단계
        ProcessingStepResult parsingResult;
        try
        {
            parsingResult = await ProcessParsingStageAsync(rawContent!, parsingOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Parsing stage: {FilePath}", filePath);
            parsingResult = new ProcessingStepResult
            {
                Stage = ProcessingStage.Error,
                Progress = ProcessingProgress.Factory.CreateError(filePath, ex.Message),
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }

        if (!parsingResult.IsSuccess)
        {
            yield return parsingResult;
            yield break;
        }
        parsedContent = parsingResult.Data as ParsedDocumentContent;
        yield return parsingResult;

        // 3. Chunking 단계
        ProcessingStepResult chunkingResult;
        try
        {
            chunkingResult = await ProcessChunkingStageAsync(parsedContent!, chunkingOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Chunking stage: {FilePath}", filePath);
            chunkingResult = new ProcessingStepResult
            {
                Stage = ProcessingStage.Error,
                Progress = ProcessingProgress.Factory.CreateError(filePath, ex.Message),
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }

        if (!chunkingResult.IsSuccess)
        {
            yield return chunkingResult;
            yield break;
        }
        chunks = chunkingResult.Data as DocumentChunk[];
        yield return chunkingResult;

        // 4. Validation 단계
        ProcessingStepResult validationResult;
        try
        {
            validationResult = await ProcessValidationStageAsync(chunks!, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Validation stage: {FilePath}", filePath);
            validationResult = new ProcessingStepResult
            {
                Stage = ProcessingStage.Error,
                Progress = ProcessingProgress.Factory.CreateError(filePath, ex.Message),
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }

        if (!validationResult.IsSuccess)
        {
            yield return validationResult;
            yield break;
        }
        yield return validationResult;

        // 5. Completion 단계
        ProcessingStepResult completionResult;
        try
        {
            completionResult = await ProcessCompletionStageAsync(chunks!, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Completion stage: {FilePath}", filePath);
            completionResult = new ProcessingStepResult
            {
                Stage = ProcessingStage.Error,
                Progress = ProcessingProgress.Factory.CreateError(filePath, ex.Message),
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }

        yield return completionResult;
    }

    private async Task<ProcessingStepResult> ProcessExtractionStageAsync(string filePath, CancellationToken cancellationToken)
    {
        var reader = _readerFactory.GetReader(filePath);
        if (reader == null)
        {
            return new ProcessingStepResult
            {
                Stage = ProcessingStage.Error,
                Progress = ProcessingProgress.Factory.CreateError(filePath, $"지원되지 않는 파일 형식: {Path.GetExtension(filePath)}"),
                IsSuccess = false
            };
        }

        var rawContent = await reader.ExtractAsync(filePath, cancellationToken);
        return new ProcessingStepResult
        {
            Stage = ProcessingStage.Extracting,
            Progress = ProcessingProgress.Factory.Create(filePath, ProcessingStage.Extracting, 1.0, "텍스트 추출 완료"),
            Data = rawContent,
            IsSuccess = true
        };
    }

    private async Task<ProcessingStepResult> ProcessParsingStageAsync(RawDocumentContent rawContent, DocumentParsingOptions parsingOptions, CancellationToken cancellationToken)
    {
        // 이전 단계에서 전달받은 RawDocumentContent를 사용

        // 실제 Parser 사용 - 파일명을 rawContent에서 가져오기
        var fileName = rawContent.FileInfo?.FileName ?? "unknown";
        var parser = _parserFactory.GetParser(fileName);
        ParsedDocumentContent parsedContent;

        if (parser != null && parsingOptions.UseAdvancedParsing)
        {
            // LLM 기반 파싱 사용
            parsedContent = await parser.ParseAsync(rawContent, parsingOptions, cancellationToken);
        }
        else
        {
            // 기본 파싱 사용 (LLM 없이)
            parsedContent = new ParsedDocumentContent
            {
                StructuredText = rawContent.Text,
                OriginalText = rawContent.Text,
                Metadata = new DocumentMetadata
                {
                    FileName = rawContent.FileInfo?.FileName ?? "Unknown",
                    ProcessedAt = DateTime.UtcNow
                },
                Structure = new FileFlux.Domain.DocumentStructure
                {
                    DocumentType = "General",
                    Sections = new List<DocumentSection>()
                },
                Quality = new QualityMetrics
                {
                    ConfidenceScore = 0.7,
                    CompletenessScore = 0.8,
                    ConsistencyScore = 0.7
                },
                ParsingInfo = new ParsingMetadata
                {
                    UsedLlm = false,
                    ParserType = "BasicParser"
                }
            };
        }

        return new ProcessingStepResult
        {
            Stage = ProcessingStage.Parsing,
            Progress = ProcessingProgress.Factory.Create(fileName, ProcessingStage.Parsing, 1.0, "파싱 완료"),
            Data = parsedContent,
            IsSuccess = true
        };
    }

    private async Task<ProcessingStepResult> ProcessChunkingStageAsync(ParsedDocumentContent parsedContent, ChunkingOptions chunkingOptions, CancellationToken cancellationToken)
    {
        // 실제 청킹 전략 사용
        var chunkingStrategy = _chunkingStrategyFactory.GetStrategy(chunkingOptions.Strategy);
        var fileName = parsedContent.Metadata?.FileName ?? "unknown";

        if (chunkingStrategy == null)
        {
            return new ProcessingStepResult
            {
                Stage = ProcessingStage.Error,
                Progress = ProcessingProgress.Factory.CreateError(fileName, $"지원되지 않는 청킹 전략: {chunkingOptions.Strategy}"),
                IsSuccess = false
            };
        }

        // ParsedDocumentContent를 DocumentContent로 변환
        var documentContent = new DocumentContent
        {
            Text = parsedContent.StructuredText ?? parsedContent.OriginalText,
            Metadata = parsedContent.Metadata ?? new DocumentMetadata
            {
                FileName = fileName,
                ProcessedAt = DateTime.UtcNow
            }
        };

        var chunks = await chunkingStrategy.ChunkAsync(documentContent, chunkingOptions, cancellationToken);
        var chunkArray = chunks.ToArray();

        return new ProcessingStepResult
        {
            Stage = ProcessingStage.Chunking,
            Progress = ProcessingProgress.Factory.Create(fileName, ProcessingStage.Chunking, 1.0, $"{chunkArray.Length}개 청크 생성 완료"),
            Data = chunkArray,
            IsSuccess = true
        };
    }

    private async Task<ProcessingStepResult> ProcessValidationStageAsync(DocumentChunk[] chunks, CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken); // 시뮬레이션

        var fileName = chunks.FirstOrDefault()?.Metadata?.FileName ?? "unknown";

        return new ProcessingStepResult
        {
            Stage = ProcessingStage.Validating,
            Progress = ProcessingProgress.Factory.Create(fileName, ProcessingStage.Validating, 1.0, "검증 완료"),
            IsSuccess = true
        };
    }

    private async Task<ProcessingStepResult> ProcessCompletionStageAsync(DocumentChunk[] chunks, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken); // 시뮬레이션

        var fileName = chunks.FirstOrDefault()?.Metadata?.FileName ?? "unknown";

        return new ProcessingStepResult
        {
            Stage = ProcessingStage.Completed,
            Progress = ProcessingProgress.Factory.Create(fileName, ProcessingStage.Completed, 1.0, "문서 처리 완료"),
            Data = chunks,
            IsSuccess = true
        };
    }
}