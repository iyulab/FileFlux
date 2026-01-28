namespace FileFlux.Core;

/// <summary>
/// Stage 2.5 output: LLM-refined content with enhanced quality.
/// Produced by ILlmRefiner from RefinedContent (rule-based).
/// </summary>
public class LlmRefinedContent
{
    /// <summary>
    /// Unique LLM refine ID.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Reference to rule-based refinement stage.
    /// </summary>
    public Guid RefinedId { get; init; }

    /// <summary>
    /// Reference to original raw extraction stage.
    /// </summary>
    public Guid RawId { get; init; }

    /// <summary>
    /// LLM-refined text content with improved quality.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Document sections after LLM restructuring.
    /// </summary>
    public IReadOnlyList<Section> Sections { get; init; } = [];

    /// <summary>
    /// Structured elements (may be enhanced by LLM).
    /// </summary>
    public IReadOnlyList<StructuredElement> Structures { get; init; } = [];

    /// <summary>
    /// Document-level metadata.
    /// </summary>
    public DocumentMetadata Metadata { get; init; } = new();

    /// <summary>
    /// LLM refinement quality metrics.
    /// </summary>
    public LlmRefinementQuality Quality { get; init; } = new();

    /// <summary>
    /// LLM refinement process information.
    /// </summary>
    public LlmRefinementInfo Info { get; init; } = new();

    /// <summary>
    /// LLM refinement timestamp.
    /// </summary>
    public DateTime RefinedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Processing status.
    /// </summary>
    public ProcessingStatus Status { get; init; } = ProcessingStatus.Completed;

    /// <summary>
    /// Errors encountered during LLM refinement.
    /// </summary>
    public IReadOnlyList<ProcessingError> Errors { get; init; } = [];

    /// <summary>
    /// Success indicator.
    /// </summary>
    public bool IsSuccess => Status == ProcessingStatus.Completed && Errors.Count == 0;

    /// <summary>
    /// Whether LLM was actually used (may be skipped if unavailable).
    /// </summary>
    public bool LlmWasUsed => Info.LlmWasUsed;

    /// <summary>
    /// Character count after LLM refinement.
    /// </summary>
    public int CharCount => Text.Length;

    /// <summary>
    /// Create from RefinedContent when LLM is skipped.
    /// </summary>
    public static LlmRefinedContent FromRefinedContent(RefinedContent refined)
    {
        return new LlmRefinedContent
        {
            RefinedId = refined.Id,
            RawId = refined.RawId,
            Text = refined.Text,
            Sections = refined.Sections,
            Structures = refined.Structures,
            Metadata = refined.Metadata,
            Quality = new LlmRefinementQuality
            {
                InputCharCount = refined.CharCount,
                OutputCharCount = refined.CharCount,
                ImprovementScore = 0.0,
                ConfidenceScore = 1.0
            },
            Info = new LlmRefinementInfo
            {
                LlmWasUsed = false,
                SkipReason = "LLM refiner not available or disabled"
            }
        };
    }
}

/// <summary>
/// Quality metrics for LLM refinement stage.
/// </summary>
public record LlmRefinementQuality
{
    /// <summary>
    /// Character count before LLM refinement.
    /// </summary>
    public int InputCharCount { get; init; }

    /// <summary>
    /// Character count after LLM refinement.
    /// </summary>
    public int OutputCharCount { get; init; }

    /// <summary>
    /// Improvement score (0.0 - 1.0).
    /// Higher means more improvements were made.
    /// </summary>
    public double ImprovementScore { get; init; }

    /// <summary>
    /// Confidence in the improvements (0.0 - 1.0).
    /// </summary>
    public double ConfidenceScore { get; init; }

    /// <summary>
    /// Compression ratio (output / input).
    /// </summary>
    public double CompressionRatio => InputCharCount > 0
        ? (double)OutputCharCount / InputCharCount
        : 1.0;

    /// <summary>
    /// Number of noise segments removed.
    /// </summary>
    public int NoiseSegmentsRemoved { get; init; }

    /// <summary>
    /// Number of sentences restored (from broken lines).
    /// </summary>
    public int SentencesRestored { get; init; }

    /// <summary>
    /// Number of structure changes made.
    /// </summary>
    public int StructureChanges { get; init; }

    /// <summary>
    /// Number of OCR errors corrected.
    /// </summary>
    public int OcrErrorsCorrected { get; init; }

    /// <summary>
    /// Number of duplicate segments merged.
    /// </summary>
    public int DuplicatesMerged { get; init; }
}

/// <summary>
/// LLM refinement process information.
/// </summary>
public class LlmRefinementInfo
{
    /// <summary>
    /// Whether LLM was actually used.
    /// </summary>
    public bool LlmWasUsed { get; init; }

    /// <summary>
    /// Reason for skipping LLM (if skipped).
    /// </summary>
    public string? SkipReason { get; init; }

    /// <summary>
    /// LLM model used.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Input tokens consumed.
    /// </summary>
    public int InputTokens { get; init; }

    /// <summary>
    /// Output tokens generated.
    /// </summary>
    public int OutputTokens { get; init; }

    /// <summary>
    /// Total tokens consumed.
    /// </summary>
    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>
    /// Processing duration.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// List of improvements made by LLM.
    /// </summary>
    public IReadOnlyList<string> Improvements { get; init; } = [];

    /// <summary>
    /// Warnings during LLM refinement.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
