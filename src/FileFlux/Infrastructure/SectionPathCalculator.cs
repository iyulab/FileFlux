using System.Text.RegularExpressions;
using FileFlux.Core;

namespace FileFlux.Infrastructure;

/// <summary>
/// Shared section building and heading-path calculation for chunk-producing paths.
/// Single source of truth so DocumentRefiner, DocumentProcessor and StatefulDocumentProcessor
/// agree on how structural metadata (Sections / HeadingPath / Section) is derived.
/// </summary>
internal static partial class SectionPathCalculator
{
    [GeneratedRegex(@"^(#{1,6})\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();

    // The structural marker a reader writes on the line before a heading (MarkdownDocumentReader).
    [GeneratedRegex(@"^<!--\s*HEADING_START:H\d\s*-->$")]
    private static partial Regex HeadingStartMarkerRegex();

    /// <summary>
    /// Builds sections from markdown heading markers in <paramref name="text"/>.
    /// A section spans until the next heading of the same or shallower level, so nested
    /// headings produce overlapping (hierarchical) ranges — a chunk inside "## Sub" also
    /// falls inside its parent "# Root", which is required for a hierarchical HeadingPath.
    /// </summary>
    /// <remarks>
    /// A heading that a reader introduced with a structural marker (<c>&lt;!-- HEADING_START:H2 --&gt;</c> on the line
    /// before) starts at that marker: a chunk that begins at the marker begins the section, so its heading path names
    /// the heading it opens with rather than only the enclosing one.
    /// </remarks>
    internal static List<Section> BuildSections(string text)
    {
        var sections = new List<Section>();
        var matches = HeadingRegex().Matches(text);
        var starts = new int[matches.Count];
        for (int i = 0; i < matches.Count; i++)
            starts[i] = SectionStart(text, matches[i].Index);

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var level = match.Groups[1].Value.Length;
            var title = match.Groups[2].Value.Trim();

            // Section ends where the next heading of the same or shallower level starts (hierarchical span).
            var endPos = text.Length;
            for (int j = i + 1; j < matches.Count; j++)
            {
                if (matches[j].Groups[1].Value.Length <= level)
                {
                    endPos = starts[j];
                    break;
                }
            }

            sections.Add(new Section
            {
                Id = $"section_{i}",
                Title = title,
                Level = level,
                Start = starts[i],
                End = endPos,
                Content = text.Substring(match.Index, endPos - match.Index).Trim()
            });
        }

        return sections;
    }

    /// <summary>
    /// Where the section of the heading at <paramref name="headingIndex"/> starts: the heading-start marker on the line
    /// before it, when there is one (blank lines between are allowed), otherwise the heading itself.
    /// </summary>
    private static int SectionStart(string text, int headingIndex)
    {
        var end = headingIndex;
        while (end > 0 && char.IsWhiteSpace(text[end - 1]))
            end--;

        if (end < 3 || string.CompareOrdinal(text, end - 3, "-->", 0, 3) != 0)
            return headingIndex;

        var start = text.LastIndexOf("<!--", end - 1, StringComparison.Ordinal);
        if (start < 0 || (start > 0 && text[start - 1] != '\n'))
            return headingIndex;

        return HeadingStartMarkerRegex().IsMatch(text.AsSpan(start, end - start)) ? start : headingIndex;
    }
    /// <summary>
    /// Flattens a section hierarchy into a single list (depth-first, parents before children).
    /// </summary>
    internal static List<Section> Flatten(List<Section> sections)
    {
        var result = new List<Section>();
        foreach (var section in sections)
        {
            result.Add(section);
            if (section.Children.Count > 0)
            {
                result.AddRange(Flatten(section.Children));
            }
        }
        return result;
    }

    /// <summary>
    /// Calculates the heading path (outermost → innermost) of the sections containing
    /// <paramref name="startChar"/>. Section offsets must refer to the same text the
    /// chunk offsets refer to.
    /// </summary>
    internal static List<string> CalculateHeadingPath(List<Section> flatSections, int startChar, int endChar)
    {
        var containingSections = flatSections
            .Where(s => s.Start <= startChar && s.End >= startChar && !string.IsNullOrEmpty(s.Title))
            .OrderBy(s => s.Level)
            .ThenBy(s => s.Start)
            .ToList();

        return containingSections.Select(s => s.Title).ToList();
    }
}
