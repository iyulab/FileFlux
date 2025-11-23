using FileFlux.Core;

namespace FileFlux.Domain;

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
    /// Higher values indicate more reliance on surrounding context
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
    /// <summary>
    /// Unique source document identifier
    /// </summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// Document type (PDF, DOCX, MD, etc.)
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// Document title
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Original file path
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Document creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Detected language (ISO 639-1)
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Language detection confidence
    /// </summary>
    public double LanguageConfidence { get; set; } = 0.0;

    /// <summary>
    /// Total word count
    /// </summary>
    public int WordCount { get; set; }

    /// <summary>
    /// Total chunk count
    /// </summary>
    public int ChunkCount { get; set; }

    /// <summary>
    /// Total page count
    /// </summary>
    public int? PageCount { get; set; }
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
    /// Section path (legacy - use HeadingPath instead)
    /// </summary>
    public string? Section { get; set; }

    /// <summary>
    /// Hierarchical heading path from document root
    /// Example: ["1장 서론", "1.2 배경", "1.2.1 연구 목적"]
    /// </summary>
    public List<string> HeadingPath { get; set; } = new();
}
