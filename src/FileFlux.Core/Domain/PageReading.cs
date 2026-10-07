namespace FileFlux.Core;

/// <summary>
/// Which pages to render and read through an image-to-text service, and how (<see cref="ExtractOptions.PageReading"/>).
/// The selection and the budget are the consumer's; the reader decides only whether a read may replace the page's text.
/// </summary>
/// <remarks>
/// A read replaces a page's text when the page has no readable text (no text layer, no characters, or a suppressed OCR
/// layer), or when it lost content (undecodable content streams, discarded text runs, U+FFFD) and the read keeps at least
/// <see cref="MinNativeCoverage"/> of the text the page did have. A render that could not paint everything the page
/// asks for (text in fonts that are not embedded, image codecs, inline images, undecodable content) never replaces text,
/// and a page with tables in <see cref="RawContent.Tables"/> keeps its text so tables and table blocks stay aligned. Every
/// other read is kept in <see cref="RawContent.PageReads"/> for the consumer to use as it sees fit.
/// </remarks>
public sealed class PageReadingOptions
{
    /// <summary>Which pages to render and read, from the reader's page record. Null: none.</summary>
    public Func<PageQuality, bool>? SelectPages { get; set; }

    /// <summary>Render resolution in dots per inch. Default 150.</summary>
    public int Dpi { get; set; } = 150;

    /// <summary>The most pages one document renders; selected pages past it are recorded as skipped. Null: no limit.</summary>
    public int? MaxPages { get; set; }

    /// <summary>
    /// For a page that lost part of its content: the share of the page's own text the read must keep to replace it, so a
    /// read that drops what the page had does not win. Each of the page's words counts as kept when it occurs in the read
    /// with spaces and punctuation removed (case- and compatibility-folded, weighted by length), so a read that spaces or
    /// orders words differently from the text layer keeps everything. Default 0.9.
    /// </summary>
    public double MinNativeCoverage { get; set; } = 0.9;
}

/// <summary>What happened to a page selected by <see cref="PageReadingOptions.SelectPages"/>.</summary>
public enum PageReadOutcome
{
    /// <summary>The page was not rendered (see <see cref="PageRead.Reason"/>).</summary>
    Skipped = 0,

    /// <summary>The read replaced the page's text in <see cref="RawContent.Text"/>.</summary>
    Replaced = 1,

    /// <summary>The page kept its text; the read is in <see cref="PageRead.Text"/> (see <see cref="PageRead.Reason"/>).</summary>
    Kept = 2,

    /// <summary>Rendering or reading failed, or the read was empty; the page kept its text.</summary>
    Failed = 3
}

/// <summary>One selected page's render and read.</summary>
/// <param name="Page">1-based page number, as in the page spans and <see cref="PageQuality.Page"/>.</param>
public sealed record PageRead(int Page)
{
    /// <summary>The selected pages exceeded <see cref="PageReadingOptions.MaxPages"/>.</summary>
    public const string OverBudget = "over_budget";

    /// <summary>The page has readable text and lost none, so the read is guidance only.</summary>
    public const string NativeTextReadable = "native_text_readable";

    /// <summary>The read kept less than <see cref="PageReadingOptions.MinNativeCoverage"/> of the page's text.</summary>
    public const string LowCoverage = "low_coverage";

    /// <summary>The render could not paint everything the page asks for (see the gap counts).</summary>
    public const string RenderGaps = "render_gaps";

    /// <summary>The page has tables in <see cref="RawContent.Tables"/>.</summary>
    public const string PageHasTables = "page_has_tables";

    /// <summary>Rendering the page failed.</summary>
    public const string RenderFailed = "render_failed";

    /// <summary>The image-to-text service threw or reported an error.</summary>
    public const string ReadFailed = "read_failed";

    /// <summary>The image-to-text service returned no text.</summary>
    public const string EmptyRead = "empty_read";

    /// <summary>What happened to the page.</summary>
    public PageReadOutcome Outcome { get; init; }

    /// <summary>Why the page was skipped, kept or failed; null when the read replaced the page's text.</summary>
    public string? Reason { get; init; }

    /// <summary>The text read from the rendered page; null when nothing was read.</summary>
    public string? Text { get; init; }

    /// <summary>Text runs the render could not paint (fonts not embedded or Type 3, codes without a glyph).</summary>
    public int UnrenderedTextRuns { get; init; }

    /// <summary>Images and inline images the render could not paint.</summary>
    public int UnrenderedImages { get; init; }

    /// <summary>Content streams of the page the render could not decode.</summary>
    public int UnrenderedContentStreams { get; init; }

    /// <summary>
    /// Share of the page's text the read kept (word by word, however spaced — see
    /// <see cref="PageReadingOptions.MinNativeCoverage"/>); null when it was not compared.
    /// </summary>
    public double? NativeCoverage { get; init; }
}
