namespace FileFlux.Core;

/// <summary>
/// Markdown normalization service interface.
/// Applies rule-based corrections to markdown structure without AI.
/// </summary>
public interface IMarkdownNormalizer
{
    /// <summary>
    /// Normalize markdown structure by applying rule-based corrections.
    /// </summary>
    /// <param name="markdown">Raw markdown content</param>
    /// <param name="options">Normalization options</param>
    /// <returns>Normalized result with applied corrections</returns>
    NormalizationResult Normalize(string markdown, NormalizationOptions? options = null);
}

/// <summary>
/// Options for markdown normalization process.
/// </summary>
public class NormalizationOptions
{
    /// <summary>
    /// Normalize heading hierarchy (fix H1 → H5 jumps).
    /// </summary>
    public bool NormalizeHeadings { get; set; } = true;

    /// <summary>
    /// Remove empty or meaningless headings.
    /// </summary>
    public bool RemoveEmptyHeadings { get; set; } = true;

    /// <summary>
    /// Normalize list structure and indentation.
    /// </summary>
    public bool NormalizeLists { get; set; } = true;

    /// <summary>
    /// Normalize whitespace and blank lines.
    /// </summary>
    public bool NormalizeWhitespace { get; set; } = true;

    /// <summary>
    /// Remove lines that are likely annotation/comment rather than headings.
    /// Patterns like "(증가율)", "※ 주석" will be converted to plain text.
    /// </summary>
    public bool DemoteAnnotationHeadings { get; set; } = true;

    /// <summary>
    /// Normalize tables by validating column consistency.
    /// Complex or malformed tables are converted to text blocks with hints.
    /// </summary>
    public bool NormalizeTables { get; set; } = true;

    /// <summary>
    /// Maximum allowed column count variance in a table.
    /// If column counts vary more than this, the table is considered malformed.
    /// Default is 0 (all rows must have same column count).
    /// </summary>
    public int MaxColumnVariance { get; set; }

    /// <summary>
    /// Maximum allowed heading level jump. Default is 1.
    /// H1 → H2 is allowed, but H1 → H3 would be corrected to H1 → H2.
    /// </summary>
    public int MaxHeadingLevelJump { get; set; } = 1;

    /// <summary>
    /// Maximum heading level for first heading. Default is 2.
    /// If first heading is H4, it will be promoted to H1 or H2.
    /// </summary>
    public int MaxFirstHeadingLevel { get; set; } = 2;

    /// <summary>
    /// Default normalization options.
    /// </summary>
    public static NormalizationOptions Default => new();
}

/// <summary>
/// Result of markdown normalization process.
/// </summary>
public class NormalizationResult
{
    /// <summary>
    /// Normalized markdown content.
    /// </summary>
    public string Markdown { get; set; } = string.Empty;

    /// <summary>
    /// Original markdown content before normalization.
    /// </summary>
    public string OriginalMarkdown { get; set; } = string.Empty;

    /// <summary>
    /// List of normalization actions applied.
    /// </summary>
    public List<NormalizationAction> Actions { get; set; } = [];

    /// <summary>
    /// Summary statistics of normalization.
    /// </summary>
    public NormalizationStats Stats { get; set; } = new();

    /// <summary>
    /// Whether any changes were made.
    /// </summary>
    public bool HasChanges => Actions.Count > 0;
}

/// <summary>
/// Individual normalization action applied to the markdown.
/// </summary>
public class NormalizationAction
{
    /// <summary>
    /// Type of normalization action.
    /// </summary>
    public NormalizationActionType Type { get; set; }

    /// <summary>
    /// Line number (1-based) where the action was applied.
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// Original content before normalization.
    /// </summary>
    public string Before { get; set; } = string.Empty;

    /// <summary>
    /// Content after normalization.
    /// </summary>
    public string After { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable reason for the change.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Types of normalization actions.
/// </summary>
public enum NormalizationActionType
{
    /// <summary>First heading promoted to H1/H2.</summary>
    FirstHeadingPromoted,

    /// <summary>Heading level adjusted to fix hierarchy jump.</summary>
    HeadingLevelAdjusted,

    /// <summary>Empty or meaningless heading removed.</summary>
    EmptyHeadingRemoved,

    /// <summary>Annotation-like heading demoted to plain text.</summary>
    AnnotationHeadingDemoted,

    /// <summary>List indentation normalized.</summary>
    ListIndentNormalized,

    /// <summary>Excessive blank lines removed.</summary>
    ExcessiveBlankLinesRemoved,

    /// <summary>Trailing whitespace removed.</summary>
    TrailingWhitespaceRemoved,

    /// <summary>Malformed table converted to text block with hint.</summary>
    MalformedTableConverted,

    /// <summary>Complex table (merged cells) converted to text block.</summary>
    ComplexTableConverted
}

/// <summary>
/// Statistics summary of normalization process.
/// </summary>
public class NormalizationStats
{
    /// <summary>Total number of lines processed.</summary>
    public int TotalLines { get; set; }

    /// <summary>Number of headings found.</summary>
    public int HeadingsFound { get; set; }

    /// <summary>Number of headings adjusted.</summary>
    public int HeadingsAdjusted { get; set; }

    /// <summary>Number of headings removed.</summary>
    public int HeadingsRemoved { get; set; }

    /// <summary>Number of headings demoted to text.</summary>
    public int HeadingsDemoted { get; set; }

    /// <summary>Number of list items normalized.</summary>
    public int ListItemsNormalized { get; set; }

    /// <summary>Number of blank lines removed.</summary>
    public int BlankLinesRemoved { get; set; }

    /// <summary>Number of tables found.</summary>
    public int TablesFound { get; set; }

    /// <summary>Number of malformed tables converted to text.</summary>
    public int TablesConvertedToText { get; set; }

    /// <summary>Number of valid tables preserved.</summary>
    public int TablesPreserved { get; set; }
}
