namespace FileFlux.Core;

/// <summary>
/// Enriched chunk contract for FluxIndex integration.
/// Provides standardized metadata required by RAG systems.
/// </summary>
public interface IEnrichedChunk
{
    /// <summary>
    /// Chunk content text.
    /// </summary>
    string Content { get; }

    /// <summary>
    /// Unique chunk identifier.
    /// </summary>
    string ChunkId { get; }

    /// <summary>
    /// Zero-based chunk index in document.
    /// </summary>
    int ChunkIndex { get; }

    /// <summary>
    /// Hierarchical heading path from document root.
    /// Example: ["Chapter 1", "Section 1.2", "1.2.1 Purpose"]
    /// </summary>
    IReadOnlyList<string> HeadingPath { get; }

    /// <summary>
    /// Current section title (last element of HeadingPath).
    /// </summary>
    string? SectionTitle { get; }

    /// <summary>
    /// Start page number (1-based, null if not applicable).
    /// </summary>
    int? StartPage { get; }

    /// <summary>
    /// End page number (for chunks spanning multiple pages).
    /// </summary>
    int? EndPage { get; }

    /// <summary>
    /// Overall chunk quality score (0.0 - 1.0).
    /// </summary>
    double Quality { get; }

    /// <summary>
    /// Context dependency score (0.0 - 1.0).
    /// Higher values indicate more reliance on surrounding context.
    /// Used to determine whether LLM-based contextual header is needed.
    /// </summary>
    double ContextDependency { get; }

    /// <summary>
    /// Estimated token count.
    /// </summary>
    int TokenCount { get; }

    /// <summary>
    /// Source document metadata.
    /// </summary>
    ISourceMetadata Source { get; }
}

/// <summary>
/// Source document metadata for traceability and filtering.
/// </summary>
public interface ISourceMetadata
{
    /// <summary>
    /// Unique source document identifier.
    /// </summary>
    string SourceId { get; }

    /// <summary>
    /// Document type (PDF, DOCX, MD, etc.).
    /// </summary>
    string SourceType { get; }

    /// <summary>
    /// Document title.
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Original file path (if available).
    /// </summary>
    string? FilePath { get; }

    /// <summary>
    /// Document creation timestamp.
    /// </summary>
    DateTime CreatedAt { get; }

    /// <summary>
    /// Detected language (ISO 639-1 code: "ko", "en", "ja").
    /// </summary>
    string Language { get; }

    /// <summary>
    /// Language detection confidence (0.0 - 1.0).
    /// </summary>
    double LanguageConfidence { get; }

    /// <summary>
    /// Total word count in document.
    /// </summary>
    int WordCount { get; }

    /// <summary>
    /// Total number of chunks generated.
    /// </summary>
    int ChunkCount { get; }

    /// <summary>
    /// Total page count (if applicable).
    /// </summary>
    int? PageCount { get; }
}

/// <summary>
/// Hierarchical chunk interface for parent-child relationships.
/// Enables multi-level granularity: small chunks for retrieval, larger parent chunks for context.
/// </summary>
public interface IHierarchicalChunk : IEnrichedChunk
{
    /// <summary>
    /// Parent chunk ID (null for root/document-level chunks).
    /// </summary>
    string? ParentChunkId { get; }

    /// <summary>
    /// Child chunk IDs (empty list for leaf chunks).
    /// </summary>
    IReadOnlyList<string> ChildChunkIds { get; }

    /// <summary>
    /// Hierarchy level in document structure.
    /// 0 = document level (largest context)
    /// 1 = section level
    /// 2 = subsection level
    /// 3 = paragraph level (smallest, most granular)
    /// </summary>
    int HierarchyLevel { get; }

    /// <summary>
    /// Type of chunk in the hierarchy.
    /// </summary>
    HierarchyChunkType ChunkType { get; }

    /// <summary>
    /// Merge group ID for auto-merging related chunks during retrieval.
    /// Chunks with the same merge group can be combined for richer context.
    /// </summary>
    string? MergeGroupId { get; }
}

/// <summary>
/// Defines the type of chunk within a hierarchical structure.
/// </summary>
public enum HierarchyChunkType
{
    /// <summary>
    /// Root/document-level chunk containing overview.
    /// </summary>
    Root = 0,

    /// <summary>
    /// Parent chunk that has children (section or subsection header with summary).
    /// </summary>
    Parent = 1,

    /// <summary>
    /// Leaf chunk with no children (actual content for retrieval).
    /// </summary>
    Leaf = 2,

    /// <summary>
    /// Summary chunk aggregating child content.
    /// </summary>
    Summary = 3
}
