namespace FileFlux.Core;

/// <summary>
/// Inter-chunk relationship graph for a document.
/// Built during Enrich stage after all chunks are available.
/// </summary>
public class DocumentGraph
{
    /// <summary>
    /// Document ID this graph belongs to.
    /// </summary>
    public Guid DocumentId { get; init; }

    /// <summary>
    /// Chunk nodes in the graph.
    /// </summary>
    public IReadOnlyList<ChunkNode> Nodes { get; init; } = [];

    /// <summary>
    /// Edges representing relationships between chunks.
    /// </summary>
    public IReadOnlyList<ChunkEdge> Edges { get; init; } = [];

    /// <summary>
    /// Graph build timestamp.
    /// </summary>
    public DateTime BuiltAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Total node count.
    /// </summary>
    public int NodeCount => Nodes.Count;

    /// <summary>
    /// Total edge count.
    /// </summary>
    public int EdgeCount => Edges.Count;

    /// <summary>
    /// Get edges from a specific chunk.
    /// </summary>
    public IEnumerable<ChunkEdge> GetEdgesFrom(Guid chunkId) =>
        Edges.Where(e => e.SourceId == chunkId);

    /// <summary>
    /// Get edges to a specific chunk.
    /// </summary>
    public IEnumerable<ChunkEdge> GetEdgesTo(Guid chunkId) =>
        Edges.Where(e => e.TargetId == chunkId);

    /// <summary>
    /// Get all connected chunk IDs for a given chunk.
    /// </summary>
    public IEnumerable<Guid> GetConnectedChunks(Guid chunkId) =>
        GetEdgesFrom(chunkId).Select(e => e.TargetId)
            .Concat(GetEdgesTo(chunkId).Select(e => e.SourceId))
            .Distinct();

    /// <summary>
    /// Get edges by type.
    /// </summary>
    public IEnumerable<ChunkEdge> GetEdgesByType(EdgeType type) =>
        Edges.Where(e => e.Type == type);

    /// <summary>
    /// Find path between two chunks (BFS).
    /// </summary>
    public IReadOnlyList<Guid>? FindPath(Guid fromId, Guid toId)
    {
        if (fromId == toId)
            return [fromId];

        var visited = new HashSet<Guid> { fromId };
        var queue = new Queue<List<Guid>>();
        queue.Enqueue([fromId]);

        while (queue.Count > 0)
        {
            var path = queue.Dequeue();
            var current = path[^1];

            foreach (var neighbor in GetConnectedChunks(current))
            {
                if (neighbor == toId)
                {
                    path.Add(neighbor);
                    return path;
                }

                if (visited.Add(neighbor))
                {
                    var newPath = new List<Guid>(path) { neighbor };
                    queue.Enqueue(newPath);
                }
            }
        }

        return null;
    }
}

/// <summary>
/// Node representing a chunk in the document graph.
/// </summary>
public class ChunkNode
{
    /// <summary>
    /// Chunk ID.
    /// </summary>
    public Guid ChunkId { get; init; }

    /// <summary>
    /// Sequential index in document.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Summary of chunk content.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Keywords extracted from chunk.
    /// </summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>
    /// Structure IDs contained in this chunk.
    /// </summary>
    public IReadOnlyList<string> StructureIds { get; init; } = [];

    /// <summary>
    /// Section path in document hierarchy.
    /// </summary>
    public IReadOnlyList<string> SectionPath { get; init; } = [];

    /// <summary>
    /// Position information.
    /// </summary>
    public ChunkPosition Position { get; init; } = new();
}

/// <summary>
/// Chunk position in document structure.
/// </summary>
public class ChunkPosition
{
    /// <summary>
    /// Sequential order.
    /// </summary>
    public int Sequence { get; init; }

    /// <summary>
    /// Parent chunk ID (for hierarchical structure).
    /// </summary>
    public Guid? ParentId { get; init; }

    /// <summary>
    /// Previous chunk ID.
    /// </summary>
    public Guid? PreviousId { get; init; }

    /// <summary>
    /// Next chunk ID.
    /// </summary>
    public Guid? NextId { get; init; }

    /// <summary>
    /// Depth in hierarchy (0 = root level).
    /// </summary>
    public int Depth { get; init; }
}

/// <summary>
/// Edge representing relationship between two chunks.
/// </summary>
public class ChunkEdge
{
    /// <summary>
    /// Source chunk ID.
    /// </summary>
    public Guid SourceId { get; init; }

    /// <summary>
    /// Target chunk ID.
    /// </summary>
    public Guid TargetId { get; init; }

    /// <summary>
    /// Type of relationship.
    /// </summary>
    public EdgeType Type { get; init; }

    /// <summary>
    /// Relationship strength (0.0 - 1.0).
    /// </summary>
    public double Weight { get; init; } = 1.0;

    /// <summary>
    /// Description of the relationship.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>
    /// Additional edge properties.
    /// </summary>
    public Dictionary<string, object> Properties { get; init; } = new();
}

/// <summary>
/// Type of edge relationship between chunks.
/// </summary>
public enum EdgeType
{
    /// <summary>
    /// Sequential order (chunk A comes before chunk B).
    /// </summary>
    Sequential,

    /// <summary>
    /// Hierarchical relationship (chunk A is parent of chunk B).
    /// </summary>
    Hierarchical,

    /// <summary>
    /// Cross-reference (chunk A references chunk B).
    /// </summary>
    Reference,

    /// <summary>
    /// Semantic similarity (chunks share similar meaning).
    /// </summary>
    Semantic,

    /// <summary>
    /// Shared entity (chunks mention same entity).
    /// </summary>
    SharedEntity,

    /// <summary>
    /// Shared structure (chunks reference same table/code/list).
    /// </summary>
    SharedStructure,

    /// <summary>
    /// Continuation (chunk B continues topic from chunk A).
    /// </summary>
    Continuation,

    /// <summary>
    /// Contrast (chunk B presents opposing view to chunk A).
    /// </summary>
    Contrast,

    /// <summary>
    /// Example (chunk B provides example for chunk A).
    /// </summary>
    Example,

    /// <summary>
    /// Definition (chunk B defines concept from chunk A).
    /// </summary>
    Definition
}
