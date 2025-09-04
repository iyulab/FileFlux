using FileFlux.Core;
using FileFlux.Core.Exceptions;
using FileFlux.Domain;
using FileFlux.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace FileFlux.Infrastructure;

/// <summary>
/// 문서 처리기 구현체 - Reader/Parser 분리 아키텍처 + AsyncEnumerable 스트리밍
/// Reader(텍스트 추출) -> Parser(구조화) -> Chunking 파이프라인
/// </summary>
public class DocumentProcessor : IDocumentProcessor
{
    private readonly IDocumentReaderFactory _readerFactory;
    private readonly IDocumentParserFactory _parserFactory;
    private readonly IChunkingStrategyFactory _chunkingFactory;
    private readonly ILogger<DocumentProcessor>? _logger;
    private readonly TestResultsStorage? _resultsStorage;

    public DocumentProcessor(
        IDocumentReaderFactory readerFactory,
        IDocumentParserFactory parserFactory,
        IChunkingStrategyFactory chunkingFactory,
        ILogger<DocumentProcessor>? logger = null,
        TestResultsStorage? resultsStorage = null)
    {
        _readerFactory = readerFactory ?? throw new ArgumentNullException(nameof(readerFactory));
        _parserFactory = parserFactory ?? throw new ArgumentNullException(nameof(parserFactory));
        _chunkingFactory = chunkingFactory ?? throw new ArgumentNullException(nameof(chunkingFactory));
        _logger = logger;
        _resultsStorage = resultsStorage;
    }

    public async IAsyncEnumerable<ProcessingResult<DocumentChunk>> ProcessAsync(
        string filePath,
        ChunkingOptions? options = null,
        DocumentParsingOptions? parsingOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        var fileInfo = new FileInfo(filePath);
        const long LARGE_FILE_THRESHOLD = 50 * 1024 * 1024; // 50MB

        // 대용량 파일 최적화
        if (fileInfo.Length > LARGE_FILE_THRESHOLD)
        {
            options ??= new ChunkingOptions();
            if (options.MaxChunkSize > 1024)
            {
                options.MaxChunkSize = 512;
                _logger?.LogInformation("Adjusted chunk size to 512 for large file: {FilePath}", filePath);
            }
        }

        await foreach (var result in ProcessInternalAsync(filePath, null, options, parsingOptions, cancellationToken))
        {
            yield return result;
        }
    }

    public async IAsyncEnumerable<ProcessingResult<DocumentChunk>> ProcessAsync(
        Stream stream,
        string fileName,
        ChunkingOptions? options = null,
        DocumentParsingOptions? parsingOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name cannot be null or empty", nameof(fileName));

        // 스트림 크기 확인 및 최적화
        long streamSize = -1;
        try
        {
            if (stream.CanSeek)
            {
                streamSize = stream.Length;
                const long LARGE_STREAM_THRESHOLD = 50 * 1024 * 1024; // 50MB

                if (streamSize > LARGE_STREAM_THRESHOLD)
                {
                    options ??= new ChunkingOptions();
                    if (options.MaxChunkSize > 1024)
                    {
                        options.MaxChunkSize = 512;
                        _logger?.LogInformation("Adjusted chunk size to 512 for large stream: {FileName}", fileName);
                    }
                }
            }
        }
        catch
        {
            // Length 접근 불가능한 스트림의 경우 무시
        }

        await foreach (var result in ProcessInternalAsync(null, stream, options, parsingOptions, cancellationToken, fileName))
        {
            yield return result;
        }
    }

