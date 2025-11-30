namespace FileFlux.Core;

/// <summary>
/// Stage 3 output: RAG-optimized document chunk
/// Implements IEnrichedChunk for FluxIndex integration
/// </summary>
public class DocumentChunk : IEnrichedChunk
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

    /// <summary>
    /// Context dependency score (0.0 - 1.0)
    /// </summary>
    public double ContextDependency { get; set; } = 0.0;

    /// <summary>
    /// Source metadata for the document this chunk belongs to
    /// </summary>
    public SourceMetadataInfo SourceInfo { get; set; } = new();

    #region IEnrichedChunk Implementation

    string IEnrichedChunk.ChunkId => Id.ToString();
    int IEnrichedChunk.ChunkIndex => Index;
    IReadOnlyList<string> IEnrichedChunk.HeadingPath => Location.HeadingPath;
    string? IEnrichedChunk.SectionTitle => Location.HeadingPath.Count > 0
        ? Location.HeadingPath[^1]
        : Location.Section;
    int? IEnrichedChunk.StartPage => Location.StartPage;
    int? IEnrichedChunk.EndPage => Location.EndPage;
    int IEnrichedChunk.TokenCount => Tokens;
    ISourceMetadata IEnrichedChunk.Source => SourceInfo;

    #endregion
}

/// <summary>
/// Source document metadata implementation
/// </summary>
public class SourceMetadataInfo : ISourceMetadata
{
    public string SourceId { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Language { get; set; } = "en";
    public double LanguageConfidence { get; set; } = 0.0;
    public int WordCount { get; set; }
    public int ChunkCount { get; set; }
    public int? PageCount { get; set; }
}

/// <summary>
/// Source location in original document
/// </summary>
public class SourceLocation
{
    public int StartChar { get; set; }
    public int EndChar { get; set; }
    public int? StartPage { get; set; }
    public int? EndPage { get; set; }
    public string? Section { get; set; }
    public List<string> HeadingPath { get; set; } = new();
}
