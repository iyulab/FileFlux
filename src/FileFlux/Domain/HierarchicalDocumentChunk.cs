using FileFlux.Core;

namespace FileFlux.Domain;

/// <summary>
/// Hierarchical document chunk with parent-child relationships
/// Enables multi-level granularity for RAG retrieval
/// </summary>
public class HierarchicalDocumentChunk : DocumentChunk, IHierarchicalChunk
{
    private List<string> _childChunkIds = new();

    /// <summary>
    /// Parent chunk ID (null for root/document-level chunks)
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// Child chunk IDs
    /// </summary>
    public List<string> ChildIds
    {
        get => _childChunkIds;
        set => _childChunkIds = value ?? new List<string>();
    }

    /// <summary>
    /// Hierarchy level (0=document, 1=section, 2=subsection, 3=paragraph)
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Type of chunk in hierarchy
    /// </summary>
    public HierarchyChunkType Type { get; set; } = HierarchyChunkType.Leaf;

    /// <summary>
    /// Merge group ID for auto-merging related chunks
    /// </summary>
    public string? GroupId { get; set; }

    #region IHierarchicalChunk Implementation

    string? IHierarchicalChunk.ParentChunkId => ParentId;

    IReadOnlyList<string> IHierarchicalChunk.ChildChunkIds => ChildIds.AsReadOnly();

    int IHierarchicalChunk.HierarchyLevel => Level;

    HierarchyChunkType IHierarchicalChunk.ChunkType => Type;

    string? IHierarchicalChunk.MergeGroupId => GroupId;

    #endregion

    /// <summary>
    /// Add a child chunk ID
    /// </summary>
    public void AddChild(string childId)
    {
        if (!string.IsNullOrWhiteSpace(childId) && !_childChunkIds.Contains(childId))
        {
            _childChunkIds.Add(childId);
        }
    }

    /// <summary>
    /// Remove a child chunk ID
    /// </summary>
    public bool RemoveChild(string childId)
    {
        return _childChunkIds.Remove(childId);
    }

    /// <summary>
    /// Check if this chunk has children
    /// </summary>
    public bool HasChildren => _childChunkIds.Count > 0;

    /// <summary>
    /// Check if this is a root/top-level chunk
    /// </summary>
    public bool IsRoot => string.IsNullOrEmpty(ParentId);

    /// <summary>
    /// Check if this is a leaf chunk (no children)
    /// </summary>
    public bool IsLeaf => _childChunkIds.Count == 0;

    /// <summary>
    /// Create a new hierarchical chunk from an existing DocumentChunk
    /// </summary>
    public static HierarchicalDocumentChunk FromDocumentChunk(DocumentChunk source, int hierarchyLevel = 0)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new HierarchicalDocumentChunk
        {
            Id = source.Id,
            ParsedId = source.ParsedId,
            RawId = source.RawId,
            Content = source.Content,
            Index = source.Index,
            Location = source.Location,
            Metadata = source.Metadata,
            Quality = source.Quality,
            Importance = source.Importance,
            Density = source.Density,
            Strategy = source.Strategy,
            Tokens = source.Tokens,
            Props = new Dictionary<string, object>(source.Props),
            ContextDependency = source.ContextDependency,
            SourceInfo = source.SourceInfo,
            Level = hierarchyLevel,
            Type = HierarchyChunkType.Leaf
        };
    }
}
