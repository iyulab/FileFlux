namespace FileFlux.Infrastructure.Strategies;

using FileFlux.Core;
using FileFlux.Infrastructure.Adapters;
using FluxCurator.Core.Core;
using FileFluxDocumentChunk = FileFlux.Core.DocumentChunk;
using FluxCuratorDocumentChunk = FluxCurator.Core.Domain.DocumentChunk;
using FluxCuratorChunkOptions = FluxCurator.Core.Domain.ChunkOptions;
using FluxCuratorStrategy = FluxCurator.Core.Domain.ChunkingStrategy;

/// <summary>
/// Chunking strategy that delegates to FluxCurator's IChunkerFactory.
/// Provides integration between FileFlux document processing and FluxCurator text chunking.
/// </summary>
/// <remarks>
/// This strategy enables FileFlux to leverage FluxCurator's advanced text chunking capabilities
/// including semantic chunking, hierarchical chunking, and multi-language support.
///
/// Use this strategy when:
/// - You need FluxCurator's semantic or hierarchical chunking
/// - You want consistent chunking behavior between FileFlux and FluxCurator
/// - You're building a pipeline that uses both libraries
/// </remarks>
public class FluxCuratorChunkingStrategy : IChunkingStrategy
{
    private readonly IChunkerFactory _chunkerFactory;
    private readonly FluxCuratorStrategy _strategy;

    /// <summary>
    /// Creates a new FluxCurator chunking strategy with the specified chunker factory.
    /// </summary>
    /// <param name="chunkerFactory">The FluxCurator chunker factory to use.</param>
    /// <param name="strategy">The specific FluxCurator strategy to use. Default: Auto.</param>
    public FluxCuratorChunkingStrategy(
        IChunkerFactory chunkerFactory,
        FluxCuratorStrategy strategy = FluxCuratorStrategy.Auto)
    {
        _chunkerFactory = chunkerFactory ?? throw new ArgumentNullException(nameof(chunkerFactory));
        _strategy = strategy;
    }

    /// <inheritdoc/>
    public string StrategyName => $"FluxCurator.{_strategy}";

    /// <inheritdoc/>
    public IEnumerable<string> SupportedOptions => new[]
    {
        "MaxChunkSize",
        "MinChunkSize",
        "OverlapSize",
        "LanguageCode",
        "PreserveParagraphs",
        "PreserveSentences",
        "SemanticSimilarityThreshold"
    };

    /// <inheritdoc/>
    public async Task<IEnumerable<FileFluxDocumentChunk>> ChunkAsync(
        DocumentContent content,
        ChunkingOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(content.Text))
            return [];

        // Convert FileFlux options to FluxCurator options
        var fluxCuratorOptions = ConvertOptions(options);

        // Get the appropriate chunker
        var chunker = _chunkerFactory.CreateChunker(_strategy);

        // Perform chunking using FluxCurator
        var fluxCuratorChunks = await chunker.ChunkAsync(
            content.Text,
            fluxCuratorOptions,
            cancellationToken).ConfigureAwait(false);

        // Convert FluxCurator chunks to FileFlux chunks with document context
        var chunks = ConvertChunks(fluxCuratorChunks, content, options);