    private async IAsyncEnumerable<ProcessingResult<DocumentChunk>> ProcessInternalAsync(
        string? filePath,
        Stream? stream,
        ChunkingOptions? options,
        DocumentParsingOptions? parsingOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        string? fileName = null)
    {
        var progress = new ProcessingProgress();
        var targetName = filePath ?? fileName ?? "unknown";

        // Step 1: Extract raw text (Reader)
        progress.Stage = ProcessingStage.Extracting;
        progress.Message = "Extracting text from document...";
        progress.StageProgress = 0.0;
        progress.OverallProgress = 0.0;
        yield return ProcessingResult<DocumentChunk>.InProgress(progress);

        var extractionResult = await TryExtractTextAsync(filePath, stream, fileName, targetName, cancellationToken);
        if (!extractionResult.IsSuccess)
        {
            yield return ProcessingResult<DocumentChunk>.Error(extractionResult.ErrorMessage!, progress);
            yield break;
        }

        var rawContent = extractionResult.Content!;
        progress.StageProgress = 1.0;
        progress.OverallProgress = 0.33;

        // Save extraction results if storage is configured
        if (_resultsStorage != null)
        {
            try
            {
                await _resultsStorage.SaveExtractionResultAsync(targetName, rawContent);
                _logger?.LogInformation("Extraction results saved for: {FileName}", targetName);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to save extraction results for: {FileName}", targetName);
            }
        }

        if (rawContent.Text.Length > 100_000)
        {
            _logger?.LogWarning("Large text content detected ({Length:N0} characters). Processing may be memory intensive.", rawContent.Text.Length);
        }

        // Step 2: Parse and structure (Parser)
        progress.Stage = ProcessingStage.Parsing;
        progress.Message = "Parsing document structure...";
        progress.StageProgress = 0.0;
        progress.OverallProgress = 0.33;
        var parsingProgress = ProcessingResult<DocumentChunk>.InProgress(progress);
        parsingProgress.RawContent = rawContent;
        yield return parsingProgress;

        var parsingResult = await TryParseAsync(rawContent, parsingOptions, targetName, cancellationToken);
        if (!parsingResult.IsSuccess)
        {
            yield return ProcessingResult<DocumentChunk>.Error(parsingResult.ErrorMessage!, progress);
            yield break;
        }

        var parsedContent = parsingResult.Content!;
        progress.StageProgress = 1.0;
        progress.OverallProgress = 0.66;

        // Save parsing results if storage is configured
        if (_resultsStorage != null)
        {
            try
            {
                await _resultsStorage.SaveParsingResultAsync(targetName, parsedContent);
                _logger?.LogInformation("Parsing results saved for: {FileName}", targetName);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to save parsing results for: {FileName}", targetName);
            }
        }

        // Step 3: Generate chunks (Chunking Strategy)
        progress.Stage = ProcessingStage.Chunking;
        progress.Message = "Generating chunks...";
        progress.StageProgress = 0.0;
        progress.OverallProgress = 0.66;
        var chunkingProgress = ProcessingResult<DocumentChunk>.InProgress(progress);
        chunkingProgress.RawContent = rawContent;
        chunkingProgress.ParsedContent = parsedContent;
        yield return chunkingProgress;

        var chunkingResult = await TryChunkAsync(parsedContent, options, targetName, cancellationToken);
        if (!chunkingResult.IsSuccess)
        {
            yield return ProcessingResult<DocumentChunk>.Error(chunkingResult.ErrorMessage!, progress);
            yield break;
        }

        var chunks = chunkingResult.Content!;

        // Save chunking results if storage is configured
        if (_resultsStorage != null)
        {
            try
            {
                await _resultsStorage.SaveChunkingResultsAsync(targetName, chunks, options ?? new ChunkingOptions());
                _logger?.LogInformation("Chunking results saved for: {FileName}", targetName);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to save chunking results for: {FileName}", targetName);
            }
        }

        // Yield chunks progressively with real-time progress
        var totalChunks = chunks.Length;
        for (int i = 0; i < totalChunks; i++)
        {
            progress.StageProgress = (double)(i + 1) / totalChunks;
            progress.OverallProgress = 0.66 + (progress.StageProgress * 0.34);
            progress.Message = $"Generated chunk {i + 1}/{totalChunks}";

            var result = ProcessingResult<DocumentChunk>.Success(chunks[i], progress);
            // Set intermediate results for debugging and inspection
            result.RawContent = rawContent;
            result.ParsedContent = parsedContent;

            yield return result;

            // Allow cancellation between chunks for responsiveness
            cancellationToken.ThrowIfCancellationRequested();
        }

        // Final completion
        progress.Stage = ProcessingStage.Completed;
        progress.OverallProgress = 1.0;
        progress.StageProgress = 1.0;
        progress.Message = $"Processing completed. Generated {totalChunks} chunks";
        yield return ProcessingResult<DocumentChunk>.InProgress(progress);
    }

    private async Task<OperationResult<RawDocumentContent>> TryExtractTextAsync(
        string? filePath,
        Stream? stream,
        string? fileName,
        string targetName,
        CancellationToken cancellationToken)
    {
        try
        {
            RawDocumentContent rawContent;
            if (filePath != null)
            {
                rawContent = await ExtractTextAsync(filePath, cancellationToken);
            }
            else if (stream != null && fileName != null)
            {
                var reader = _readerFactory.GetReader(fileName);
                if (reader == null)
                    throw new UnsupportedFileFormatException(fileName, $"No suitable reader found for file: {fileName}");
                rawContent = await reader.ExtractAsync(stream, fileName, cancellationToken);
            }
            else
            {
                throw new ArgumentException("Either filePath or stream+fileName must be provided");
            }
            return OperationResult<RawDocumentContent>.Success(rawContent);
        }
        catch (OutOfMemoryException ex)
        {
            _logger?.LogError(ex, "Out of memory during text extraction: {TargetName}", targetName);
            return OperationResult<RawDocumentContent>.Failure("Document too large for current memory allocation.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to extract text from document: {TargetName}", targetName);
            return OperationResult<RawDocumentContent>.Failure($"Text extraction failed: {ex.Message}");
        }
    }

