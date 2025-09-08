namespace FileFlux;

/// <summary>
/// Combines statistical language modeling and embedding-based boundary detection.
/// Implements research-based multi-signal approach for improved accuracy.
/// </summary>
public interface IHybridBoundaryDetector
{
    /// <summary>
    /// Detects boundaries using both statistical and embedding analysis.
    /// Hybrid Score = α * Statistical + (1-α) * (1 - similarity)
    /// </summary>
    /// <param name="segment1">First segment</param>
    /// <param name="segment2">Second segment</param>
    /// <param name="textCompletionService">LLM service for statistical analysis</param>
    /// <param name="embeddingService">Embedding service for similarity</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Hybrid boundary detection result</returns>
    Task<HybridBoundaryResult> DetectBoundaryAsync(
        string segment1,
        string segment2,
        ITextCompletionService textCompletionService,
        IEmbeddingService embeddingService,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates the hybrid score combining statistical uncertainty and similarity.
    /// </summary>
    /// <param name="statisticalScore">Normalized statistical score (0-1)</param>
    /// <param name="similarity">Cosine similarity score (0-1)</param>
    /// <returns>Combined hybrid score</returns>
    double CalculateHybridScore(double statisticalScore, double similarity);

    /// <summary>
    /// Detects all boundaries in a document using hybrid approach.
    /// </summary>
    /// <param name="segments">Document segments</param>
    /// <param name="textCompletionService">LLM service</param>
    /// <param name="embeddingService">Embedding service</param>
    /// <param name="options">Detection options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of hybrid boundary points</returns>
    Task<IEnumerable<HybridBoundaryPoint>> DetectBoundariesAsync(
        IList<string> segments,
        ITextCompletionService textCompletionService,
        IEmbeddingService embeddingService,
        HybridDetectionOptions? options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or sets the alpha weight for statistical score (default: 0.6).
    /// Higher alpha gives more weight to statistical analysis.
    /// </summary>
    double Alpha { get; set; }

    /// <summary>
    /// Gets or sets the boundary threshold (default: 0.7).
    /// </summary>
    double BoundaryThreshold { get; set; }
}

/// <summary>
/// Options for hybrid boundary detection.
/// </summary>
public class HybridDetectionOptions
{
    /// <summary>
    /// Alpha weight for statistical score vs similarity (0-1).
    /// Default: 0.6 (60% statistical, 40% similarity).
    /// </summary>
    public double Alpha { get; set; } = 0.6;

    /// <summary>
    /// Threshold for boundary detection.
    /// </summary>
    public double BoundaryThreshold { get; set; } = 0.7;

    /// <summary>
    /// Whether to use adaptive thresholding.
    /// </summary>
    public bool UseAdaptiveThreshold { get; set; } = true;

    /// <summary>
    /// Minimum segment length to consider for boundaries.
    /// </summary>
    public int MinSegmentLength { get; set; } = 50;

    /// <summary>
    /// Whether to merge nearby boundaries.
    /// </summary>
    public bool MergeNearbyBoundaries { get; set; } = true;

    /// <summary>
    /// Distance threshold for merging boundaries.
    /// </summary>
    public int MergeDistance { get; set; } = 2;
}

/// <summary>
/// Result of hybrid boundary detection.
/// </summary>
public class HybridBoundaryResult
{
    /// <summary>
    /// Whether a boundary was detected.
    /// </summary>
    public bool IsBoundary { get; set; }

    /// <summary>
    /// The hybrid score (0-1, higher = stronger boundary).
    /// </summary>
    public double HybridScore { get; set; }

    /// <summary>
    /// Statistical component of the score.
    /// </summary>
    public double StatisticalScore { get; set; }

    /// <summary>
    /// Similarity component of the score.
    /// </summary>
    public double SimilarityScore { get; set; }

    /// <summary>
    /// Raw statistical score value.
    /// </summary>
    public double RawStatisticalScore { get; set; }

    /// <summary>
    /// Raw similarity value.
    /// </summary>
    public double RawSimilarity { get; set; }

    /// <summary>
    /// Confidence in the detection (0-1).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Type of boundary detected.
    /// </summary>
    public BoundaryType BoundaryType { get; set; }

    /// <summary>
    /// Additional decision metadata.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Represents a boundary point detected by hybrid analysis.
/// </summary>
public class HybridBoundaryPoint
{
    /// <summary>
    /// Segment index after which boundary occurs.
    /// </summary>
    public int SegmentIndex { get; set; }

    /// <summary>
    /// The hybrid score at this point.
    /// </summary>
    public double HybridScore { get; set; }

    /// <summary>
    /// Statistical score contribution.
    /// </summary>
    public double StatisticalContribution { get; set; }

    /// <summary>
    /// Similarity contribution.
    /// </summary>
    public double SimilarityContribution { get; set; }

    /// <summary>
    /// Whether this is confirmed as boundary.
    /// </summary>
    public bool IsBoundary { get; set; }

    /// <summary>
    /// Confidence level (0-1).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Type of boundary.
    /// </summary>
    public BoundaryType Type { get; set; }

    /// <summary>
    /// Reason for boundary detection.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}