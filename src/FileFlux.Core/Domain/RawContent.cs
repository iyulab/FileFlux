namespace FileFlux.Core;

/// <summary>
/// Stage 1 output: Raw text extraction result.
/// </summary>
public class RawContent
{
    /// <summary>
    /// Unique extraction ID.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Extracted raw text.
    /// </summary>
    public string Text { get; set; } = string.Empty;

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
    /// Structural hints detected by reader (optional).
    /// </summary>
    public Dictionary<string, object> Hints { get; set; } = new();

    /// <summary>
    /// Extraction warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Processing status.
    /// </summary>
    public ProcessingStatus Status { get; set; } = ProcessingStatus.Completed;

    /// <summary>
    /// Errors encountered during extraction.
    /// </summary>
    public List<ProcessingError> Errors { get; set; } = new();

    /// <summary>
    /// Success indicator.
    /// </summary>
    public bool IsSuccess => Status == ProcessingStatus.Completed && Errors.Count == 0;

    /// <summary>
    /// Extracted images from document (base64, embedded, etc.)
    /// </summary>
    public List<ImageInfo> Images { get; set; } = new();
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