    private async Task<OperationResult<ParsedDocumentContent>> TryParseAsync(
        RawDocumentContent rawContent,
        DocumentParsingOptions? parsingOptions,
        string targetName,
        CancellationToken cancellationToken)
    {
        try
        {
            var parsedContent = await ParseAsync(rawContent, parsingOptions, cancellationToken);
            return OperationResult<ParsedDocumentContent>.Success(parsedContent);
        }
        catch (OutOfMemoryException ex)
        {
            _logger?.LogError(ex, "Out of memory during parsing: {TargetName}", targetName);
            return OperationResult<ParsedDocumentContent>.Failure("Document too large for current memory allocation.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to parse document: {TargetName}", targetName);
            return OperationResult<ParsedDocumentContent>.Failure($"Document parsing failed: {ex.Message}");
        }
    }

    private async Task<OperationResult<DocumentChunk[]>> TryChunkAsync(
        ParsedDocumentContent parsedContent,
        ChunkingOptions? options,
        string targetName,
        CancellationToken cancellationToken)
    {
        try
        {
            var chunks = await ChunkAsync(parsedContent, options, cancellationToken);
            return OperationResult<DocumentChunk[]>.Success(chunks);
        }
        catch (OutOfMemoryException ex)
        {
            _logger?.LogError(ex, "Out of memory during chunking: {TargetName}", targetName);
            return OperationResult<DocumentChunk[]>.Failure("Document too large for current memory allocation.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to chunk document: {TargetName}", targetName);
            return OperationResult<DocumentChunk[]>.Failure($"Document chunking failed: {ex.Message}");
        }
    }

    public async Task<RawDocumentContent> ExtractTextAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        _logger?.LogDebug("Starting text extraction for: {FilePath}", filePath);

