namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// The Markdown options the Office and HWP readers pass to the native parsers. One definition, so the readers cannot
/// disagree about paragraph separation: every source paragraph ends with a blank line and is therefore its own Markdown
/// paragraph. (Markdown joins adjacent lines into one paragraph, so without the blank line a renderer shows a run-on
/// paragraph and a paragraph-based chunker sees one block.)
/// </summary>
internal static class ParserMarkdownOptions
{
    /// <summary>Options for Undoc (DOCX, XLSX, PPTX and their legacy formats).</summary>
    internal static Undoc.MarkdownOptions Office() => new()
    {
        IncludeFrontmatter = false,
        EscapeSpecialChars = false,
        ParagraphSpacing = true,
    };

    /// <summary>Options for Unhwp (HWP, HWPX).</summary>
    internal static Unhwp.MarkdownOptions Hwp() => new()
    {
        IncludeFrontmatter = false,
        EscapeSpecialChars = false,
        ParagraphSpacing = true,
    };
}
