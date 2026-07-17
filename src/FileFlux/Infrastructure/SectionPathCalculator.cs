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

    /// <summary>
    /// Builds sections from markdown heading markers in <paramref name="text"/>.
    /// A section spans until the next heading of the same or shallower level, so nested
    /// headings produce overlapping (hierarchical) ranges — a chunk inside "## Sub" also
    /// falls inside its parent "# Root", which is required for a hierarchical HeadingPath.
    /// </summary>
    internal static List<Section> BuildSections(string text)
    {
        var sections = new List<Section>();
        var matches = HeadingRegex().Matches(text);

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var level = match.Groups[1].Value.Length;
            var title = match.Groups[2].Value.Trim();

            // Section ends at the next heading of the same or shallower level (hierarchical span).
            var endPos = text.Length;
            for (int j = i + 1; j < matches.Count; j++)
            {
                if (matches[j].Groups[1].Value.Length <= level)
                {
                    endPos = matches[j].Index;
                    break;
                }
            }

            sections.Add(new Section
            {
                Id = $"section_{i}",
                Title = title,
                Level = level,
                Start = match.Index,
                End = endPos,
                Content = text.Substring(match.Index, endPos - match.Index).Trim()
            });
        }

        return sections;
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
