using FileFlux.Core;
using FileFlux.Domain;

namespace FileFlux.Infrastructure.Integration;

/// <summary>
/// Helper class for integrating FileFlux with FluxImprover quality layer.
/// Provides conversion and context building utilities.
/// </summary>
public class FluxImproverIntegrationHelper : IFluxImproverIntegration
{
    /// <inheritdoc />
    public ChunkData ToFluxImproverChunk(DocumentChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var metadata = new Dictionary<string, object>();

        // Copy Props
        foreach (var prop in chunk.Props)
        {
            metadata[prop.Key] = prop.Value;
        }

        // Add core metadata
        if (chunk.Location.StartPage.HasValue)
            metadata["StartPage"] = chunk.Location.StartPage.Value;
        if (chunk.Location.EndPage.HasValue)
            metadata["EndPage"] = chunk.Location.EndPage.Value;
        if (chunk.Location.HeadingPath.Count > 0)
            metadata["HeadingPath"] = chunk.Location.HeadingPath;

        metadata["Index"] = chunk.Index;
        metadata["Strategy"] = chunk.Strategy;
        metadata["Quality"] = chunk.Quality;
        metadata["TokenCount"] = chunk.Tokens;

        return new ChunkData
        {
            Id = chunk.Id.ToString(),
            Content = chunk.Content,
            Metadata = metadata
        };
    }

    /// <inheritdoc />
    public ChunkDataWithContext ToFluxImproverChunkWithContext(
        HierarchicalDocumentChunk chunk,
        ParentEnrichmentData? parentEnrichedData = null)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var baseChunk = ToFluxImproverChunk(chunk);

        // Build parent context from chunk hierarchy and enriched data
        var parentContext = parentEnrichedData ?? BuildParentContextFromChunk(chunk);

        return new ChunkDataWithContext
        {
            Id = baseChunk.Id,
            Content = baseChunk.Content,
            Metadata = baseChunk.Metadata,
            ParentContext = parentContext
        };
    }

    /// <inheritdoc />
    public ContextualEnrichmentData PrepareForContextualEnrichment(
        DocumentChunk chunk,
        string fullDocumentText)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(fullDocumentText);

        var chunkData = ToFluxImproverChunk(chunk);

        return new ContextualEnrichmentData
        {
            Chunk = chunkData,
            FullDocumentText = fullDocumentText,
            DocumentMetadata = new DocumentContextMetadata
            {
                Title = chunk.SourceInfo?.Title,
                DocumentType = chunk.SourceInfo?.SourceType,
                Language = chunk.SourceInfo?.Language,
                TotalChunks = chunk.SourceInfo?.ChunkCount ?? 0
            }
        };
    }

    /// <summary>
    /// Build parent context from chunk's own hierarchy information
    /// </summary>
    private static ParentEnrichmentData? BuildParentContextFromChunk(HierarchicalDocumentChunk chunk)
    {
        if (chunk.IsRoot)
            return null;

        // Extract heading path as breadcrumb
        var headingPath = chunk.Location.HeadingPath.Count > 0
            ? string.Join(" > ", chunk.Location.HeadingPath)
            : null;

        return new ParentEnrichmentData
        {
            ParentId = chunk.ParentId,
            ParentHeadingPath = headingPath,
            HierarchyLevel = chunk.Level
        };
    }

    /// <summary>
    /// Process hierarchical chunks in order suitable for FluxImprover enrichment.
    /// Returns chunks ordered by hierarchy level (parents first).
    /// </summary>
    /// <param name="chunks">Hierarchical chunks to process</param>
    /// <returns>Chunks ordered for hierarchical enrichment</returns>
    public static IEnumerable<HierarchicalDocumentChunk> OrderForHierarchicalEnrichment(
        IEnumerable<HierarchicalDocumentChunk> chunks)
    {
        // Group by level and return in order (level 0 first, then 1, 2, etc.)
        return chunks.OrderBy(c => c.Level).ThenBy(c => c.Index);
    }

    /// <summary>
    /// Build a parent-child enrichment map for hierarchical processing.
    /// Returns dictionary mapping parent IDs to their child chunks.
    /// Uses empty string as key for root-level chunks (no parent).
    /// </summary>
    public static Dictionary<string, List<HierarchicalDocumentChunk>> BuildHierarchyMap(
        IEnumerable<HierarchicalDocumentChunk> chunks)
    {
        var map = new Dictionary<string, List<HierarchicalDocumentChunk>>();

        foreach (var chunk in chunks)
        {
            // Use empty string for null parent IDs (root-level chunks)
            var parentId = chunk.ParentId ?? string.Empty;
            if (!map.TryGetValue(parentId, out var children))
            {
                children = new List<HierarchicalDocumentChunk>();
                map[parentId] = children;
            }
            children.Add(chunk);
        }

        return map;
    }

    /// <summary>
    /// Convert batch of FileFlux chunks to FluxImprover-compatible format.
    /// </summary>
    public IReadOnlyList<ChunkData> ToFluxImproverChunks(IEnumerable<DocumentChunk> chunks)
    {
        return chunks.Select(ToFluxImproverChunk).ToList();
    }

    /// <summary>
    /// Prepare batch of chunks for contextual enrichment.
    /// </summary>
    public IReadOnlyList<ContextualEnrichmentData> PrepareForContextualEnrichmentBatch(
        IEnumerable<DocumentChunk> chunks,
        string fullDocumentText)
    {
        return chunks
            .Select(c => PrepareForContextualEnrichment(c, fullDocumentText))
            .ToList();
    }
}

