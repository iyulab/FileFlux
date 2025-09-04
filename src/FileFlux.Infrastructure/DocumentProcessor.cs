using FileFlux.Core;
using FileFlux.Core.Exceptions;
using FileFlux.Domain;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace FileFlux.Infrastructure;

/// <summary>
/// 문서 처리기 구현체 - 간결한 API 제공
/// </summary>
public class DocumentProcessor : IDocumentProcessor
{
    private readonly IDocumentReaderFactory _readerFactory;
    private readonly IDocumentParserFactory _parserFactory;
    private readonly IChunkingStrategyFactory _chunkingFactory;
    private readonly ILogger<DocumentProcessor>? _logger;

    public DocumentProcessor(
        IDocumentReaderFactory readerFactory,
        IDocumentParserFactory parserFactory,
        IChunkingStrategyFactory chunkingFactory,
        ILogger<DocumentProcessor>? logger = null)
    {
        _readerFactory = readerFactory ?? throw new ArgumentNullException(nameof(readerFactory));
        _parserFactory = parserFactory ?? throw new ArgumentNullException(nameof(parserFactory));
        _chunkingFactory = chunkingFactory ?? throw new ArgumentNullException(nameof(chunkingFactory));
        _logger = logger;
    }

    public async IAsyncEnumerable<DocumentChunk> ProcessAsync(
        string filePath,
        ChunkingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        // Step 1: Extract text
        var rawContent = await ExtractTextInternalAsync(filePath, cancellationToken);
        
        // Step 2: Parse document
        var parsedContent = await ParseAsync(rawContent, null, cancellationToken);
        
        // Step 3: Generate chunks
        var chunks = await ChunkAsync(parsedContent, options, cancellationToken);
        
        // Yield chunks
        foreach (var chunk in chunks)
        {
            yield return chunk;
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public async IAsyncEnumerable<DocumentChunk> ProcessAsync(
        Stream stream,
        string fileName,
        ChunkingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name cannot be null or empty", nameof(fileName));

        // Step 1: Extract text from stream
        var rawContent = await ExtractTextInternalAsync(stream, fileName, cancellationToken);
        
        // Step 2: Parse document
        var parsedContent = await ParseAsync(rawContent, null, cancellationToken);
        
        // Step 3: Generate chunks
        var chunks = await ChunkAsync(parsedContent, options, cancellationToken);
        
        // Yield chunks
        foreach (var chunk in chunks)
        {
            yield return chunk;
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public async IAsyncEnumerable<DocumentChunk> ProcessAsync(
        RawDocumentContent rawContent,
        ChunkingOptions? options = null,
        DocumentParsingOptions? parsingOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rawContent);

        // Step 1: Parse document
        var parsedContent = await ParseAsync(rawContent, parsingOptions, cancellationToken);
        
        // Step 2: Generate chunks
        var chunks = await ChunkAsync(parsedContent, options, cancellationToken);
        
        // Yield chunks
        foreach (var chunk in chunks)
        {
            yield return chunk;
            cancellationToken.ThrowIfCancellationRequested();
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

    public async Task<RawDocumentContent> ExtractAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        return await ExtractTextInternalAsync(filePath, cancellationToken);
    }

    /// <summary>
    /// 내부 텍스트 추출 메서드 (파일 경로)
    /// </summary>
    private async Task<RawDocumentContent> ExtractTextInternalAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
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

    /// <summary>
    /// 내부 텍스트 추출 메서드 (스트림)
    /// </summary>
    private async Task<RawDocumentContent> ExtractTextInternalAsync(
        Stream stream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var reader = _readerFactory.GetReader(fileName);
            if (reader == null)
                throw new UnsupportedFileFormatException(fileName, $"No suitable reader found for file: {fileName}");

            var rawContent = await reader.ExtractAsync(stream, fileName, cancellationToken);

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
            throw new DocumentProcessingException(fileName, $"Text extraction failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// ParsedDocumentContent를 기존 DocumentContent로 변환
    /// </summary>
    private static DocumentContent ConvertToDocumentContent(ParsedDocumentContent parsedContent)
    {
        return new DocumentContent
        {
            Text = parsedContent.StructuredText,
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
}