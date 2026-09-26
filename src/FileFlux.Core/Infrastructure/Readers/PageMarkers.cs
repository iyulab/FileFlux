using System.Text;
using System.Text.RegularExpressions;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Reads <c>&lt;!-- page N --&gt;</c> boundary comments (Unpdf's <c>MarkdownOptions.PageMarkers</c>, and the per-page
/// fallback's own) into page <see cref="SourceSpan"/>s and removes them. The returned text is trimmed, with the spans
/// re-expressed over the trimmed text.
/// </summary>
internal static partial class PageMarkers
{
    [GeneratedRegex(@"<!--\s*page\s+(\d+)\s*-->[ \t]*(\r?\n)?")]
    private static partial Regex Marker();

    public static (string Text, IReadOnlyList<SourceSpan> Spans) Extract(string text)
    {
        var starts = new List<(int Page, int Position)>();
        var sb = new StringBuilder(text.Length);
        var last = 0;
        foreach (Match m in Marker().Matches(text))
        {
            sb.Append(text, last, m.Index - last);
            if (int.TryParse(m.Groups[1].ValueSpan, out var page))
                starts.Add((page, sb.Length));
            last = m.Index + m.Length;
        }
        sb.Append(text, last, text.Length - last);

        var untrimmed = sb.ToString();
        var trimmed = untrimmed.Trim();
        var lead = untrimmed.Length - untrimmed.TrimStart().Length;

        var spans = new List<SourceSpan>(starts.Count);
        for (var i = 0; i < starts.Count; i++)
        {
            var start = Math.Clamp(starts[i].Position - lead, 0, trimmed.Length);
            var end = Math.Clamp((i + 1 < starts.Count ? starts[i + 1].Position : untrimmed.Length) - lead, 0, trimmed.Length);
            if (end > start && !string.IsNullOrWhiteSpace(trimmed[start..end]))
                spans.Add(new SourceSpan(start, end) { Page = starts[i].Page });
        }
        return (trimmed, spans);
    }
}
