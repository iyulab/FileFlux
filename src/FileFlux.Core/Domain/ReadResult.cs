namespace FileFlux.Core;

/// <summary>
/// Stage 0 output: Raw parsing result from document reader.
/// Contains parsed document structure before extraction.
/// </summary>
public class ReadResult
{
    /// <summary>
    /// Unique read operation ID.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Source file information.
    /// </summary>
    public SourceFileInfo File { get; set; } = new();

    /// <summary>
    /// Reader type used for parsing.
    /// </summary>
    public string ReaderType { get; set; } = string.Empty;

    /// <summary>
    /// Page/sheet/slide information.
    /// </summary>
    public List<PageInfo> Pages { get; set; } = [];

    /// <summary>
    /// Document-level metadata extracted during read.
    /// </summary>
    public Dictionary<string, object> DocumentProps { get; set; } = [];

    /// <summary>
    /// Read operation timestamp.
    /// </summary>
    public DateTime ReadAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Processing duration.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Warnings during read operation.
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Processing status.
    /// </summary>
    public ProcessingStatus Status { get; set; } = ProcessingStatus.Completed;

    /// <summary>
    /// Errors encountered during read.
    /// </summary>
    public List<ProcessingError> Errors { get; set; } = [];

    /// <summary>
    /// Success indicator.
    /// </summary>
    public bool IsSuccess => Status == ProcessingStatus.Completed && Errors.Count == 0;

    /// <summary>
    /// Total page count.
    /// </summary>
    public int PageCount => Pages.Count;
}

/// <summary>
/// Page/sheet/slide information from read stage.
/// </summary>
public class PageInfo
{
    /// <summary>
    /// Page number (1-based).
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// Page width in points (PDF) or pixels.
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Page height in points (PDF) or pixels.
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Page-specific properties.
    /// </summary>
    public Dictionary<string, object> Props { get; set; } = [];

    /// <summary>
    /// Whether page has content.
    /// </summary>
    public bool HasContent { get; set; } = true;
}
