using FileFlux.Core;
using FileFlux.Infrastructure.Adapters;
using FluxCurator.Core.Core;
using FluxImprover;
using FluxImprover.ContextualRetrieval;
using FluxImprover.Enrichment;
using FluxImprover.Models;
using Microsoft.Extensions.Logging;
using FluxCuratorStrategy = FluxCurator.Core.Domain.ChunkingStrategy;
using FluxCuratorChunkOptions = FluxCurator.Core.Domain.ChunkOptions;

namespace FileFlux.Infrastructure;

/// <summary>
/// Document processor that delegates chunking to FluxCurator and enhancement to FluxImprover.
/// Provides a unified API for the complete document processing pipeline.
/// </summary>
public sealed partial class FluxDocumentProcessor : IDocumentProcessor
{
    private readonly IDocumentReaderFactory _readerFactory;
    private readonly IDocumentParserFactory _parserFactory;
    private readonly IChunkerFactory _chunkerFactory;
    private readonly FluxImproverServices? _improverServices;
    private readonly ILogger<FluxDocumentProcessor> _logger;

    /// <summary>
    /// Creates a new FluxDocumentProcessor with required dependencies.
    /// </summary>
    /// <param name="readerFactory">Factory for document readers (from FileFlux.Core)</param>
    /// <param name="parserFactory">Factory for document parsers</param>
    /// <param name="chunkerFactory">Factory for chunkers (from FluxCurator)</param>
    /// <param name="improverServices">FluxImprover services for enhancement (optional)</param>
    /// <param name="logger">Logger instance</param>
    public FluxDocumentProcessor(
        IDocumentReaderFactory readerFactory,
        IDocumentParserFactory parserFactory,
        IChunkerFactory chunkerFactory,
        FluxImproverServices? improverServices = null,
        ILogger<FluxDocumentProcessor>? logger = null)
    {
        _readerFactory = readerFactory ?? throw new ArgumentNullException(nameof(readerFactory));
        _parserFactory = parserFactory ?? throw new ArgumentNullException(nameof(parserFactory));
        _chunkerFactory = chunkerFactory ?? throw new ArgumentNullException(nameof(chunkerFactory));
        _improverServices = improverServices;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FluxDocumentProcessor>.Instance;
    }

    #region Full Pipeline

