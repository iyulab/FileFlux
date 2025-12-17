using System.Collections;

namespace FileFlux.Core;

/// <summary>
/// Container for complete document processing results.
/// Provides access to all pipeline stage outputs.
/// </summary>
public class ProcessingResult : IEnumerable<DocumentChunk>
{
    /// <summary>
    /// Unique document processing ID.
    /// </summary>
    public Guid DocumentId { get; init; } = Guid.NewGuid();

    // === Stage Results ===

    /// <summary>
    /// Stage 1: Raw extraction result.
    /// </summary>
    public RawContent? Raw { get; set; }

    /// <summary>
    /// Stage 2: Refined content with structure analysis.
    /// </summary>
    public RefinedContent? Refined { get; set; }

    /// <summary>
    /// Stage 3: Chunked document segments.
    /// </summary>
    public IReadOnlyList<DocumentChunk>? Chunks { get; set; }

    // === Extended Results ===

    /// <summary>
    /// Inter-chunk relationship graph (built during Enrich stage).
    /// </summary>
    public DocumentGraph? Graph { get; set; }

    // === Processing Metrics ===

    /// <summary>
    /// Processing performance metrics.
    /// </summary>
    public ProcessingMetrics Metrics { get; } = new();

    // === State Inspection ===

    /// <summary>
    /// True if extraction stage completed.
    /// </summary>
    public bool IsExtracted => Raw != null;

    /// <summary>
    /// True if refine stage completed.
    /// </summary>
    public bool IsRefined => Refined != null;

    /// <summary>
    /// True if chunking stage completed.
    /// </summary>
    public bool IsChunked => Chunks != null;

    /// <summary>
    /// True if graph has been built.
    /// </summary>
    public bool HasGraph => Graph != null;

    /// <summary>
    /// True if minimum processing (up to chunks) is complete.
    /// </summary>
    public bool IsComplete => IsChunked;

    /// <summary>
    /// True if full processing (including graph) is complete.
    /// </summary>
    public bool IsFullyProcessed => IsChunked && HasGraph;

    // === IEnumerable Implementation (for backward compatibility) ===

    /// <summary>
    /// Enumerates chunks for backward compatibility.
    /// </summary>
    public IEnumerator<DocumentChunk> GetEnumerator()
        => (Chunks ?? []).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // === Internal Methods ===

    /// <summary>
    /// Clears all results (used during disposal).
    /// </summary>
    public void Clear()
    {
        Raw = null;
        Refined = null;
        Chunks = null;
        Graph = null;
        Metrics.Reset();
    }
}

/// <summary>
/// Processing performance metrics across all stages.
/// </summary>
public class ProcessingMetrics
{
    /// <summary>
    /// Time spent in extraction stage.
    /// </summary>
    public TimeSpan ExtractDuration { get; set; }

    /// <summary>
    /// Time spent in refine stage.
    /// </summary>
    public TimeSpan RefineDuration { get; set; }

    /// <summary>
    /// Time spent in chunking stage.
    /// </summary>
    public TimeSpan ChunkDuration { get; set; }

    /// <summary>
    /// Time spent in enrichment stage.
    /// </summary>
    public TimeSpan EnrichDuration { get; set; }

    /// <summary>
    /// Total processing time across all stages.
    /// </summary>
    public TimeSpan TotalDuration => ExtractDuration + RefineDuration + ChunkDuration + EnrichDuration;

    /// <summary>
    /// Total number of chunks generated.
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// Total estimated tokens across all chunks.
    /// </summary>
    public int TotalTokens { get; set; }

    /// <summary>
    /// Number of structured elements extracted.
    /// </summary>
    public int StructuresExtracted { get; set; }

    /// <summary>
    /// Number of edges in the chunk graph.
    /// </summary>
    public int GraphEdges { get; set; }

    /// <summary>
    /// Number of nodes in the chunk graph.
    /// </summary>
    public int GraphNodes { get; set; }

    /// <summary>
    /// Source file size in bytes.
    /// </summary>
    public long SourceFileSize { get; set; }

    /// <summary>
    /// Original text character count.
    /// </summary>
    public int OriginalCharCount { get; set; }

    /// <summary>
    /// Refined text character count.
    /// </summary>
    public int RefinedCharCount { get; set; }

    /// <summary>
    /// Resets all metrics to default values.
    /// </summary>
    internal void Reset()
    {
        ExtractDuration = TimeSpan.Zero;
        RefineDuration = TimeSpan.Zero;
        ChunkDuration = TimeSpan.Zero;
        EnrichDuration = TimeSpan.Zero;
        TotalChunks = 0;
        TotalTokens = 0;
        StructuresExtracted = 0;
        GraphEdges = 0;
        GraphNodes = 0;
        SourceFileSize = 0;
        OriginalCharCount = 0;
        RefinedCharCount = 0;
    }
}
