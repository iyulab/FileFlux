namespace FileFlux.Domain;

/// <summary>
/// Stage 3 output: RAG-optimized document chunk
/// </summary>
public class DocumentChunk
{
    /// <summary>
    /// Unique chunk ID
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Reference to parsing stage
    /// </summary>
    public Guid ParsedId { get; set; }

    /// <summary>
    /// Reference to extraction stage (for full traceability)
    /// </summary>
    public Guid RawId { get; set; }

    /// <summary>
    /// Chunk content text
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Chunk index in sequence
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Source location in original document
    /// </summary>
    public SourceLocation Location { get; set; } = new();

    /// <summary>
    /// Document metadata
    /// </summary>
    public DocumentMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Chunk quality score (0.0 - 1.0)
    /// </summary>
    public double Quality { get; set; } = 0.5;

    /// <summary>
    /// Semantic importance score (0.0 - 1.0)
    /// </summary>
    public double Importance { get; set; } = 0.5;

    /// <summary>
    /// Information density (0.0 - 1.0)
    /// </summary>
    public double Density { get; set; } = 0.5;

    /// <summary>
    /// Chunking strategy used
    /// </summary>
    public string Strategy { get; set; } = string.Empty;

    /// <summary>
    /// Estimated token count
    /// </summary>
    public int Tokens { get; set; }

    /// <summary>
    /// Chunk creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Custom properties for extensibility
    /// </summary>
    public Dictionary<string, object> Props { get; set; } = new();
}

/// <summary>
/// Source location in original document
/// </summary>
public class SourceLocation
{
    /// <summary>
    /// Start character position
    /// </summary>
    public int StartChar { get; set; }

    /// <summary>
    /// End character position
    /// </summary>
    public int EndChar { get; set; }

    /// <summary>
    /// Start page number (if applicable)
    /// </summary>
    public int? StartPage { get; set; }

    /// <summary>
    /// End page number (if applicable)
    /// </summary>
    public int? EndPage { get; set; }

    /// <summary>
    /// Section path (e.g., "Introduction/Background")
    /// </summary>
    public string? Section { get; set; }
}
