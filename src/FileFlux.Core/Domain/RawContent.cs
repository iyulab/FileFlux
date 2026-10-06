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
    /// A shallow copy carrying <paramref name="text"/> — for a pipeline step that hands the text as cleaned so far to
    /// a component typed on <see cref="RawContent"/>. A memberwise copy, so a member added later is carried too.
    /// </summary>
    internal RawContent WithText(string text)
    {
        var copy = (RawContent)MemberwiseClone();
        copy.Text = text;
        return copy;
    }

    /// <summary>
    /// Where the text came from in the source — pages, time ranges — as ordered spans over <see cref="Text"/>.
    /// A reader that knows it fills this; refinement carries the spans to <see cref="RefinedContent.Spans"/>, and
    /// chunking writes them onto each chunk's <see cref="SourceLocation"/>. Empty when the reader does not know.
    /// </summary>
    public IReadOnlyList<SourceSpan> Spans { get; set; } = [];

    /// <summary>
    /// Structured text blocks with position and style info.
    /// </summary>
    public List<TextBlock> Blocks { get; set; } = [];

    /// <summary>
    /// The tables of <see cref="Text"/> as structured data — the same tables the text carries inline, not additional
    /// ones. <see cref="Text"/> stays authoritative: nothing in the pipeline writes these into the text again.
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
/// How well the source was read, page by page. A reader that knows fills it; empty when it does not.
/// </summary>
public sealed class ExtractionQuality
{
    /// <summary>
    /// One record per page, in page order — page numbers as in <see cref="RawContent.Spans"/>. Empty when the reader
    /// has no page signals (only the PDF reader reports them today).
    /// </summary>
    public IReadOnlyList<PageQuality> Pages { get; init; } = [];
}

/// <summary>
/// What a reader can say about one page's extraction — the facts a consumer needs to decide whether that page should
/// be read another way (for example rendered and read by a vision model). Thresholds are the consumer's.
/// </summary>
/// <param name="Page">1-based page number, the numbering of <see cref="RawContent.Spans"/>.</param>
public sealed record PageQuality(int Page)
{
    /// <summary>Length of this page's text in <see cref="RawContent.Text"/> (its span); 0 when the page produced no text.</summary>
    public int Characters { get; init; }

    /// <summary>U+FFFD replacement characters in this page's text — decode failures that survived as glyphs.</summary>
    public int ReplacementCharacters { get; init; }

    /// <summary>Text-showing operators the parser found on the page.</summary>
    public int TextOperators { get; init; }

    /// <summary>Image paints the parser found on the page.</summary>
    public int ImageOperators { get; init; }

    /// <summary>
    /// Form XObject paints on the page. The text and images drawn inside a form are already counted in
    /// <see cref="TextOperators"/> and <see cref="ImageOperators"/>.
    /// </summary>
    public int FormOperators { get; init; }

    /// <summary>The parser dropped this page's OCR text layer because it could not be read.</summary>
    public bool OcrLayerSuppressed { get; init; }

    /// <summary>Text runs the font decoder could not read and discarded on this page; that text is missing.</summary>
    public int SuppressedTextRuns { get; init; }

    /// <summary>Content streams of this page the parser could not decode and left out; what they draw is missing.</summary>
    public int UndecodableContentStreams { get; init; }

    /// <summary>The page has a text layer the parser could read: text operators, and no suppressed OCR layer.</summary>
    public bool HasTextLayer => TextOperators > 0 && !OcrLayerSuppressed;
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
