namespace FileFlux;

/// <summary>
/// Detects semantic boundaries between text segments using embeddings.
/// </summary>
public interface ISemanticBoundaryDetector
{
    /// <summary>
    /// Detects if there is a semantic boundary between two text segments.
    /// </summary>
    /// <param name="segment1">First text segment</param>
    /// <param name="segment2">Second text segment</param>
    /// <param name="embeddingService">The embedding service to use</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Boundary detection result with confidence score</returns>
    Task<BoundaryDetectionResult> DetectBoundaryAsync(
        string segment1,
        string segment2,
        IEmbeddingService embeddingService,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes multiple segments and identifies all boundary points.
    /// </summary>
    /// <param name="segments">Collection of text segments</param>
    /// <param name="embeddingService">The embedding service to use</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of boundary indices with confidence scores</returns>
    Task<IEnumerable<BoundaryPoint>> DetectBoundariesAsync(
        IList<string> segments,
        IEmbeddingService embeddingService,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or sets the similarity threshold for boundary detection.
    /// Lower similarity indicates a boundary (default: 0.7).
    /// </summary>
    double SimilarityThreshold { get; set; }
}

/// <summary>
/// Result of boundary detection between two segments.
/// </summary>
public class BoundaryDetectionResult
{
    /// <summary>
    /// Whether a boundary was detected.
    /// </summary>
    public bool IsBoundary { get; set; }

    /// <summary>
    /// The similarity score between segments (0-1).
    /// </summary>
    public double Similarity { get; set; }

    /// <summary>
    /// Confidence in the boundary detection (0-1).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// The type of boundary detected.
    /// </summary>
    public BoundaryType Type { get; set; }

    /// <summary>
    /// Additional metadata about the boundary.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Represents a boundary point in a sequence of segments.
/// </summary>
public class BoundaryPoint
{
    /// <summary>
    /// The index after which the boundary occurs.
    /// </summary>
    public int SegmentIndex { get; set; }

    /// <summary>
    /// The similarity score at this boundary.
    /// </summary>
    public double Similarity { get; set; }

    /// <summary>
    /// The confidence score for this boundary.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// The type of boundary.
    /// </summary>
    public BoundaryType Type { get; set; }
}

/// <summary>
/// Types of semantic boundaries.
/// </summary>
public enum BoundaryType
{
    /// <summary>
    /// No boundary detected
    /// </summary>
    None,

    /// <summary>
    /// Topic change boundary
    /// </summary>
    TopicChange,

    /// <summary>
    /// Section boundary (heading, chapter)
    /// </summary>
    Section,

    /// <summary>
    /// Paragraph boundary
    /// </summary>
    Paragraph,

    /// <summary>
    /// Sentence boundary
    /// </summary>
    Sentence,

    /// <summary>
    /// Code block boundary
    /// </summary>
    CodeBlock,

    /// <summary>
    /// Table boundary
    /// </summary>
    Table,

    /// <summary>
    /// List boundary
    /// </summary>
    List
}