/// <summary>
/// Extension methods for FluxImprover integration.
/// </summary>
public static class FluxImproverIntegrationExtensions
{
    private static readonly FluxImproverIntegrationHelper _helper = new();

    /// <summary>
    /// Convert DocumentChunk to FluxImprover-compatible ChunkData.
    /// </summary>
    public static ChunkData ToFluxImproverChunk(this DocumentChunk chunk)
        => _helper.ToFluxImproverChunk(chunk);

    /// <summary>
    /// Convert HierarchicalDocumentChunk to ChunkDataWithContext for hierarchical enrichment.
    /// </summary>
    public static ChunkDataWithContext ToFluxImproverChunkWithContext(
        this HierarchicalDocumentChunk chunk,
        ParentEnrichmentData? parentEnrichedData = null)
        => _helper.ToFluxImproverChunkWithContext(chunk, parentEnrichedData);

    /// <summary>
    /// Prepare chunk for contextual retrieval enrichment.
    /// </summary>
    public static ContextualEnrichmentData PrepareForContextualEnrichment(
        this DocumentChunk chunk,
        string fullDocumentText)
        => _helper.PrepareForContextualEnrichment(chunk, fullDocumentText);

    /// <summary>
    /// Convert batch of chunks to FluxImprover format.
    /// </summary>
    public static IReadOnlyList<ChunkData> ToFluxImproverChunks(
        this IEnumerable<DocumentChunk> chunks)
        => _helper.ToFluxImproverChunks(chunks);

    /// <summary>
    /// Build parent enrichment data from FluxImprover enrichment result.
    /// Call this after enriching a parent chunk to create context for children.
    /// </summary>
    /// <param name="parentChunk">The parent chunk that was enriched</param>
    /// <param name="summary">Summary from FluxImprover enrichment</param>
    /// <param name="keywords">Keywords from FluxImprover enrichment</param>
    /// <returns>Parent context for child enrichment</returns>
    public static ParentEnrichmentData BuildParentContext(
        this HierarchicalDocumentChunk parentChunk,
        string? summary = null,
        IReadOnlyList<string>? keywords = null)
    {
        var headingPath = parentChunk.Location.HeadingPath.Count > 0
            ? string.Join(" > ", parentChunk.Location.HeadingPath)
            : null;

        return new ParentEnrichmentData
        {
            ParentId = parentChunk.Id.ToString(),
            ParentSummary = summary,
            ParentKeywords = keywords,
            ParentHeadingPath = headingPath,
            HierarchyLevel = parentChunk.Level
        };
    }

    /// <summary>
    /// Order chunks for hierarchical enrichment (parents first).
    /// </summary>
    public static IEnumerable<HierarchicalDocumentChunk> OrderForHierarchicalEnrichment(
        this IEnumerable<HierarchicalDocumentChunk> chunks)
        => FluxImproverIntegrationHelper.OrderForHierarchicalEnrichment(chunks);

    /// <summary>
    /// Build hierarchy map for parent-child processing.
    /// Uses empty string as key for root-level chunks (no parent).
    /// </summary>
    public static Dictionary<string, List<HierarchicalDocumentChunk>> BuildHierarchyMap(
        this IEnumerable<HierarchicalDocumentChunk> chunks)
        => FluxImproverIntegrationHelper.BuildHierarchyMap(chunks);
}