        return chunks;
    }

    /// <inheritdoc/>
    public int EstimateChunkCount(DocumentContent content, ChunkingOptions options)
    {
        if (content == null || string.IsNullOrWhiteSpace(content.Text))
            return 0;

        var fluxCuratorOptions = ConvertOptions(options);
        var chunker = _chunkerFactory.CreateChunker(_strategy);
        return chunker.EstimateChunkCount(content.Text, fluxCuratorOptions);
    }

    /// <summary>
    /// Converts FileFlux ChunkingOptions to FluxCurator ChunkOptions.
    /// </summary>
    private static FluxCuratorChunkOptions ConvertOptions(ChunkingOptions options)
    {
        return new FluxCuratorChunkOptions
        {
            MaxChunkSize = options.MaxChunkSize,
            MinChunkSize = options.MinChunkSize,
            OverlapSize = options.OverlapSize,
            TargetChunkSize = options.MaxChunkSize / 2, // Target half of max
            LanguageCode = options.LanguageCode == "auto" ? null : options.LanguageCode,
            PreserveParagraphs = options.PreserveParagraphs,
            PreserveSentences = options.PreserveSentences,
            PreserveSectionHeaders = true,
            IncludeMetadata = true,
            TrimWhitespace = true
        };
    }

    /// <summary>
    /// Converts FluxCurator chunks to FileFlux chunks with document context.
    /// </summary>
    private static List<FileFluxDocumentChunk> ConvertChunks(
        IReadOnlyList<FluxCuratorDocumentChunk> fluxCuratorChunks,
        DocumentContent content,
        ChunkingOptions options)
    {
        var chunks = new List<FileFluxDocumentChunk>();
        var parsedId = Guid.NewGuid();
        var rawId = Guid.NewGuid();

        foreach (var fcChunk in fluxCuratorChunks)
        {
            var chunk = fcChunk.ToFileFluxChunk(parsedId, rawId);

            // Enrich with FileFlux-specific document context
            EnrichWithDocumentContext(chunk, fcChunk, content);

            // Update source info
            chunk.SourceInfo.ChunkCount = fluxCuratorChunks.Count;
            chunk.SourceInfo.Title = content.Metadata.Title ?? string.Empty;
            chunk.SourceInfo.SourceType = content.Metadata.FileType ?? string.Empty;
            chunk.SourceInfo.FilePath = content.Metadata.FileName;

            chunks.Add(chunk);
        }

        return chunks;
    }

    /// <summary>
    /// Enriches a FileFlux chunk with document context from DocumentContent.
    /// </summary>
    private static void EnrichWithDocumentContext(
        FileFluxDocumentChunk chunk,
        FluxCuratorDocumentChunk fcChunk,
        DocumentContent content)
    {
        // Add page information if available
        if (content.PageRanges.Count > 0)
        {
            var (startPage, endPage) = FindPageRange(
                fcChunk.Location.StartPosition,
                fcChunk.Location.EndPosition,
                content.PageRanges);

            chunk.Location.StartPage = startPage;
            chunk.Location.EndPage = endPage;
        }

        // Build heading path from document sections if FluxCurator didn't provide one
        if (chunk.Location.HeadingPath.Count == 0 && content.Sections.Count > 0)
        {
            var headingPath = BuildHeadingPath(
                fcChunk.Location.StartPosition,
                content.Sections);

            if (headingPath.Count > 0)
            {
                chunk.Location.HeadingPath = headingPath;
            }
        }
    }

    /// <summary>
    /// Finds the page range for a character position range.
    /// </summary>
    private static (int? Start, int? End) FindPageRange(
        int startPos,
        int endPos,
        Dictionary<int, (int Start, int End)> pageRanges)
    {
        int? startPage = null;
        int? endPage = null;

        foreach (var (pageNum, range) in pageRanges.OrderBy(p => p.Key))
        {
            if (startPage == null && startPos >= range.Start && startPos <= range.End)
            {
                startPage = pageNum;
            }

            if (endPos >= range.Start && endPos <= range.End)
            {
                endPage = pageNum;
            }

            // If we found end page, we're done
            if (endPage != null)
                break;
        }

        return (startPage, endPage ?? startPage);
    }

    /// <summary>
    /// Builds the heading path for a position in the document.
    /// </summary>
    private static List<string> BuildHeadingPath(int position, List<ContentSection> sections)
    {
        var path = new List<string>();
        BuildHeadingPathRecursive(position, sections, path);
        return path;
    }

    private static void BuildHeadingPathRecursive(
        int position,
        List<ContentSection> sections,
        List<string> path)
    {
        foreach (var section in sections)
        {
            if (position >= section.StartPosition && position <= section.EndPosition)
            {
                if (!string.IsNullOrEmpty(section.Title))
                {
                    path.Add(section.Title);
                }

                if (section.Children.Count > 0)
                {
                    BuildHeadingPathRecursive(position, section.Children, path);
                }

                break;
            }
        }
    }
}

/// <summary>
/// Factory extension methods for creating FluxCurator-backed strategies.
/// </summary>
public static class FluxCuratorStrategyExtensions
{
    /// <summary>
    /// Creates a FluxCurator sentence chunking strategy.
    /// </summary>
    public static FluxCuratorChunkingStrategy CreateSentenceStrategy(this IChunkerFactory factory)
        => new(factory, FluxCuratorStrategy.Sentence);

    /// <summary>
    /// Creates a FluxCurator paragraph chunking strategy.
    /// </summary>
    public static FluxCuratorChunkingStrategy CreateParagraphStrategy(this IChunkerFactory factory)
        => new(factory, FluxCuratorStrategy.Paragraph);

    /// <summary>
    /// Creates a FluxCurator token chunking strategy.
    /// </summary>
    public static FluxCuratorChunkingStrategy CreateTokenStrategy(this IChunkerFactory factory)
        => new(factory, FluxCuratorStrategy.Token);

    /// <summary>
    /// Creates a FluxCurator semantic chunking strategy (requires embedder).
    /// </summary>
    public static FluxCuratorChunkingStrategy CreateSemanticStrategy(this IChunkerFactory factory)
        => new(factory, FluxCuratorStrategy.Semantic);

    /// <summary>
    /// Creates a FluxCurator hierarchical chunking strategy.
    /// </summary>
    public static FluxCuratorChunkingStrategy CreateHierarchicalStrategy(this IChunkerFactory factory)
        => new(factory, FluxCuratorStrategy.Hierarchical);
}
