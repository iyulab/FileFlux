namespace FileFlux;

/// <summary>
/// Detects boundaries using statistical language modeling for topic transitions.
/// Measures prediction uncertainty to identify semantic boundaries.
/// Higher uncertainty indicates potential topic change or boundary.
/// </summary>
public interface IStatisticalBoundaryDetector
{
    /// <summary>
    /// Calculates the uncertainty score for a text segment.
    /// Score = exp(-1/n ∑log P(tj|t&lt;j))
    /// </summary>
    /// <param name="segment">The text segment to analyze</param>
    /// <param name="context">Optional preceding context</param>
    /// <param name="textCompletionService">LLM service for probability calculation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Uncertainty score (higher = more uncertain/boundary likely)</returns>
    Task<StatisticalBoundaryResult> CalculateUncertaintyAsync(
        string segment,
        string? context,
        IDocumentAnalysisService textCompletionService,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects if there's a topic boundary based on uncertainty threshold.
    /// </summary>
    /// <param name="uncertaintyScore">The calculated uncertainty score</param>
    /// <param name="threshold">Threshold for boundary detection (default: dynamic)</param>
    /// <returns>True if boundary detected</returns>
    bool IsTopicBoundary(double uncertaintyScore, double? threshold = null);

    /// <summary>
    /// Analyzes a sequence of segments and identifies boundaries using statistical modeling.
    /// </summary>
    /// <param name="segments">Text segments to analyze</param>
    /// <param name="textCompletionService">LLM service for probability calculation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Boundary points with uncertainty scores</returns>
    Task<IEnumerable<StatisticalBoundaryPoint>> DetectBoundariesAsync(
        IList<string> segments,
        IDocumentAnalysisService textCompletionService,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or sets the base uncertainty threshold.
    /// Dynamic adjustment may occur based on document characteristics.
    /// </summary>
    double BaseThreshold { get; set; }

    /// <summary>
    /// Gets or sets whether to use adaptive thresholding.
    /// </summary>
    bool UseAdaptiveThreshold { get; set; }
}

/// <summary>
/// Result of statistical boundary analysis.
/// </summary>
public class StatisticalBoundaryResult
{
    /// <summary>
    /// The calculated uncertainty score.
    /// </summary>
    public double UncertaintyScore { get; set; }

    /// <summary>
    /// Token-level probabilities if available.
    /// </summary>
    public List<TokenProbability> TokenProbabilities { get; set; } = new();

    /// <summary>
    /// Average log probability.
    /// </summary>
    public double AverageLogProbability { get; set; }

    /// <summary>
    /// Number of tokens analyzed.
    /// </summary>
    public int TokenCount { get; set; }

    /// <summary>
    /// Confidence in the uncertainty calculation (0-1).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Represents probability for a single token.
/// </summary>
public class TokenProbability
{
    /// <summary>
    /// The token text.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Log probability of the token.
    /// </summary>
    public double LogProbability { get; set; }

    /// <summary>
    /// Probability (exp of log probability).
    /// </summary>
    public double Probability => Math.Exp(LogProbability);

    /// <summary>
    /// Position in the sequence.
    /// </summary>
    public int Position { get; set; }
}

/// <summary>
/// Represents a boundary point detected by statistical analysis.
/// </summary>
public class StatisticalBoundaryPoint
{
    /// <summary>
    /// Index of the segment after which boundary occurs.
    /// </summary>
    public int SegmentIndex { get; set; }

    /// <summary>
    /// Uncertainty score at this point.
    /// </summary>
    public double UncertaintyScore { get; set; }

    /// <summary>
    /// Change in uncertainty from previous segment.
    /// </summary>
    public double UncertaintyDelta { get; set; }

    /// <summary>
    /// Whether this is a boundary.
    /// </summary>
    public bool IsBoundary { get; set; }

    /// <summary>
    /// Confidence in boundary detection (0-1).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// The threshold used for this detection.
    /// </summary>
    public double ThresholdUsed { get; set; }
}
