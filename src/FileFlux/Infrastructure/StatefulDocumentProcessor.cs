using System.Diagnostics;
using System.Runtime.CompilerServices;
using FileFlux.Core;
using FluxCurator.Core;
using FluxCurator.Core.Core;
using FluxCurator.Core.Infrastructure.Refining;
using FluxImprover;
using Microsoft.Extensions.Logging;
using FluxCuratorStrategy = FluxCurator.Core.Domain.ChunkingStrategy;
using FluxCuratorChunkOptions = FluxCurator.Core.Domain.ChunkOptions;

namespace FileFlux.Infrastructure;

/// <summary>
/// Stateful document processor implementing the 5-stage pipeline.
/// Each instance processes a single document and maintains state.
/// </summary>
/// <remarks>
/// Pipeline: Extract → Refine → LLM-Refine → Chunk → Enrich
/// LLM-Refine stage is optional and gracefully skips if no LLM service is available.
/// </remarks>
public sealed class StatefulDocumentProcessor : IDocumentProcessor
{
    private readonly IDocumentReaderFactory _readerFactory;
    private readonly IChunkerFactory _chunkerFactory;
    private readonly IDocumentRefiner? _documentRefiner;
    private readonly ILlmRefiner? _llmRefiner;
    private readonly IDocumentEnricher? _documentEnricher;
    private readonly FluxImproverServices? _improverServices;
    private readonly ITextRefiner _textRefiner;
    private readonly IMarkdownConverter? _markdownConverter;
    private readonly IImageToTextService? _imageToTextService;
    private readonly ILogger<StatefulDocumentProcessor> _logger;

    private readonly Stream? _stream;
    private readonly byte[]? _content;
    private readonly string _extension;
    private readonly string? _fileName;

    private ProcessorState _state = ProcessorState.Created;
    private bool _disposed;

    /// <inheritdoc/>
    public ProcessingResult Result { get; } = new();

    /// <inheritdoc/>
    public string FilePath { get; }

    /// <inheritdoc/>
    public ProcessorState State => _state;

    #region Constructors

    /// <summary>
    /// Create processor for file path.
    /// </summary>
    internal StatefulDocumentProcessor(
        string filePath,
        IDocumentReaderFactory readerFactory,
        IChunkerFactory chunkerFactory,
        IDocumentRefiner? documentRefiner,
        ILlmRefiner? llmRefiner,
        IDocumentEnricher? documentEnricher,
        FluxImproverServices? improverServices,
        IMarkdownConverter? markdownConverter,
        IImageToTextService? imageToTextService,
        ILogger<StatefulDocumentProcessor> logger)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        FilePath = filePath;
        _extension = Path.GetExtension(filePath);
        _fileName = Path.GetFileName(filePath);

