using FileFlux.Domain;

namespace FileFlux;

/// <summary>
/// Filters and scores chunks using LLM-based relevance assessment.
/// Implements 3-stage assessment: initial, self-reflection, critic validation.
/// Target: 10 percentage point accuracy improvement.
/// </summary>
public interface ILLMChunkFilter
{
    /// <summary>
    /// Filters chunks based on relevance and quality using LLM assessment.
    /// </summary>
    /// <param name="chunks">Chunks to filter</param>
    /// <param name="query">Query or context for relevance assessment</param>
    /// <param name="textCompletionService">LLM service for assessment</param>
    /// <param name="options">Filtering options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Filtered and scored chunks</returns>
    Task<IEnumerable<FilteredChunk>> FilterChunksAsync(
        IEnumerable<DocumentChunk> chunks,
        string? query,
        ITextCompletionService textCompletionService,
        ChunkFilterOptions? options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs 3-stage relevance assessment on a single chunk.
    /// </summary>
    /// <param name="chunk">Chunk to assess</param>
    /// <param name="query">Query context</param>
    /// <param name="textCompletionService">LLM service</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Assessment result with scores</returns>
    Task<ChunkAssessment> AssessChunkAsync(
        DocumentChunk chunk,
        string? query,
        ITextCompletionService textCompletionService,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or sets the minimum relevance threshold (0-1).
    /// </summary>
    double RelevanceThreshold { get; set; }

    /// <summary>
    /// Gets or sets whether to use critic validation stage.
    /// </summary>
    bool UseCriticValidation { get; set; }
}

/// <summary>
/// Options for chunk filtering.
/// </summary>
public class ChunkFilterOptions
{
    /// <summary>
    /// Minimum relevance score to retain chunk (0-1).
    /// </summary>
    public double MinRelevanceScore { get; set; } = 0.7;

    /// <summary>
    /// Maximum number of chunks to return.
    /// </summary>
    public int? MaxChunks { get; set; }

    /// <summary>
    /// Whether to use self-reflection stage.
    /// </summary>
    public bool UseSelfReflection { get; set; } = true;

    /// <summary>
    /// Whether to use critic validation stage.
    /// </summary>
    public bool UseCriticValidation { get; set; } = true;

    /// <summary>
    /// Quality weight vs relevance weight (0-1).
    /// 0 = pure relevance, 1 = pure quality.
    /// </summary>
    public double QualityWeight { get; set; } = 0.3;

    /// <summary>
    /// Whether to preserve document order.
    /// </summary>
    public bool PreserveOrder { get; set; } = false;

    /// <summary>
    /// Specific criteria for filtering.
    /// </summary>
    public List<FilterCriterion> Criteria { get; set; } = new();
}

/// <summary>
/// Represents a filtering criterion.
/// </summary>
public class FilterCriterion
{
    /// <summary>
    /// Type of criterion.
    /// </summary>
    public CriterionType Type { get; set; }

    /// <summary>
    /// Value or threshold for the criterion.
    /// </summary>
    public object Value { get; set; } = new();

    /// <summary>
    /// Weight of this criterion (0-1).
    /// </summary>
    public double Weight { get; set; } = 1.0;

    /// <summary>
    /// Whether this criterion is mandatory.
    /// </summary>
    public bool IsMandatory { get; set; }
}

/// <summary>
/// Types of filtering criteria.
/// </summary>
public enum CriterionType
{
    /// <summary>
    /// Keyword presence
    /// </summary>
    KeywordPresence,

    /// <summary>
    /// Topic relevance
    /// </summary>
    TopicRelevance,

    /// <summary>
    /// Information density
    /// </summary>
    InformationDensity,

    /// <summary>
    /// Factual content
    /// </summary>
    FactualContent,

    /// <summary>
    /// Recency/temporal relevance
    /// </summary>
    Recency,

    /// <summary>
    /// Source credibility
    /// </summary>
    SourceCredibility,

    /// <summary>
    /// Completeness
    /// </summary>
    Completeness
}

/// <summary>
/// Result of chunk filtering with scores.
/// </summary>
public class FilteredChunk
{
    /// <summary>
    /// The original chunk.
    /// </summary>
    public DocumentChunk Chunk { get; set; } = new();

    /// <summary>
    /// Overall relevance score (0-1).
    /// </summary>
    public double RelevanceScore { get; set; }

    /// <summary>
    /// Quality score (0-1).
    /// </summary>
    public double QualityScore { get; set; }

    /// <summary>
    /// Combined score considering weights.
    /// </summary>
    public double CombinedScore { get; set; }

    /// <summary>
    /// Whether chunk passed filtering.
    /// </summary>
    public bool Passed { get; set; }

    /// <summary>
    /// Detailed assessment information.
    /// </summary>
    public ChunkAssessment? Assessment { get; set; }

    /// <summary>
    /// Reason for filtering decision.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Detailed assessment of a chunk.
/// </summary>
public class ChunkAssessment
{
    /// <summary>
    /// Initial assessment score.
    /// </summary>
    public double InitialScore { get; set; }

    /// <summary>
    /// Score after self-reflection.
    /// </summary>
    public double? ReflectionScore { get; set; }

    /// <summary>
    /// Score after critic validation.
    /// </summary>
    public double? CriticScore { get; set; }

    /// <summary>
    /// Final consolidated score.
    /// </summary>
    public double FinalScore { get; set; }

    /// <summary>
    /// Confidence in the assessment (0-1).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Key factors in the assessment.
    /// </summary>
    public List<AssessmentFactor> Factors { get; set; } = new();

    /// <summary>
    /// Suggestions for improvement.
    /// </summary>
    public List<string> Suggestions { get; set; } = new();

    /// <summary>
    /// Stage-by-stage reasoning.
    /// </summary>
    public Dictionary<string, string> Reasoning { get; set; } = new();
}

/// <summary>
/// Factor contributing to assessment.
/// </summary>
public class AssessmentFactor
{
    /// <summary>
    /// Name of the factor.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Score contribution (-1 to 1).
    /// </summary>
    public double Contribution { get; set; }

    /// <summary>
    /// Explanation of the factor.
    /// </summary>
    public string Explanation { get; set; } = string.Empty;
}