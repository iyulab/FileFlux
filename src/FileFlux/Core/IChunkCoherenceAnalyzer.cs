using FileFlux.Domain;

namespace FileFlux;

/// <summary>
/// Analyzes the semantic coherence of document chunks using embeddings.
/// </summary>
public interface IChunkCoherenceAnalyzer
{
    /// <summary>
    /// Calculates the coherence score for a document chunk.
    /// </summary>
    /// <param name="chunk">The document chunk to analyze</param>
    /// <param name="embeddingService">The embedding service to use</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Coherence analysis result</returns>
    Task<CoherenceAnalysisResult> AnalyzeCoherenceAsync(
        DocumentChunk chunk,
        IEmbeddingService embeddingService,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes coherence for multiple chunks and provides comparative analysis.
    /// </summary>
    /// <param name="chunks">Collection of chunks to analyze</param>
    /// <param name="embeddingService">The embedding service to use</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of coherence results with comparative metrics</returns>
    Task<IEnumerable<CoherenceAnalysisResult>> AnalyzeBatchCoherenceAsync(
        IEnumerable<DocumentChunk> chunks,
        IEmbeddingService embeddingService,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates the optimal chunk boundaries based on coherence.
    /// </summary>
    /// <param name="content">The content to analyze</param>
    /// <param name="embeddingService">The embedding service to use</param>
    /// <param name="options">Chunking options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Suggested chunk boundaries with coherence scores</returns>
    Task<IEnumerable<ChunkBoundary>> SuggestBoundariesAsync(
        string content,
        IEmbeddingService embeddingService,
        ChunkingOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of coherence analysis for a chunk.
/// </summary>
public class CoherenceAnalysisResult
{
    /// <summary>
    /// The overall coherence score (0-1, higher is better).
    /// </summary>
    public double CoherenceScore { get; set; }

    /// <summary>
    /// Average similarity between sentences within the chunk.
    /// </summary>
    public double IntraSentenceSimilarity { get; set; }

    /// <summary>
    /// Standard deviation of sentence similarities.
    /// </summary>
    public double SimilarityVariance { get; set; }

    /// <summary>
    /// The cohesion level of the chunk.
    /// </summary>
    public CohesionLevel Level { get; set; }

    /// <summary>
    /// Specific areas where coherence is weak.
    /// </summary>
    public List<CoherenceIssue> Issues { get; set; } = new();

    /// <summary>
    /// Suggestions for improving coherence.
    /// </summary>
    public List<string> Suggestions { get; set; } = new();

    /// <summary>
    /// The chunk that was analyzed.
    /// </summary>
    public DocumentChunk? AnalyzedChunk { get; set; }
}

/// <summary>
/// Represents a suggested chunk boundary.
/// </summary>
public class ChunkBoundary
{
    /// <summary>
    /// The start position of the chunk.
    /// </summary>
    public int StartPosition { get; set; }

    /// <summary>
    /// The end position of the chunk.
    /// </summary>
    public int EndPosition { get; set; }

    /// <summary>
    /// The coherence score for this chunk.
    /// </summary>
    public double CoherenceScore { get; set; }

    /// <summary>
    /// The reason for this boundary.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// The content preview of this chunk.
    /// </summary>
    public string ContentPreview { get; set; } = string.Empty;
}

/// <summary>
/// Represents a coherence issue in a chunk.
/// </summary>
public class CoherenceIssue
{
    /// <summary>
    /// The type of coherence issue.
    /// </summary>
    public CoherenceIssueType Type { get; set; }

    /// <summary>
    /// Description of the issue.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The position in the chunk where the issue occurs.
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// The severity of the issue.
    /// </summary>
    public IssueSeverity Severity { get; set; }
}

/// <summary>
/// Types of coherence issues.
/// </summary>
public enum CoherenceIssueType
{
    /// <summary>
    /// Abrupt topic change
    /// </summary>
    TopicShift,

    /// <summary>
    /// Missing context
    /// </summary>
    MissingContext,

    /// <summary>
    /// Incomplete thought
    /// </summary>
    IncompleteThought,

    /// <summary>
    /// Mixed content types
    /// </summary>
    MixedContent,

    /// <summary>
    /// Broken reference
    /// </summary>
    BrokenReference
}

/// <summary>
/// Severity levels for coherence issues.
/// </summary>
public enum IssueSeverity
{
    /// <summary>
    /// Minor issue, does not significantly impact understanding
    /// </summary>
    Low,

    /// <summary>
    /// Moderate issue, may cause some confusion
    /// </summary>
    Medium,

    /// <summary>
    /// Major issue, significantly impacts understanding
    /// </summary>
    High
}

/// <summary>
/// Cohesion levels for chunks.
/// </summary>
public enum CohesionLevel
{
    /// <summary>
    /// Very low cohesion, chunk needs restructuring
    /// </summary>
    VeryLow,

    /// <summary>
    /// Low cohesion, some improvements needed
    /// </summary>
    Low,

    /// <summary>
    /// Medium cohesion, acceptable
    /// </summary>
    Medium,

    /// <summary>
    /// High cohesion, well-structured
    /// </summary>
    High,

    /// <summary>
    /// Very high cohesion, excellent structure
    /// </summary>
    VeryHigh
}