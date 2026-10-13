using System.Text.RegularExpressions;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Recognises a GFM table in the Markdown a parser returns, by its delimiter row: pipe-separated cells of dashes, each
/// optionally marked for alignment (<c>:---</c>, <c>---:</c>, <c>:---:</c>). A line of dashes without a pipe
/// is a thematic break, not a table.
/// </summary>
internal static partial class MarkdownTables
{
    internal static bool Contains(string markdown) => DelimiterRow().IsMatch(markdown);

    // One delimiter cell, then either a pipe and any further cells, or (with a leading pipe) nothing more.
    [GeneratedRegex(
        @"^[ \t]*(?:\|[ \t]*)?:?-+:?[ \t]*\|(?:[ \t]*:?-+:?[ \t]*\|?)*[ \t\r]*$" +
        @"|^[ \t]*\|[ \t]*:?-+:?[ \t]*[ \t\r]*$",
        RegexOptions.Multiline)]
    private static partial Regex DelimiterRow();
}
