using FileFlux.Domain;

namespace FileFlux.Core;

/// <summary>
/// Integration contracts for FluxImprover quality layer.
/// These interfaces allow FileFlux consumers to integrate with FluxImprover
/// for LLM-based quality enhancements without creating a direct dependency.
/// </summary>
public interface IFluxImproverIntegration
{
    /// <summary>
    /// Convert FileFlux chunk to FluxImprover-compatible chunk format.
    /// </summary>
    /// <param name="chunk">FileFlux document chunk</param>
    /// <returns>Generic chunk data that can be passed to FluxImprover</returns>
    ChunkData ToFluxImproverChunk(DocumentChunk chunk);

    /// <summary>
    /// Convert hierarchical chunk with parent context for FluxImprover enrichment.
    /// </summary>
    /// <param name="chunk">Hierarchical document chunk</param>
    /// <param name="parentEnrichedData">Enrichment data from parent chunk (if available)</param>
    /// <returns>Chunk data with parent context for hierarchical enrichment</returns>
    ChunkDataWithContext ToFluxImproverChunkWithContext(
        HierarchicalDocumentChunk chunk,
        ParentEnrichmentData? parentEnrichedData = null);

    /// <summary>
    /// Prepare data for contextual retrieval enrichment.
    /// </summary>
    /// <param name="chunk">Document chunk</param>
    /// <param name="fullDocumentText">Full document text for context</param>
    /// <returns>Data prepared for contextual enrichment</returns>
    ContextualEnrichmentData PrepareForContextualEnrichment(
        DocumentChunk chunk,
        string fullDocumentText);
}

/// <summary>
/// Generic chunk data for FluxImprover integration.
/// Maps to FluxImprover.Models.Chunk structure.
/// </summary>
public class ChunkData
{
    /// <summary>
    /// Chunk unique identifier
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Chunk text content
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Metadata dictionary (source, page number, etc.)
    /// </summary>
    public IDictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Chunk data with hierarchical parent context.
/// Used for FluxImprover's ParentChunkContext integration.
/// </summary>
public class ChunkDataWithContext : ChunkData
{
    /// <summary>
    /// Parent context for hierarchical enrichment
    /// </summary>
    public ParentEnrichmentData? ParentContext { get; init; }
}

/// <summary>
/// Parent chunk enrichment data for hierarchical context propagation.
/// Maps to FluxImprover's ParentChunkContext structure.
/// </summary>
public class ParentEnrichmentData
{
    /// <summary>
    /// Parent chunk identifier
    /// </summary>
    public string? ParentId { get; init; }

    /// <summary>
    /// Summary of the parent chunk (from FluxImprover enrichment)
    /// </summary>
    public string? ParentSummary { get; init; }

    /// <summary>
    /// Keywords extracted from parent chunk
    /// </summary>
    public IReadOnlyList<string>? ParentKeywords { get; init; }

    /// <summary>
    /// Document structure path (e.g., "Chapter 1 > Section 1.1")
    /// </summary>
    public string? ParentHeadingPath { get; init; }

    /// <summary>
    /// Hierarchy level (0 = root, 1 = first child level, etc.)
    /// </summary>
    public int HierarchyLevel { get; init; }
}

/// <summary>
/// Data prepared for contextual retrieval enrichment.
/// Used with FluxImprover's IContextualEnrichmentService.
/// </summary>
public class ContextualEnrichmentData
{
    /// <summary>
    /// Chunk to enrich
    /// </summary>
    public required ChunkData Chunk { get; init; }

    /// <summary>
    /// Full document text for context extraction
    /// </summary>
    public required string FullDocumentText { get; init; }

    /// <summary>
    /// Document metadata
    /// </summary>
    public DocumentContextMetadata? DocumentMetadata { get; init; }
}

/// <summary>
/// Document-level metadata for contextual enrichment
/// </summary>
public class DocumentContextMetadata
{
    /// <summary>
    /// Document title
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Document type (PDF, DOCX, etc.)
    /// </summary>
    public string? DocumentType { get; init; }

    /// <summary>
    /// Document language
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Total chunk count in document
    /// </summary>
    public int TotalChunks { get; init; }
}

/// <summary>
/// Options for batch hierarchical enrichment workflow
/// </summary>
public class HierarchicalEnrichmentOptions
{
    /// <summary>
    /// Process parent chunks before children (default: true)
    /// </summary>
    public bool EnrichParentsFirst { get; init; } = true;

    /// <summary>
    /// Propagate parent summary to children
    /// </summary>
    public bool PropagateParentSummary { get; init; } = true;

    /// <summary>
    /// Propagate parent keywords to children
    /// </summary>
    public bool PropagateParentKeywords { get; init; } = true;

    /// <summary>
    /// Maximum hierarchy depth to process (default: unlimited)
    /// </summary>
    public int? MaxDepth { get; init; }
}
