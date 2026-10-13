namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// The Markdown options the Office and HWP readers pass to the native parsers. One definition, so the readers cannot
/// disagree about how text becomes Markdown:
/// <list type="bullet">
/// <item>every source paragraph ends with a blank line and is therefore its own Markdown paragraph (Markdown joins
/// adjacent lines, so without it a renderer shows a run-on paragraph and a paragraph-based chunker sees one block);</item>
/// <item>characters in body text that Markdown would read as syntax (<c>*</c>, <c>`</c>, <c>[</c>, <c>\</c> …) are
/// escaped, so <c>a*b*c</c> renders as written and not as emphasis.</item>
/// </list>
/// </summary>
internal static class ParserMarkdownOptions
{
    /// <summary>Options for Undoc (DOCX, XLSX, PPTX and their legacy formats).</summary>
    internal static Undoc.MarkdownOptions Office() => new()
    {
        IncludeFrontmatter = false,
        EscapeSpecialChars = true,
        ParagraphSpacing = true,
    };

    /// <summary>Options for Unhwp (HWP, HWPX).</summary>
    internal static Unhwp.MarkdownOptions Hwp() => new()
    {
        IncludeFrontmatter = false,
        EscapeSpecialChars = true,
        ParagraphSpacing = true,
    };
}
