using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure;

/// <summary>
/// Line-level facts about pipe tables, shared by the Markdown converter and normaliser so they count columns and
/// recognise rows the same way.
/// </summary>
internal static partial class MarkdownTableLines
{
    /// <summary>A pipe-table row: at least two unescaped pipes, not a block quote.</summary>
    internal static bool IsRow(string line) =>
        !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('>') && CountUnescapedPipes(line) >= 2;

    /// <summary>A delimiter row (<c>| --- | :---: |</c>).</summary>
    internal static bool IsDelimiter(string line) => DelimiterPattern().IsMatch(line.Trim());

    /// <summary>
    /// Cells in a row, counted from unescaped pipes (<c>\|</c> is cell text). Outer pipes are optional, as in GFM.
    /// </summary>
    internal static int CellCount(string line)
    {
        var trimmed = line.Trim();
        var pipes = CountUnescapedPipes(trimmed);
        if (trimmed.StartsWith('|'))
            pipes--;
        if (trimmed.EndsWith('|') && !trimmed.EndsWith("\\|", StringComparison.Ordinal) && trimmed.Length > 1)
            pipes--;
        return pipes + 1;
    }

    /// <summary>A delimiter row for <paramref name="columns"/> columns.</summary>
    internal static string Delimiter(int columns) =>
        "|" + string.Concat(Enumerable.Repeat(" --- |", Math.Max(1, columns)));

    /// <summary>
    /// Re-attaches a header row that a blank line separated from its body (some parsers write a table's header row,
    /// then a blank line, then the body without a delimiter row). Read on its own, such a header is a one-row "table"
    /// and its body a header-less one whose first data row would be promoted to header. A single pipe row followed,
    /// after blank lines only, by a pipe block of the same width becomes one table: header, delimiter, body.
    /// </summary>
    internal static List<string> JoinDetachedHeaderRows(IReadOnlyList<string> lines)
    {
        var result = new List<string>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            result.Add(line);

            var startsBlock = IsRow(line) && (i == 0 || !IsRow(lines[i - 1]));
            var singleRow = startsBlock && (i + 1 >= lines.Count || !IsRow(lines[i + 1]));
            if (!singleRow || IsDelimiter(line))
                continue;

            var next = i + 1;
            while (next < lines.Count && string.IsNullOrWhiteSpace(lines[next]))
                next++;

            if (next == i + 1 || next >= lines.Count || !IsRow(lines[next]) || IsDelimiter(lines[next])
                || CellCount(lines[next]) != CellCount(line))
                continue;

            result.Add(Delimiter(CellCount(line)));
            i = next - 1; // drop the blank lines; the body follows
        }

        return result;
    }

    private static int CountUnescapedPipes(string line)
    {
        var count = 0;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '|' && (i == 0 || line[i - 1] != '\\'))
                count++;
        }

        return count;
    }

    [GeneratedRegex(@"^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?$")]
    private static partial Regex DelimiterPattern();
}
