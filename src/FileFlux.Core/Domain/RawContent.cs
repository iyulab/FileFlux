namespace FileFlux.Core;

/// <summary>
/// Stage 1 output: Raw content extraction result.
/// Contains structured raw data before markdown conversion.
/// </summary>
public class RawContent
{
    /// <summary>
    /// Unique extraction ID.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Reference to ReadResult ID (if available).
    /// </summary>
    public Guid? ReadId { get; set; }

    /// <summary>
    /// Extracted raw text (plain text, no markdown).
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Structured text blocks with position and style info.
    /// </summary>
    public List<TextBlock> Blocks { get; set; } = [];

    /// <summary>
    /// Extracted tables with raw cell data (no markdown conversion).
    /// </summary>
    public List<TableData> Tables { get; set; } = [];

    /// <summary>
    /// Extracted images from document.
    /// </summary>
    public List<ImageInfo> Images { get; set; } = [];

    /// <summary>
    /// Source file information.
    /// </summary>
    public SourceFileInfo File { get; set; } = new();

    /// <summary>
    /// Extraction quality metrics.
    /// </summary>
    public ExtractionQuality Quality { get; set; } = new();

    /// <summary>
    /// Extraction timestamp.
    /// </summary>
    public DateTime ExtractedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Processing duration.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Reader type used for extraction.
    /// </summary>
    public string ReaderType { get; set; } = string.Empty;

    /// <summary>
    /// Structural hints detected by reader.
    /// </summary>
    public Dictionary<string, object> Hints { get; set; } = [];

    /// <summary>
    /// Extraction warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Processing status.
    /// </summary>
    public ProcessingStatus Status { get; set; } = ProcessingStatus.Completed;

    /// <summary>
    /// Errors encountered during extraction.
    /// </summary>
    public List<ProcessingError> Errors { get; set; } = [];

    /// <summary>
    /// Success indicator.
    /// </summary>
    public bool IsSuccess => Status == ProcessingStatus.Completed && Errors.Count == 0;

    /// <summary>
    /// Whether content has structured blocks.
    /// </summary>
    public bool HasBlocks => Blocks.Count > 0;

    /// <summary>
    /// Whether content has tables.
    /// </summary>
    public bool HasTables => Tables.Count > 0;

    /// <summary>
    /// Whether content has images.
    /// </summary>
    public bool HasImages => Images.Count > 0;

    /// <summary>
    /// Total table count.
    /// </summary>
    public int TableCount => Tables.Count;

    /// <summary>
    /// Count of tables needing LLM assistance.
    /// </summary>
    public int LowConfidenceTableCount => Tables.Count(t => t.NeedsLlmAssist);
}

/// <summary>
/// Source file metadata.
/// </summary>
public class SourceFileInfo
{
    /// <summary>
    /// File name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// File extension.
    /// </summary>
    public string Extension { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// File creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// File modification timestamp.
    /// </summary>
    public DateTime ModifiedAt { get; set; }
}

/// <summary>
/// Extraction quality metrics.
/// </summary>
public class ExtractionQuality
{
    /// <summary>
    /// Confidence score (0.0 - 1.0).
    /// </summary>
    public double Confidence { get; set; } = 1.0;

    /// <summary>
    /// Character count.
    /// </summary>
    public int CharCount { get; set; }

    /// <summary>
    /// Detected language.
    /// </summary>
    public string Language { get; set; } = "unknown";

    /// <summary>
    /// Quality issues detected.
    /// </summary>
    public List<string> Issues { get; set; } = new();
}

/// <summary>
/// Processing status enumeration.
/// </summary>
public enum ProcessingStatus
{
    /// <summary>
    /// Processing completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Processing failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Processing partially completed.
    /// </summary>
    Partial
}

/// <summary>
/// Processing error details.
/// </summary>
public class ProcessingError
{
    /// <summary>
    /// Error code.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Pipeline stage where error occurred.
    /// </summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>
    /// Additional error details.
    /// </summary>
    public Dictionary<string, object> Details { get; set; } = new();

    /// <summary>
    /// Error timestamp.
    /// </summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