    /// <inheritdoc/>
    public async Task<DocumentChunk[]> ProcessAsync(
        string filePath,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFilePath(filePath);
        options ??= new ChunkingOptions();

        _logger.LogDebug("Processing file: {FilePath}", filePath);

        // Stage 1: Extract (FileFlux.Core)
        var rawContent = await ExtractAsync(filePath, cancellationToken).ConfigureAwait(false);

        // Stage 2: Parse
        var parsedContent = await ParseAsync(rawContent, null, cancellationToken).ConfigureAwait(false);

        // Stage 3: Chunk (FluxCurator)
        var chunks = await ChunkAsync(parsedContent, options, cancellationToken).ConfigureAwait(false);

        // Stage 4: Enhance (FluxImprover) - if available and enabled
        if (_improverServices != null && ShouldEnhance(options))
        {
            chunks = await EnhanceChunksAsync(chunks, rawContent.Text, options, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Processed {FileName}: {ChunkCount} chunks", rawContent.File.Name, chunks.Length);
        return chunks;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<DocumentChunk> ProcessStreamAsync(
        string filePath,
        ChunkingOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chunks = await ProcessAsync(filePath, options, cancellationToken).ConfigureAwait(false);
        foreach (var chunk in chunks)
        {
            yield return chunk;
        }
    }

    #endregion

    #region Stage 1: Extract (FileFlux.Core)

    /// <inheritdoc/>
    public async Task<RawContent> ExtractAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ValidateFilePath(filePath);

        _logger.LogDebug("Extracting content from: {FilePath}", filePath);

        try
        {
            var reader = _readerFactory.GetReader(filePath)
                ?? throw new UnsupportedFileFormatException(filePath, $"No reader found for: {filePath}");

            var rawContent = await reader.ExtractAsync(filePath, cancellationToken).ConfigureAwait(false);

            LogWarnings("Extraction", rawContent.Warnings);
            return rawContent;
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Extraction failed: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<RawContent> ExtractStreamAsync(
        string filePath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return await ExtractAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Stage 2: Parse

    /// <inheritdoc/>
    public async Task<ParsedContent> ParseAsync(
        RawContent raw,
        ParsingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(raw);
        options ??= new ParsingOptions();

        _logger.LogDebug("Parsing document: {FileName}", raw.File.Name);

        try
        {
            var parser = _parserFactory.GetParser(raw);
            var parsed = await parser.ParseAsync(raw, new DocumentParsingOptions
            {
                UseLlmParsing = options.UseLlm,
                StructuringLevel = StructuringLevel.Medium
            }, cancellationToken).ConfigureAwait(false);

            LogWarnings("Parsing", parsed.Info.Warnings);
            return parsed;
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(raw.File.Name, $"Parsing failed: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ParsedContent> ParseStreamAsync(
        RawContent raw,
        ParsingOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return await ParseAsync(raw, options, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Stage 3: Chunk (FluxCurator delegation)

    /// <inheritdoc/>
    public async Task<DocumentChunk[]> ChunkAsync(
        ParsedContent parsed,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        options ??= new ChunkingOptions();

        if (string.IsNullOrWhiteSpace(parsed.Text))
            return [];

        _logger.LogDebug("Chunking with strategy: {Strategy}", options.Strategy);

        try
        {
            // Map FileFlux strategy to FluxCurator strategy
            var fcStrategy = MapToFluxCuratorStrategy(options.Strategy);
            var chunker = _chunkerFactory.CreateChunker(fcStrategy);

            // Convert options
            var fcOptions = new FluxCuratorChunkOptions
            {
                MaxChunkSize = options.MaxChunkSize,
                MinChunkSize = options.MinChunkSize,
                OverlapSize = options.OverlapSize,
                TargetChunkSize = options.MaxChunkSize / 2,
                LanguageCode = options.LanguageCode == "auto" ? null : options.LanguageCode,
                PreserveParagraphs = options.PreserveParagraphs,
                PreserveSentences = options.PreserveSentences,
                PreserveSectionHeaders = true,
                IncludeMetadata = true,
                TrimWhitespace = true
            };

            // Execute chunking via FluxCurator
            var fcChunks = await chunker.ChunkAsync(parsed.Text, fcOptions, cancellationToken).ConfigureAwait(false);

            // Convert to FileFlux chunks
            var parsedId = Guid.NewGuid();
            var rawId = Guid.NewGuid();
            var chunks = fcChunks.ToFileFluxChunks(parsedId, rawId);

            // Enrich with document metadata
            EnrichChunksWithMetadata(chunks, parsed);

            _logger.LogDebug("Created {Count} chunks using {Strategy}", chunks.Count, chunker.StrategyName);
            return [.. chunks];
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(parsed.Metadata.FileName, $"Chunking failed: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<DocumentChunk> ChunkStreamAsync(
        ParsedContent parsed,
        ChunkingOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chunks = await ChunkAsync(parsed, options, cancellationToken).ConfigureAwait(false);
        foreach (var chunk in chunks)
        {
            yield return chunk;
        }
    }

    #endregion

    #region Stage 4: Enhance (FluxImprover delegation)

    /// <summary>
    /// Enhance chunks using FluxImprover services.
    /// </summary>
    private async Task<DocumentChunk[]> EnhanceChunksAsync(
        DocumentChunk[] chunks,
        string fullDocumentText,
        ChunkingOptions options,
        CancellationToken cancellationToken)
    {
        if (_improverServices == null || chunks.Length == 0)
            return chunks;

        _logger.LogDebug("Enhancing {Count} chunks with FluxImprover", chunks.Length);

        try
        {
            // Convert to FluxImprover Chunk format
            var improverChunks = chunks.Select(c => new Chunk
            {
                Id = c.Id.ToString(),
                Content = c.Content,
                Metadata = c.Props
            }).ToList();

            // Apply contextual enrichment (Anthropic pattern)
            if (ShouldUseContextualRetrieval(options))
            {
                var contextualChunks = await _improverServices.ContextualEnrichment
                    .EnrichBatchAsync(improverChunks, fullDocumentText, null, cancellationToken)
                    .ConfigureAwait(false);

                // Merge contextual info back to chunks
                for (int i = 0; i < chunks.Length && i < contextualChunks.Count; i++)
                {
                    if (contextualChunks[i].ContextSummary != null)
                        chunks[i].Props["contextual_summary"] = contextualChunks[i].ContextSummary;
                    chunks[i].Props["contextual_text"] = contextualChunks[i].GetContextualizedText();
                }
            }

            // Apply keyword extraction and summarization
            if (ShouldEnrichWithKeywords(options))
            {
                var enrichedChunks = await _improverServices.ChunkEnrichment
                    .EnrichBatchAsync(improverChunks, null, cancellationToken)
                    .ConfigureAwait(false);

                for (int i = 0; i < chunks.Length && i < enrichedChunks.Count; i++)
                {
                    if (enrichedChunks[i].Summary != null)
                        chunks[i].Props["summary"] = enrichedChunks[i].Summary;
                    if (enrichedChunks[i].Keywords != null)
                        chunks[i].Props["keywords"] = enrichedChunks[i].Keywords;
                }
            }

            _logger.LogDebug("Enhancement completed for {Count} chunks", chunks.Length);
            return chunks;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Enhancement failed, returning original chunks");
            return chunks;
        }
    }

    #endregion

    #region Helper Methods

    private static FluxCuratorStrategy MapToFluxCuratorStrategy(string strategy)
    {
        return strategy?.ToLowerInvariant() switch
        {
            "auto" => FluxCuratorStrategy.Auto,
            "sentence" => FluxCuratorStrategy.Sentence,
            "paragraph" => FluxCuratorStrategy.Paragraph,
            "token" => FluxCuratorStrategy.Token,
            "semantic" => FluxCuratorStrategy.Semantic,
            "hierarchical" => FluxCuratorStrategy.Hierarchical,
            // Legacy mappings
            "smart" => FluxCuratorStrategy.Sentence,
            "intelligent" => FluxCuratorStrategy.Semantic,
            "fixedsize" => FluxCuratorStrategy.Token,
            "pagelevel" => FluxCuratorStrategy.Paragraph,
            _ => FluxCuratorStrategy.Auto
        };
    }

    private static void EnrichChunksWithMetadata(IReadOnlyList<DocumentChunk> chunks, ParsedContent parsed)
    {
        foreach (var chunk in chunks)
        {
            chunk.SourceInfo.Title = parsed.Metadata.Title ?? parsed.Metadata.FileName;
            chunk.SourceInfo.SourceType = parsed.Metadata.FileType ?? "unknown";
            chunk.SourceInfo.FilePath = parsed.Metadata.FileName;
            chunk.SourceInfo.ChunkCount = chunks.Count;

            // Add structure info
            if (!string.IsNullOrEmpty(parsed.Structure.Topic))
                chunk.Props["document_topic"] = parsed.Structure.Topic;
            if (parsed.Structure.Keywords.Count > 0)
                chunk.Props["document_keywords"] = parsed.Structure.Keywords;
        }
    }

    private static bool ShouldEnhance(ChunkingOptions options)
    {
        return options.CustomProperties.TryGetValue("enableEnhancement", out var val) && val is true;
    }

    private static bool ShouldUseContextualRetrieval(ChunkingOptions options)
    {
        return options.CustomProperties.TryGetValue("useContextualRetrieval", out var val) && val is true;
    }

    private static bool ShouldEnrichWithKeywords(ChunkingOptions options)
    {
        return options.CustomProperties.TryGetValue("enrichKeywords", out var val) && val is true;
    }

    private static void ValidateFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");
    }

    private void LogWarnings(string stage, IReadOnlyList<string> warnings)
    {
        foreach (var warning in warnings)
        {
            _logger.LogWarning("{Stage} warning: {Warning}", stage, warning);
        }
    }

    #endregion
}