        _readerFactory = readerFactory ?? throw new ArgumentNullException(nameof(readerFactory));
        _chunkerFactory = chunkerFactory ?? throw new ArgumentNullException(nameof(chunkerFactory));
        _documentRefiner = documentRefiner;
        _llmRefiner = llmRefiner;
        _documentEnricher = documentEnricher;
        _improverServices = improverServices;
        _markdownConverter = markdownConverter;
        _imageToTextService = imageToTextService;
        _textRefiner = new TextRefiner();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<StatefulDocumentProcessor>.Instance;
    }

    /// <summary>
    /// Create processor for stream.
    /// </summary>
    internal StatefulDocumentProcessor(
        Stream stream,
        string extension,
        IDocumentReaderFactory readerFactory,
        IChunkerFactory chunkerFactory,
        IDocumentRefiner? documentRefiner,
        ILlmRefiner? llmRefiner,
        IDocumentEnricher? documentEnricher,
        FluxImproverServices? improverServices,
        IMarkdownConverter? markdownConverter,
        IImageToTextService? imageToTextService,
        ILogger<StatefulDocumentProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (string.IsNullOrWhiteSpace(extension))
            throw new ArgumentException("Extension is required for stream processing", nameof(extension));

        _stream = stream;
        _extension = extension.StartsWith('.') ? extension : $".{extension}";
        _fileName = $"stream{_extension}";
        FilePath = $"stream://{_fileName}";

        _readerFactory = readerFactory ?? throw new ArgumentNullException(nameof(readerFactory));
        _chunkerFactory = chunkerFactory ?? throw new ArgumentNullException(nameof(chunkerFactory));
        _documentRefiner = documentRefiner;
        _llmRefiner = llmRefiner;
        _documentEnricher = documentEnricher;
        _improverServices = improverServices;
        _markdownConverter = markdownConverter;
        _imageToTextService = imageToTextService;
        _textRefiner = new TextRefiner();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<StatefulDocumentProcessor>.Instance;
    }

    /// <summary>
    /// Create processor for byte array.
    /// </summary>
    internal StatefulDocumentProcessor(
        byte[] content,
        string extension,
        string? fileName,
        IDocumentReaderFactory readerFactory,
        IChunkerFactory chunkerFactory,
        IDocumentRefiner? documentRefiner,
        ILlmRefiner? llmRefiner,
        IDocumentEnricher? documentEnricher,
        FluxImproverServices? improverServices,
        IMarkdownConverter? markdownConverter,
        IImageToTextService? imageToTextService,
        ILogger<StatefulDocumentProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(extension))
            throw new ArgumentException("Extension is required for content processing", nameof(extension));

        _content = content;
        _extension = extension.StartsWith('.') ? extension : $".{extension}";
        _fileName = fileName ?? $"content{_extension}";
        FilePath = $"memory://{_fileName}";

        _readerFactory = readerFactory ?? throw new ArgumentNullException(nameof(readerFactory));
        _chunkerFactory = chunkerFactory ?? throw new ArgumentNullException(nameof(chunkerFactory));
        _documentRefiner = documentRefiner;
        _llmRefiner = llmRefiner;
        _documentEnricher = documentEnricher;
        _improverServices = improverServices;
        _markdownConverter = markdownConverter;
        _imageToTextService = imageToTextService;
        _textRefiner = new TextRefiner();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<StatefulDocumentProcessor>.Instance;
    }

    #endregion

    #region Stage 1: Extract

    /// <inheritdoc/>
    public async Task ExtractAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_state >= ProcessorState.Extracted)
        {
            _logger.LogDebug("Extract already completed, skipping");
            return;
        }

        var sw = Stopwatch.StartNew();
        _logger.LogDebug("Starting extraction: {FilePath}", FilePath);

        try
        {
            RawContent rawContent;

            if (_content != null)
            {
                rawContent = await ExtractFromBytesAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (_stream != null)
            {
                rawContent = await ExtractFromStreamAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                rawContent = await ExtractFromFileAsync(cancellationToken).ConfigureAwait(false);
            }

            Result.Raw = rawContent;
            Result.Metrics.ExtractDuration = sw.Elapsed;
            Result.Metrics.SourceFileSize = rawContent.File.Size;
            Result.Metrics.OriginalCharCount = rawContent.Text.Length;

            _state = ProcessorState.Extracted;
            _logger.LogInformation("Extracted {CharCount} chars from {FileName} in {Duration:F2}s",
                rawContent.Text.Length, rawContent.File.Name, sw.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            _state = ProcessorState.Failed;
            throw new DocumentProcessingException(FilePath, $"Extraction failed: {ex.Message}", ex);
        }
    }

    private async Task<RawContent> ExtractFromFileAsync(CancellationToken cancellationToken)
    {
        var reader = _readerFactory.GetReader(FilePath)
            ?? throw new UnsupportedFileFormatException(FilePath, $"No reader found for: {FilePath}");
        return await reader.ExtractAsync(FilePath, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RawContent> ExtractFromStreamAsync(CancellationToken cancellationToken)
    {
        var reader = _readerFactory.GetReader(_extension)
            ?? throw new UnsupportedFileFormatException(_extension, $"No reader found for extension: {_extension}");
        return await reader.ExtractAsync(_stream!, _fileName ?? $"stream{_extension}", null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RawContent> ExtractFromBytesAsync(CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(_content!);
        var reader = _readerFactory.GetReader(_extension)
            ?? throw new UnsupportedFileFormatException(_extension, $"No reader found for extension: {_extension}");
        return await reader.ExtractAsync(stream, _fileName ?? $"content{_extension}", null, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Stage 2: Refine

    /// <inheritdoc/>
    public async Task RefineAsync(RefineOptions? options = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_state >= ProcessorState.Refined)
        {
            _logger.LogDebug("Refine already completed, skipping");
            return;
        }

        // Auto-run previous stage if needed
        if (_state < ProcessorState.Extracted)
        {
            await ExtractAsync(cancellationToken).ConfigureAwait(false);
        }

        options ??= RefineOptions.Default;
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("Starting refinement: {FilePath}", FilePath);

        try
        {
            var raw = Result.Raw!;
            RefinedContent refined;

            // Delegate to IDocumentRefiner if available
            if (_documentRefiner != null)
            {
                refined = await _documentRefiner.RefineAsync(raw, options, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Fallback to internal refinement logic
                refined = await RefineInternalAsync(raw, options, cancellationToken).ConfigureAwait(false);
            }

            Result.Refined = refined;
            Result.Metrics.RefineDuration = sw.Elapsed;
            Result.Metrics.RefinedCharCount = refined.Text.Length;
            Result.Metrics.StructuresExtracted = refined.Structures.Count;

            _state = ProcessorState.Refined;
            _logger.LogInformation("Refined {OriginalChars} → {RefinedChars} chars, {StructureCount} structures in {Duration:F2}s",
                raw.Text.Length, refined.Text.Length, refined.Structures.Count, sw.Elapsed.TotalSeconds);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            _state = ProcessorState.Failed;
            throw new DocumentProcessingException(FilePath, $"Refinement failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Internal refinement logic used when no IDocumentRefiner is injected.
    /// </summary>
    private async Task<RefinedContent> RefineInternalAsync(RawContent raw, RefineOptions options, CancellationToken cancellationToken)
    {
        var refinedText = raw.Text;
        var structures = new List<StructuredElement>();

        // Step 1: Clean noise
        if (options.CleanNoise)
        {
            refinedText = CleanContent(refinedText);
        }

        // Step 2: Convert to markdown if converter available
        if ((options.ConvertTablesToMarkdown || options.ConvertBlocksToMarkdown) && _markdownConverter != null)
        {
            var markdownResult = await _markdownConverter.ConvertAsync(raw, new MarkdownConversionOptions
            {
                PreserveHeadings = true,
                ConvertTables = true,
                PreserveLists = true,
                IncludeImagePlaceholders = true,
                DetectCodeBlocks = true,
                NormalizeWhitespace = true
            }, cancellationToken).ConfigureAwait(false);

            if (markdownResult.IsSuccess)
            {
                refinedText = markdownResult.Markdown;
            }
        }

        // Step 3: Extract structured elements
        if (options.ExtractStructures)
        {
            var extractedStructures = ExtractStructuredElements(refinedText);
            structures.AddRange(extractedStructures);
        }

        // Step 4: Text-level refinement via FluxCurator (Standard includes token optimization)
        var textRefineOptions = FluxCurator.Core.Domain.TextRefineOptions.Standard;
        refinedText = _textRefiner.Refine(refinedText, textRefineOptions);

        // Build sections from text
        var sections = options.BuildSections ? BuildSections(refinedText) : [];

        return new RefinedContent
        {
            RawId = raw.Id,
            Text = refinedText,
            Sections = sections,
            Structures = structures,
            Metadata = BuildMetadata(raw),
            Quality = new RefinementQuality
            {
                OriginalCharCount = raw.Text.Length,
                RefinedCharCount = refinedText.Length,
                StructureScore = structures.Count > 0 ? 0.8 : 0.5,
                CleanupScore = 0.7,
                RetentionScore = Math.Min(1.0, (double)refinedText.Length / raw.Text.Length),
                ConfidenceScore = 0.75
            },
            Info = new RefinementInfo
            {
                RefinerType = "StatefulDocumentProcessor",
                UsedLlm = options.UseLlm,
                Duration = TimeSpan.Zero
            }
        };
    }

    #endregion

    #region Stage 2.5: LLM Refine

    /// <inheritdoc/>
    public async Task LlmRefineAsync(LlmRefineOptions? options = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_state >= ProcessorState.LlmRefined)
        {
            _logger.LogDebug("LLM Refine already completed, skipping");
            return;
        }

        // Auto-run previous stage if needed
        if (_state < ProcessorState.Refined)
        {
            await RefineAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        options ??= LlmRefineOptions.Default;
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("Starting LLM refinement: {FilePath}", FilePath);

        try
        {
            // If no improvements are enabled, skip LLM refinement
            if (!options.HasAnyImprovementEnabled)
            {
                _logger.LogDebug("LLM refinement disabled via options, skipping");
                Result.LlmRefined = LlmRefinedContent.FromRefinedContent(Result.Refined!);
                _state = ProcessorState.LlmRefined;
                return;
            }

            // Use ILlmRefiner if available
            if (_llmRefiner != null && _llmRefiner.IsAvailable)
            {
                Result.LlmRefined = await _llmRefiner.RefineAsync(Result.Refined!, options, cancellationToken).ConfigureAwait(false);
                Result.Metrics.LlmRefineDuration = sw.Elapsed;
                Result.Metrics.LlmRefineTokens = Result.LlmRefined.Info.InputTokens + Result.LlmRefined.Info.OutputTokens;

                _state = ProcessorState.LlmRefined;
                _logger.LogInformation("LLM Refined {OriginalChars} → {RefinedChars} chars, {Improvements} improvements in {Duration:F2}s",
                    Result.Refined!.Text.Length,
                    Result.LlmRefined.Text.Length,
                    Result.LlmRefined.Info.Improvements.Count,
                    sw.Elapsed.TotalSeconds);
                return;
            }

            // Fallback to passthrough if LLM service not available
            _logger.LogDebug("LLM refiner not available, creating passthrough result");
            Result.LlmRefined = LlmRefinedContent.FromRefinedContent(Result.Refined!);
            Result.Metrics.LlmRefineDuration = sw.Elapsed;

            _state = ProcessorState.LlmRefined;
            _logger.LogInformation("LLM Refine stage completed (passthrough) in {Duration:F2}s", sw.Elapsed.TotalSeconds);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            // On LLM failure, create passthrough result instead of failing the whole pipeline
            _logger.LogWarning(ex, "LLM refinement failed, creating passthrough result");
            Result.LlmRefined = LlmRefinedContent.FromRefinedContent(Result.Refined!);
            Result.Metrics.LlmRefineDuration = sw.Elapsed;
            _state = ProcessorState.LlmRefined;
        }
    }

    #endregion

    #region Stage 3: Chunk

    /// <inheritdoc/>
    public async Task ChunkAsync(ChunkingOptions? options = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_state >= ProcessorState.Chunked)
        {
            _logger.LogDebug("Chunk already completed, skipping");
            return;
        }

        // Auto-run previous stage if needed
        if (_state < ProcessorState.Refined)
        {
            await RefineAsync(null, cancellationToken).ConfigureAwait(false);
        }

        options ??= new ChunkingOptions();
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("Starting chunking: {FilePath}, Strategy: {Strategy}", FilePath, options.Strategy);

        try
        {
            var refined = Result.Refined!;

            if (string.IsNullOrWhiteSpace(refined.Text))
            {
                Result.Chunks = [];
                _state = ProcessorState.Chunked;
                return;
            }

            // Execute chunking via FluxCurator
            var fcStrategy = MapToFluxCuratorStrategy(options.Strategy);
            var chunker = _chunkerFactory.CreateChunker(fcStrategy);

            var fcOptions = new FluxCuratorChunkOptions
            {
                MaxChunkSize = options.MaxChunkSize,
                MinChunkSize = options.MinChunkSize,
                OverlapSize = options.OverlapSize,
                TargetChunkSize = options.MaxChunkSize / 2,
                PreserveParagraphs = options.PreserveParagraphs,
                PreserveSentences = options.PreserveSentences,
                EnableChunkBalancing = options.EnableChunkBalancing
            };

            var fcChunks = await chunker.ChunkAsync(refined.Text, fcOptions, cancellationToken).ConfigureAwait(false);

            // Convert to FileFlux DocumentChunks from FluxCurator DocumentChunks
            var chunks = fcChunks.Select((fc, idx) => new FileFlux.Core.DocumentChunk
            {
                RawId = Result.Raw!.Id,
                Content = fc.Content,
                Index = idx,
                Tokens = fc.Metadata.EstimatedTokenCount,
                Strategy = chunker.StrategyName,
                Location = new SourceLocation
                {
                    StartChar = fc.Location.StartPosition,
                    EndChar = fc.Location.EndPosition
                },
                Metadata = refined.Metadata,
                SourceInfo = new SourceMetadataInfo
                {
                    SourceId = Result.DocumentId.ToString(),
                    SourceType = refined.Metadata.FileType ?? "unknown",
                    Title = refined.Metadata.Title ?? refined.Metadata.FileName,
                    FilePath = FilePath
                }
            }).ToList();

            // Link structures to chunks
            LinkStructuresToChunks(chunks, refined.Structures);

            Result.Chunks = chunks;
            Result.Metrics.ChunkDuration = sw.Elapsed;
            Result.Metrics.TotalChunks = chunks.Count;
            Result.Metrics.TotalTokens = chunks.Sum(c => c.Tokens);

            _state = ProcessorState.Chunked;
            _logger.LogInformation("Created {ChunkCount} chunks, {TotalTokens} tokens in {Duration:F2}s",
                chunks.Count, Result.Metrics.TotalTokens, sw.Elapsed.TotalSeconds);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            _state = ProcessorState.Failed;
            throw new DocumentProcessingException(FilePath, $"Chunking failed: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<DocumentChunk> ChunkStreamAsync(
        ChunkingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Auto-run previous stage if needed
        if (_state < ProcessorState.Refined)
        {
            await RefineAsync(null, cancellationToken).ConfigureAwait(false);
        }

        options ??= new ChunkingOptions();
        var sw = Stopwatch.StartNew();
        var refined = Result.Refined!;

        if (string.IsNullOrWhiteSpace(refined.Text))
        {
            Result.Chunks = [];
            _state = ProcessorState.Chunked;
            yield break;
        }

        var fcStrategy = MapToFluxCuratorStrategy(options.Strategy);
        var chunker = _chunkerFactory.CreateChunker(fcStrategy);

        var fcOptions = new FluxCuratorChunkOptions
        {
            MaxChunkSize = options.MaxChunkSize,
            MinChunkSize = options.MinChunkSize,
            OverlapSize = options.OverlapSize
        };

        // Use ChunkAsync and yield results (IChunker doesn't have streaming)
        var fcChunks = await chunker.ChunkAsync(refined.Text, fcOptions, cancellationToken).ConfigureAwait(false);
        var chunks = new List<FileFlux.Core.DocumentChunk>();
        var idx = 0;

        foreach (var fc in fcChunks)
        {
            var chunk = new FileFlux.Core.DocumentChunk
            {
                RawId = Result.Raw!.Id,
                Content = fc.Content,
                Index = idx++,
                Tokens = fc.Metadata.EstimatedTokenCount,
                Strategy = chunker.StrategyName,
                Location = new SourceLocation
                {
                    StartChar = fc.Location.StartPosition,
                    EndChar = fc.Location.EndPosition
                },
                Metadata = refined.Metadata
            };

            chunks.Add(chunk);
            yield return chunk;
        }

        Result.Chunks = chunks;
        Result.Metrics.ChunkDuration = sw.Elapsed;
        Result.Metrics.TotalChunks = chunks.Count;
        Result.Metrics.TotalTokens = chunks.Sum(c => c.Tokens);

        _state = ProcessorState.Chunked;
    }

    #endregion

    #region Stage 4: Enrich

    /// <inheritdoc/>
    public async Task EnrichAsync(EnrichOptions? options = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_state >= ProcessorState.Enriched)
        {
            _logger.LogDebug("Enrich already completed, skipping");
            return;
        }

        // Auto-run previous stage if needed
        if (_state < ProcessorState.Chunked)
        {
            await ChunkAsync(null, cancellationToken).ConfigureAwait(false);
        }

        options ??= EnrichOptions.Default;
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("Starting enrichment: {FilePath}", FilePath);

        try
        {
            var chunks = Result.Chunks!.ToList();

            if (chunks.Count == 0)
            {
                Result.Graph = new DocumentGraph { DocumentId = Result.DocumentId };
                _state = ProcessorState.Enriched;
                return;
            }

            // Delegate to IDocumentEnricher if available
            if (_documentEnricher != null)
            {
                var enrichResult = await _documentEnricher.EnrichAsync(
                    chunks,
                    Result.Refined!,
                    options,
                    cancellationToken).ConfigureAwait(false);

                // Apply enrichment results back to chunks
                ApplyEnrichmentResults(chunks, enrichResult);

                // Set graph from enrichment result
                Result.Graph = enrichResult.Graph ?? new DocumentGraph { DocumentId = Result.DocumentId };
                Result.Metrics.GraphNodes = Result.Graph.NodeCount;
                Result.Metrics.GraphEdges = Result.Graph.EdgeCount;
                Result.Metrics.EnrichDuration = sw.Elapsed;

                _state = ProcessorState.Enriched;
                _logger.LogInformation("Enriched {ChunkCount} chunks via IDocumentEnricher, graph: {NodeCount} nodes, {EdgeCount} edges in {Duration:F2}s",
                    chunks.Count, Result.Graph.NodeCount, Result.Graph.EdgeCount, sw.Elapsed.TotalSeconds);
                return;
            }

            // Fallback to internal enrichment logic
            await EnrichInternalAsync(chunks, options, cancellationToken).ConfigureAwait(false);

            Result.Metrics.EnrichDuration = sw.Elapsed;
            _state = ProcessorState.Enriched;
            _logger.LogInformation("Enriched {ChunkCount} chunks, graph: {NodeCount} nodes, {EdgeCount} edges in {Duration:F2}s",
                chunks.Count, Result.Graph!.NodeCount, Result.Graph.EdgeCount, sw.Elapsed.TotalSeconds);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            _state = ProcessorState.Failed;
            throw new DocumentProcessingException(FilePath, $"Enrichment failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Internal enrichment logic used when no IDocumentEnricher is injected.
    /// </summary>
    private async Task EnrichInternalAsync(List<DocumentChunk> chunks, EnrichOptions options, CancellationToken cancellationToken)
    {
        // Step 1: LLM enrichment (if available)
        if (_improverServices != null)
        {
            await EnrichWithLlmAsync(chunks, options, cancellationToken).ConfigureAwait(false);
        }

        // Step 2: Build graph
        if (options.BuildGraph)
        {
            var graph = BuildDocumentGraph(chunks);
            Result.Graph = graph;
            Result.Metrics.GraphNodes = graph.NodeCount;
            Result.Metrics.GraphEdges = graph.EdgeCount;
        }
        else
        {
            Result.Graph = new DocumentGraph { DocumentId = Result.DocumentId };
        }
    }

    /// <summary>
    /// Apply enrichment results back to DocumentChunks.
    /// </summary>
    private static void ApplyEnrichmentResults(List<DocumentChunk> chunks, EnrichmentResult enrichResult)
    {
        foreach (var enrichedChunk in enrichResult.Chunks)
        {
            var chunk = chunks.FirstOrDefault(c => c.Id == enrichedChunk.Chunk.Id);
            if (chunk == null) continue;

            if (!string.IsNullOrEmpty(enrichedChunk.Summary))
                chunk.Props[ChunkPropsKeys.EnrichedSummary] = enrichedChunk.Summary;

            if (enrichedChunk.Keywords != null && enrichedChunk.Keywords.Count > 0)
                chunk.Props[ChunkPropsKeys.EnrichedKeywords] = enrichedChunk.KeywordList;

            if (!string.IsNullOrEmpty(enrichedChunk.ContextualText))
                chunk.Props[ChunkPropsKeys.EnrichedContextualText] = enrichedChunk.ContextualText;

            if (enrichedChunk.Entities != null && enrichedChunk.Entities.Count > 0)
                chunk.Props["entities"] = enrichedChunk.Entities;

            if (enrichedChunk.Topics != null && enrichedChunk.Topics.Count > 0)
                chunk.Props["topics"] = enrichedChunk.Topics;
        }
    }

    private async Task EnrichWithLlmAsync(List<DocumentChunk> chunks, EnrichOptions options, CancellationToken cancellationToken)
    {
        if (_improverServices == null) return;

        var improverChunks = chunks.Select(c => new FluxImprover.Models.Chunk
        {
            Id = c.Id.ToString(),
            Content = c.Content,
            Metadata = c.Props
        }).ToList();

        // Enrichment via FluxImprover
        if (options.GenerateSummaries || options.ExtractKeywords)
        {
            var enrichmentOptions = new FluxImprover.Options.EnrichmentOptions();
            var enrichedChunks = await _improverServices.ChunkEnrichment
                .EnrichBatchAsync(improverChunks, enrichmentOptions, cancellationToken)
                .ConfigureAwait(false);

            for (int i = 0; i < chunks.Count && i < enrichedChunks.Count; i++)
            {
                if (options.GenerateSummaries && enrichedChunks[i].Summary != null)
                    chunks[i].Props[ChunkPropsKeys.EnrichedSummary] = enrichedChunks[i].Summary!;
                if (options.ExtractKeywords && enrichedChunks[i].Keywords != null)
                    chunks[i].Props[ChunkPropsKeys.EnrichedKeywords] = enrichedChunks[i].Keywords!;
            }
        }

        // Contextual retrieval
        if (options.AddContextualText && Result.Raw != null)
        {
            var contextualChunks = await _improverServices.ContextualEnrichment
                .EnrichBatchAsync(improverChunks, Result.Raw.Text, null, cancellationToken)
                .ConfigureAwait(false);

            for (int i = 0; i < chunks.Count && i < contextualChunks.Count; i++)
            {
                chunks[i].Props[ChunkPropsKeys.EnrichedContextualText] = contextualChunks[i].GetContextualizedText();
            }
        }
    }

    #endregion

    #region Convenience Methods

    /// <inheritdoc/>
    public async Task ProcessAsync(ProcessingOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= ProcessingOptions.Default;

        await ExtractAsync(cancellationToken).ConfigureAwait(false);
        await RefineAsync(options.Refine, cancellationToken).ConfigureAwait(false);

        if (options.IncludeLlmRefine)
        {
            await LlmRefineAsync(options.LlmRefine, cancellationToken).ConfigureAwait(false);
        }

        await ChunkAsync(options.Chunking, cancellationToken).ConfigureAwait(false);

        if (options.IncludeEnrich)
        {
            await EnrichAsync(options.Enrich, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<DocumentChunk> ProcessStreamAsync(
        ProcessingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= ProcessingOptions.Default;

        await ExtractAsync(cancellationToken).ConfigureAwait(false);
        await RefineAsync(options.Refine, cancellationToken).ConfigureAwait(false);

        if (options.IncludeLlmRefine)
        {
            await LlmRefineAsync(options.LlmRefine, cancellationToken).ConfigureAwait(false);
        }

        await foreach (var chunk in ChunkStreamAsync(options.Chunking, cancellationToken))
        {
            yield return chunk;
        }

        if (options.IncludeEnrich)
        {
            await EnrichAsync(options.Enrich, cancellationToken).ConfigureAwait(false);
        }
    }

    #endregion

    #region Helper Methods

    private string CleanContent(string text)
    {
        // Remove artificial paragraph headings
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"^#{1,6}\s*Paragraph\s+\d+\s*$",
            "",
            System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Clean whitespace
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[ \t]{2,}", " ");

        return text.Trim();
    }

    private List<StructuredElement> ExtractStructuredElements(string text)
    {
        var structures = new List<StructuredElement>();

        // Extract code blocks
        var codeBlockPattern = @"```(\w+)?\s*\n([\s\S]*?)```";
        var codeMatches = System.Text.RegularExpressions.Regex.Matches(text, codeBlockPattern);
        foreach (System.Text.RegularExpressions.Match match in codeMatches)
        {
            var language = match.Groups[1].Value;
            var code = match.Groups[2].Value.Trim();
            var codeData = new CodeBlockData
            {
                Language = string.IsNullOrEmpty(language) ? "text" : language,
                Content = code
            };

            structures.Add(new StructuredElement
            {
                Type = StructureType.Code,
                Caption = $"Code block ({codeData.Language})",
                Data = System.Text.Json.JsonSerializer.SerializeToElement(codeData),
                Location = new StructureLocation
                {
                    StartChar = match.Index,
                    EndChar = match.Index + match.Length
                }
            });
        }

        // Extract markdown tables
        var tablePattern = @"^\|.+\|\s*\n\|[-:\s|]+\|\s*\n(\|.+\|\s*\n)+";
        var tableMatches = System.Text.RegularExpressions.Regex.Matches(text, tablePattern, System.Text.RegularExpressions.RegexOptions.Multiline);
        foreach (System.Text.RegularExpressions.Match match in tableMatches)
        {
            var tableData = ParseMarkdownTable(match.Value);
            if (tableData.Count > 0)
            {
                structures.Add(new StructuredElement
                {
                    Type = StructureType.Table,
                    Caption = "Markdown table",
                    Data = System.Text.Json.JsonSerializer.SerializeToElement(tableData),
                    Location = new StructureLocation
                    {
                        StartChar = match.Index,
                        EndChar = match.Index + match.Length
                    }
                });
            }
        }

        return structures;
    }

    private List<Dictionary<string, string>> ParseMarkdownTable(string tableText)
    {
        var lines = tableText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 3) return [];

        var headers = lines[0].Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(h => h.Trim())
            .ToArray();

        var rows = new List<Dictionary<string, string>>();
        for (int i = 2; i < lines.Length; i++)
        {
            var cells = lines[i].Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .ToArray();

            var row = new Dictionary<string, string>();
            for (int j = 0; j < Math.Min(headers.Length, cells.Length); j++)
            {
                row[headers[j]] = cells[j];
            }
            rows.Add(row);
        }

        return rows;
    }

    private List<Section> BuildSections(string text)
    {
        var sections = new List<Section>();
        var headingPattern = @"^(#{1,6})\s+(.+)$";
        var matches = System.Text.RegularExpressions.Regex.Matches(text, headingPattern, System.Text.RegularExpressions.RegexOptions.Multiline);

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var level = match.Groups[1].Value.Length;
            var title = match.Groups[2].Value.Trim();

            var endPos = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;

            sections.Add(new Section
            {
                Id = $"section_{i}",
                Title = title,
                Level = level,
                Start = match.Index,
                End = endPos,
                Content = text.Substring(match.Index, endPos - match.Index).Trim()
            });
        }

        return sections;
    }

    private DocumentMetadata BuildMetadata(RawContent raw)
    {
        return new DocumentMetadata
        {
            FileName = raw.File.Name,
            FileType = raw.File.Extension.TrimStart('.').ToUpperInvariant(),
            FileSize = raw.File.Size,
            Title = raw.File.Name,
            CreatedAt = raw.File.CreatedAt,
            ModifiedAt = raw.File.ModifiedAt
        };
    }

    private void LinkStructuresToChunks(List<DocumentChunk> chunks, IReadOnlyList<StructuredElement> structures)
    {
        foreach (var structure in structures)
        {
            var startChar = structure.Location.StartChar;
            var chunk = chunks.FirstOrDefault(c =>
                c.Location.StartChar <= startChar && c.Location.EndChar >= startChar);

            if (chunk != null)
            {
                ((StructuredElement)structure).SourceChunkId = chunk.Id;
            }
        }
    }

    private DocumentGraph BuildDocumentGraph(List<DocumentChunk> chunks)
    {
        var nodes = new List<ChunkNode>();
        var edges = new List<ChunkEdge>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var node = new ChunkNode
            {
                ChunkId = chunk.Id,
                Index = i,
                Summary = chunk.EnrichedSummary,
                Keywords = chunk.EnrichedKeywords?.ToList() ?? [],
                SectionPath = chunk.Location.HeadingPath,
                Position = new ChunkPosition
                {
                    Sequence = i,
                    PreviousId = i > 0 ? chunks[i - 1].Id : null,
                    NextId = i < chunks.Count - 1 ? chunks[i + 1].Id : null,
                    Depth = chunk.Location.HeadingPath.Count
                }
            };
            nodes.Add(node);

            // Sequential edges
            if (i > 0)
            {
                edges.Add(new ChunkEdge
                {
                    SourceId = chunks[i - 1].Id,
                    TargetId = chunk.Id,
                    Type = EdgeType.Sequential,
                    Weight = 1.0,
                    Label = "follows"
                });
            }
        }

        return new DocumentGraph
        {
            DocumentId = Result.DocumentId,
            Nodes = nodes,
            Edges = edges
        };
    }

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
            _ => FluxCuratorStrategy.Auto
        };
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            _state = ProcessorState.Disposed;
            throw new ObjectDisposedException(nameof(StatefulDocumentProcessor));
        }
    }

    #endregion

    #region IDisposable / IAsyncDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _state = ProcessorState.Disposed;
        Result.Clear();
        _stream?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _state = ProcessorState.Disposed;
        Result.Clear();
        if (_stream != null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    #endregion
}