        try
        {
            // 적절한 Reader 선택
            var reader = _readerFactory.GetReader(filePath);
            if (reader == null)
                throw new UnsupportedFileFormatException(filePath, $"No suitable reader found for file: {filePath}");

            _logger?.LogDebug("Using reader: {ReaderType}", reader.ReaderType);

            // 텍스트 추출
            var rawContent = await reader.ExtractAsync(filePath, cancellationToken);

            // 추출 경고 로깅
            if (rawContent.ExtractionWarnings.Count != 0)
            {
                foreach (var warning in rawContent.ExtractionWarnings)
                {
                    _logger?.LogWarning("Extraction warning: {Warning}", warning);
                }
            }

            return rawContent;
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            throw new DocumentProcessingException(filePath, $"Text extraction failed: {ex.Message}", ex);
        }
    }

    public async Task<ParsedDocumentContent> ParseAsync(
        RawDocumentContent rawContent,
        DocumentParsingOptions? parsingOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rawContent);

        parsingOptions ??= new DocumentParsingOptions();

        _logger?.LogDebug("Starting document parsing. UseAdvancedParsing: {UseAdvancedParsing}, Level: {Level}",
            parsingOptions.UseAdvancedParsing, parsingOptions.StructuringLevel);

        try
        {
            // 적절한 Parser 선택
            var parser = _parserFactory.GetParser(rawContent);
            _logger?.LogDebug("Using parser: {ParserType}", parser.ParserType);

            // 문서 구조화
            var parsedContent = await parser.ParseAsync(rawContent, parsingOptions, cancellationToken);

            // 파싱 경고 로깅
            if (parsedContent.ParsingInfo.Warnings.Count != 0)
            {
                foreach (var warning in parsedContent.ParsingInfo.Warnings)
                {
                    _logger?.LogWarning("Parsing warning: {Warning}", warning);
                }
            }

            return parsedContent;
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            throw new DocumentProcessingException(rawContent.FileInfo.FileName, $"Document parsing failed: {ex.Message}", ex);
        }
    }

    public async Task<DocumentChunk[]> ChunkAsync(
        ParsedDocumentContent parsedContent,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsedContent);

        options ??= new ChunkingOptions();

        _logger?.LogDebug("Starting chunking. Strategy: {Strategy}, MaxSize: {MaxSize}, Overlap: {Overlap}",
            options.Strategy, options.MaxChunkSize, options.OverlapSize);

        try
        {
            // 청킹 전략 선택
            var strategy = _chunkingFactory.GetStrategy(options.Strategy);
            if (strategy == null)
                throw new InvalidOperationException($"Chunking strategy '{options.Strategy}' not found");

            _logger?.LogDebug("Using chunking strategy: {StrategyName}", strategy.StrategyName);

            // ParsedDocumentContent를 기존 DocumentContent로 변환
            var documentContent = ConvertToDocumentContent(parsedContent);

            // 청킹 실행
            var chunks = await strategy.ChunkAsync(documentContent, options, cancellationToken);

            return chunks.ToArray();
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            throw new DocumentProcessingException(parsedContent.Metadata.FileName, $"Document chunking failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// ParsedDocumentContent를 기존 DocumentContent로 변환
    /// 기존 ChunkingStrategy와의 호환성을 위해 필요
    /// </summary>
    private static DocumentContent ConvertToDocumentContent(ParsedDocumentContent parsedContent)
    {
        return new DocumentContent
        {
            Text = parsedContent.StructuredText, // 구조화된 텍스트 사용
            Metadata = parsedContent.Metadata,
            StructureInfo = new Dictionary<string, object>
            {
                ["DocumentType"] = parsedContent.Structure.DocumentType,
                ["Topic"] = parsedContent.Structure.Topic,
                ["SectionCount"] = parsedContent.Structure.Sections.Count,
                ["Keywords"] = string.Join(", ", parsedContent.Structure.Keywords),
                ["Summary"] = parsedContent.Structure.Summary,
                ["QualityScore"] = parsedContent.Quality.OverallScore,
                ["StructureConfidence"] = parsedContent.Quality.StructureConfidence,
                ["ParsingDuration"] = parsedContent.ParsingInfo.Duration.TotalMilliseconds,
                ["UsedLlm"] = parsedContent.ParsingInfo.UsedLlm
            }
        };
    }

    /// <summary>
    /// 편의 메서드: 청크만 반환하는 스트리밍 처리 (진행률 정보 제외)
    /// </summary>
    public async IAsyncEnumerable<DocumentChunk> ProcessAsStreamAsync(
        string filePath,
        ChunkingOptions? options = null,
        DocumentParsingOptions? parsingOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var result in ProcessAsync(filePath, options, parsingOptions, cancellationToken))
        {
            if (result.IsSuccess && result.Result != null)
            {
                yield return result.Result;
            }
        }
    }

    /// <summary>
    /// 편의 메서드: 스트림에서 청크만 반환하는 스트리밍 처리 (진행률 정보 제외)
    /// </summary>
    public async IAsyncEnumerable<DocumentChunk> ProcessAsStreamAsync(
        Stream stream,
        string fileName,
        ChunkingOptions? options = null,
        DocumentParsingOptions? parsingOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var result in ProcessAsync(stream, fileName, options, parsingOptions, cancellationToken))
        {
            if (result.IsSuccess && result.Result != null)
            {
                yield return result.Result;
            }
        }
    }

    /// <summary>
    /// 성능 최적화: 배치 처리를 위한 메서드 (IDocumentProcessor 인터페이스의 기본 구현 활용)
    /// </summary>
    public async Task<DocumentChunk[]> ProcessToArrayAsync(
        string filePath,
        ChunkingOptions? options = null,
        DocumentParsingOptions? parsingOptions = null,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<DocumentChunk>();
        await foreach (var result in ProcessAsync(filePath, options, parsingOptions, cancellationToken))
        {
            if (result.IsSuccess && result.Result != null)
            {
                chunks.Add(result.Result);
            }
        }
        return chunks.ToArray();
    }

    /// <summary>
    /// 성능 최적화: 스트림에서 배치 처리를 위한 메서드 (IDocumentProcessor 인터페이스의 기본 구현 활용)
    /// </summary>
    public async Task<DocumentChunk[]> ProcessToArrayAsync(
        Stream stream,
        string fileName,
        ChunkingOptions? options = null,
        DocumentParsingOptions? parsingOptions = null,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<DocumentChunk>();
        await foreach (var result in ProcessAsync(stream, fileName, options, parsingOptions, cancellationToken))
        {
            if (result.IsSuccess && result.Result != null)
            {
                chunks.Add(result.Result);
            }
        }
        return chunks.ToArray();
    }
}