using Flux.Abstractions;

namespace FileFlux.Core;

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
