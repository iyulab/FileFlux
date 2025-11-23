using FileFlux;
using FileFlux.Exceptions;
using FileFlux.Domain;
using FileFlux.Core;
using FileFlux.Infrastructure.Quality;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace FileFlux.Infrastructure;

/// <summary>
/// 문서 처리기 구현체 - 간결한 API 제공
/// </summary>
public partial class DocumentProcessor : IDocumentProcessor
{
    private readonly IDocumentReaderFactory _readerFactory;
    private readonly IDocumentParserFactory _parserFactory;
    private readonly IChunkingStrategyFactory _chunkingFactory;
    private readonly ILogger<DocumentProcessor> _logger;
    private readonly Lazy<DocumentQualityAnalyzer> _qualityAnalyzer;
    private readonly IMetadataEnricher? _metadataEnricher;

    public DocumentProcessor(
        IDocumentReaderFactory readerFactory,
        IDocumentParserFactory parserFactory,
        IChunkingStrategyFactory chunkingFactory,
        IMetadataEnricher? metadataEnricher = null,
        ILogger<DocumentProcessor>? logger = null)
    {
        _readerFactory = readerFactory ?? throw new ArgumentNullException(nameof(readerFactory));
        _parserFactory = parserFactory ?? throw new ArgumentNullException(nameof(parserFactory));
        _chunkingFactory = chunkingFactory ?? throw new ArgumentNullException(nameof(chunkingFactory));
        _metadataEnricher = metadataEnricher; // Optional
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DocumentProcessor>.Instance;

        // Lazy initialization to avoid circular dependencies
        _qualityAnalyzer = new Lazy<DocumentQualityAnalyzer>(() =>
            new DocumentQualityAnalyzer(new ChunkQualityEngine(), this));
    }

    public async Task<DocumentChunk[]> ProcessAsync(
        string filePath,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        // Step 1: Extract text
        var rawContent = await ExtractTextInternalAsync(filePath, cancellationToken);

        // Step 1.5: Enrich metadata if enabled (Phase 16)
        await EnrichMetadataIfEnabledAsync(filePath, rawContent, options, cancellationToken);

        // Step 2: Parse document
        var parsedContent = await ParseAsync(rawContent, (DocumentParsingOptions?)null, cancellationToken);

        // Step 3: Generate chunks (with raw content for page ranges)
        var chunks = await ChunkAsync(parsedContent, rawContent, options, cancellationToken);

        return chunks;
    }

    public async Task<DocumentChunk[]> ProcessAsync(
        Stream stream,
        string fileName,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name cannot be null or empty", nameof(fileName));

        // Step 1: Extract text from stream
        var rawContent = await ExtractTextInternalAsync(stream, fileName, cancellationToken);

        // Step 2: Parse document
        var parsedContent = await ParseAsync(rawContent, (DocumentParsingOptions?)null, cancellationToken);

        // Step 3: Generate chunks (with raw content for page ranges)
        var chunks = await ChunkAsync(parsedContent, rawContent, options, cancellationToken);

        return chunks;
    }

    public async Task<DocumentChunk[]> ProcessAsync(
        RawContent rawContent,
        ChunkingOptions? options = null,
        DocumentParsingOptions? parsingOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rawContent);

        // Step 1: Parse document
        var parsedContent = await ParseAsync(rawContent, parsingOptions, cancellationToken);

        // Step 2: Generate chunks (with raw content for page ranges)
        var chunks = await ChunkAsync(parsedContent, rawContent, options, cancellationToken);

