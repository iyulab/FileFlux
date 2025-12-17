namespace FileFlux.Core;

/// <summary>
/// Stage 2 output: Refined content with structure analysis.
/// Replaces ParsedContent by merging Parse and Refine stages.
/// </summary>
public class RefinedContent
{
    /// <summary>
    /// Unique refine ID.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Reference to raw extraction stage.
    /// </summary>
    public Guid RawId { get; init; }

    /// <summary>
    /// Cleaned and normalized text content.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Document sections with hierarchical structure.
    /// </summary>
    public IReadOnlyList<Section> Sections { get; init; } = [];

    /// <summary>
    /// Extracted structured elements (tables, code, lists).
    /// </summary>
    public IReadOnlyList<StructuredElement> Structures { get; init; } = [];

    /// <summary>
    /// Document-level metadata.
    /// </summary>
    public DocumentMetadata Metadata { get; init; } = new();

    /// <summary>
    /// Quality metrics after refinement.
    /// </summary>
    public RefinementQuality Quality { get; init; } = new();

    /// <summary>
    /// Refinement process information.
    /// </summary>
    public RefinementInfo Info { get; init; } = new();

    /// <summary>
    /// Refinement timestamp.
    /// </summary>
    public DateTime RefinedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Processing status.
    /// </summary>
    public ProcessingStatus Status { get; init; } = ProcessingStatus.Completed;

    /// <summary>
    /// Errors encountered during refinement.
    /// </summary>
    public IReadOnlyList<ProcessingError> Errors { get; init; } = [];

    /// <summary>
    /// Success indicator.
    /// </summary>
    public bool IsSuccess => Status == ProcessingStatus.Completed && Errors.Count == 0;

    /// <summary>
    /// Character count after refinement.
    /// </summary>
    public int CharCount => Text.Length;
}

/// <summary>
/// Quality metrics after refinement stage.
/// </summary>
public class RefinementQuality
{
    /// <summary>
    /// Structure preservation score (0.0 - 1.0).
    /// How well document structure was preserved.
    /// </summary>
    public double StructureScore { get; set; }

    /// <summary>
    /// Noise removal score (0.0 - 1.0).
    /// How effectively noise was removed.
    /// </summary>
    public double CleanupScore { get; set; }

    /// <summary>
    /// Information retention score (0.0 - 1.0).
    /// How much meaningful content was preserved.
    /// </summary>
    public double RetentionScore { get; set; }

    /// <summary>
    /// Confidence score (0.0 - 1.0).
    /// Overall confidence in refinement quality.
    /// </summary>
    public double ConfidenceScore { get; set; }

    /// <summary>
    /// Overall quality score.
    /// </summary>
    public double OverallScore => (StructureScore + CleanupScore + RetentionScore + ConfidenceScore) / 4.0;

    /// <summary>
    /// Character count before refinement.
    /// </summary>
    public int OriginalCharCount { get; set; }

    /// <summary>
    /// Character count after refinement.
    /// </summary>
    public int RefinedCharCount { get; set; }

    /// <summary>
    /// Compression ratio (refined / original).
    /// </summary>
    public double CompressionRatio => OriginalCharCount > 0
        ? (double)RefinedCharCount / OriginalCharCount
        : 1.0;

    /// <summary>
    /// Total tables converted to markdown.
    /// </summary>
    public int TablesConverted { get; set; }

    /// <summary>
    /// Tables converted using LLM assistance.
    /// </summary>
    public int TablesWithLlm { get; set; }

    /// <summary>
    /// Tables converted using rule-based approach.
    /// </summary>
    public int TablesWithRules => TablesConverted - TablesWithLlm;

    /// <summary>
    /// Total images processed.
    /// </summary>
    public int ImagesProcessed { get; set; }

    /// <summary>
    /// Images with LLM-generated captions.
    /// </summary>
    public int ImagesWithCaptions { get; set; }
}

/// <summary>
/// Refinement process information.
/// </summary>
public class RefinementInfo
{
    /// <summary>
    /// Refiner type used.
    /// </summary>
    public string RefinerType { get; init; } = string.Empty;

    /// <summary>
    /// Whether LLM was used for refinement.
    /// </summary>
    public bool UsedLlm { get; init; }

    /// <summary>
    /// LLM model used (if any).
    /// </summary>
    public string? LlmModel { get; init; }

    /// <summary>
    /// Tokens consumed (if LLM was used).
    /// </summary>
    public int? TokensUsed { get; init; }

    /// <summary>
    /// Processing duration.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Warnings during refinement.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