        return chunks;
    }

    public async Task<ParsedContent> ParseAsync(
        RawContent rawContent,
        DocumentParsingOptions? parsingOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rawContent);

        parsingOptions ??= new DocumentParsingOptions();

        _logger.LogDebug("Starting document parsing. UseLlmParsing: {UseLlmParsing}, Level: {Level}",
            parsingOptions.UseLlmParsing, parsingOptions.StructuringLevel);

        try
        {
            // 적절한 Parser 선택
            var parser = _parserFactory.GetParser(rawContent);
            _logger.LogDebug("Using parser: {ParserType}", parser.ParserType);

            // 문서 구조화
            var parsedContent = await parser.ParseAsync(rawContent, parsingOptions, cancellationToken);

            // 파싱 경고 로깅
            if (parsedContent.Info.Warnings.Count != 0)
            {
                foreach (var warning in parsedContent.Info.Warnings)
                {
                    _logger.LogWarning("Parsing warning: {Warning}", warning);
                }
            }

            return parsedContent;
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            throw new DocumentProcessingException(rawContent.File.Name, $"Document parsing failed: {ex.Message}", ex);
        }
    }

    public async Task<DocumentChunk[]> ChunkAsync(
        ParsedContent parsedContent,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await ChunkAsync(parsedContent, null, options, cancellationToken);
    }

    public async Task<DocumentChunk[]> ChunkAsync(
        ParsedContent parsedContent,
        RawContent? rawContent,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsedContent);

        options ??= new ChunkingOptions();

        _logger.LogDebug("Starting chunking. Strategy: {Strategy}, MaxSize: {MaxSize}, Overlap: {Overlap}",
            options.Strategy, options.MaxChunkSize, options.OverlapSize);

        try
        {
            // 청킹 전략 선택
            var strategy = _chunkingFactory.GetStrategy(options.Strategy);
            if (strategy == null)
                throw new InvalidOperationException($"Chunking strategy '{options.Strategy}' not found");

            _logger.LogDebug("Using chunking strategy: {StrategyName}", strategy.StrategyName);

            // ParsedContent를 기존 DocumentContent로 변환 (with page ranges from raw content)
            var documentContent = ConvertToDocumentContent(parsedContent, rawContent);

            // 청킹 실행
            var chunks = await strategy.ChunkAsync(documentContent, options, cancellationToken);

            return chunks.ToArray();
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            throw new DocumentProcessingException(parsedContent.Metadata.FileName, $"Document chunking failed: {ex.Message}", ex);
        }
    }

    public async Task<RawContent> ExtractAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        return await ExtractTextInternalAsync(filePath, cancellationToken);
    }

    /// <summary>
    /// 내부 텍스트 추출 메서드 (파일 경로)
    /// </summary>
    private async Task<RawContent> ExtractTextInternalAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting text extraction for: {FilePath}", filePath);

        try
        {
            // 적절한 Reader 선택
            var reader = _readerFactory.GetReader(filePath);
            if (reader == null)
                throw new UnsupportedFileFormatException(filePath, $"No suitable reader found for file: {filePath}");

            _logger.LogDebug("Using reader: {ReaderType}", reader.ReaderType);

            // 텍스트 추출
            var rawContent = await reader.ExtractAsync(filePath, cancellationToken);

            // 추출 경고 로깅
            if (rawContent.Warnings.Count != 0)
            {
                foreach (var warning in rawContent.Warnings)
                {
                    _logger.LogWarning("Extraction warning: {Warning}", warning);
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
    private async Task<RawContent> ExtractTextInternalAsync(
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

            if (rawContent.Warnings.Count != 0)
            {
                foreach (var warning in rawContent.Warnings)
                {
                    _logger.LogWarning("Extraction warning: {Warning}", warning);
                }
            }

            return rawContent;
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            throw new DocumentProcessingException(fileName, $"Text extraction failed: {ex.Message}", ex);
        }
    }

    // RAG Quality Analysis Methods - Phase 6.5 Enhancement

    /// <summary>
    /// 문서 처리 품질을 분석하여 RAG 시스템 최적화를 위한 리포트 생성
    /// 내부 벤치마킹과 동일한 로직을 사용하여 일관성 보장
    /// </summary>
    public async Task<DocumentQualityReport> AnalyzeQualityAsync(
        string filePath,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting quality analysis for: {FilePath}", filePath);

        try
        {
            return await _qualityAnalyzer.Value.AnalyzeQualityAsync(filePath, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            throw new DocumentProcessingException(filePath, $"Quality analysis failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 문서 기반 QA 벤치마크 데이터셋 생성
    /// RAG 시스템 성능 측정 및 청크 답변 가능성 평가에 필수
    /// </summary>
    public async Task<QABenchmark> GenerateQAAsync(
        string filePath,
        int questionCount = 20,
        QABenchmark? existingQA = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting QA generation for: {FilePath}, QuestionCount: {QuestionCount}", filePath, questionCount);

        try
        {
            var newQABenchmark = await _qualityAnalyzer.Value.GenerateQABenchmarkAsync(filePath, questionCount, cancellationToken).ConfigureAwait(false);

            // Merge with existing QA if provided
            if (existingQA != null)
            {
                _logger.LogDebug("Merging with existing QA benchmark containing {ExistingCount} questions", existingQA.Questions.Count);
                return QABenchmark.Merge(existingQA, newQABenchmark);
            }

            return newQABenchmark;
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            throw new DocumentProcessingException(filePath, $"QA generation failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// ParsedContent를 기존 DocumentContent로 변환
    /// </summary>
    private static DocumentContent ConvertToDocumentContent(ParsedContent parsedContent, RawContent? rawContent = null)
    {
        var documentContent = new DocumentContent
        {
            Text = parsedContent.Text,
            Metadata = parsedContent.Metadata,
            StructureInfo = new Dictionary<string, object>
            {
                ["DocumentType"] = parsedContent.Structure.Type,
                ["Topic"] = parsedContent.Structure.Topic,
                ["SectionCount"] = parsedContent.Structure.Sections.Count,
                ["Keywords"] = string.Join(", ", parsedContent.Structure.Keywords),
                ["Summary"] = parsedContent.Structure.Summary,
                ["QualityScore"] = parsedContent.Quality.OverallScore,
                ["StructureConfidence"] = parsedContent.Quality.StructureScore,
                ["ParsingDuration"] = parsedContent.Duration.TotalMilliseconds,
                ["UsedLlm"] = parsedContent.Info.UsedLlm
            },
            // Convert Section to ContentSection for HeadingPath support
            Sections = parsedContent.Structure.Sections
                .Select(s => ConvertToContentSection(s))
                .ToList()
        };

        // Extract page ranges from raw content hints (PDF documents)
        if (rawContent?.Hints.TryGetValue("PageRanges", out var pageRangesObj) == true &&
            pageRangesObj is Dictionary<int, (int Start, int End)> pageRanges)
        {
            documentContent.PageRanges = pageRanges;
        }

        return documentContent;
    }

    /// <summary>
    /// Convert parsed Section to ContentSection for chunking
    /// </summary>
    private static ContentSection ConvertToContentSection(Section section)
    {
        return new ContentSection
        {
            Title = section.Title,
            Level = section.Level,
            StartPosition = section.Start,
            EndPosition = section.End,
            Children = section.Children.Select(ConvertToContentSection).ToList()
        };
    }

    /// <summary>
    /// Enrich metadata if enabled in options (Phase 16)
    /// </summary>
    private async Task EnrichMetadataIfEnabledAsync(
        string filePath,
        RawContent rawContent,
        ChunkingOptions? options,
        CancellationToken cancellationToken)
    {
        if (options == null || _metadataEnricher == null)
            return;

        // Check if metadata enrichment is enabled
        if (!options.CustomProperties.TryGetValue("enableMetadataEnrichment", out var enabledObj) ||
            enabledObj is not bool enabled || !enabled)
        {
            return;
        }

        _logger.LogInformation("Metadata enrichment enabled for {FileName}", rawContent.File.Name);

        try
        {
            // Get schema (default: General)
            var schema = Core.MetadataSchema.General;
            if (options.CustomProperties.TryGetValue("metadataSchema", out var schemaObj) &&
                schemaObj is Core.MetadataSchema schemaValue)
            {
                schema = schemaValue;
            }

            // Get enrichment options
            Core.MetadataEnrichmentOptions? enrichmentOptions = null;
            if (options.CustomProperties.TryGetValue("metadataOptions", out var optionsObj) &&
                optionsObj is Core.MetadataEnrichmentOptions opts)
            {
                enrichmentOptions = opts;
            }

            // Generate cache key
            var cacheKey = _metadataEnricher.GenerateCacheKey(filePath, schema);

            // Extract metadata with caching
            var enrichedMetadata = await _metadataEnricher.EnrichWithCacheAsync(
                content: rawContent.Text,
                cacheKey: cacheKey,
                schema: schema,
                options: enrichmentOptions,
                cancellationToken: cancellationToken);

            // Store enriched metadata in ChunkingOptions.CustomProperties for later use
            foreach (var (key, value) in enrichedMetadata)
            {
                options.CustomProperties[$"enriched_{key}"] = value;
            }

            _logger.LogInformation(
                "Metadata enrichment completed: {Count} fields, confidence: {Confidence}, method: {Method}",
                enrichedMetadata.Count,
                enrichedMetadata.TryGetValue("confidence", out var confVal) ? confVal : 0.0,
                enrichedMetadata.TryGetValue("extractionMethod", out var methodVal) ? methodVal : "unknown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Metadata enrichment failed for {FileName}", rawContent.File.Name);

            // Check if we should continue on failure (default: true)
            var continueOnFailure = true;
            if (options.CustomProperties.TryGetValue("metadataOptions", out var optionsObj) &&
                optionsObj is Core.MetadataEnrichmentOptions opts)
            {
                continueOnFailure = opts.ContinueOnEnrichmentFailure;
            }

            if (!continueOnFailure)
            {
                throw;
            }

            // Store error in CustomProperties
            options.CustomProperties["enriched_error"] = ex.Message;
        }
    }
}
