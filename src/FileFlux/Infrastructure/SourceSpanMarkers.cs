using System.Text;
using System.Text.RegularExpressions;
using FileFlux.Core;

namespace FileFlux.Infrastructure;

/// <summary>
/// Carries a reader's <see cref="SourceSpan"/>s through refinement. Refinement rewrites text with a dozen string steps
/// (noise cleaning, markdown conversion, header/footer filtering, whitespace normalization) that report no offset
/// mapping, so offsets cannot be translated. Instead each span start is marked in the text with an HTML comment on its
/// own line — refinement keeps comments — and after refinement the markers are read back as positions and removed.
/// </summary>
internal static partial class SourceSpanMarkers
{
    [GeneratedRegex(@"<!--ffspan:(\d+)-->\n?")]
    private static partial Regex MarkerPattern();

    /// <summary>Inserts one marker line at each span's start. Spans are applied in order; overlaps are ignored.</summary>
    public static string Insert(string text, IReadOnlyList<SourceSpan> spans)
    {
        if (spans.Count == 0)
            return text;

        var sb = new StringBuilder(text.Length + spans.Count * 20);
        var position = 0;
        for (var i = 0; i < spans.Count; i++)
        {
            var start = Math.Clamp(spans[i].Start, position, text.Length);
            sb.Append(text, position, start - position);
            if (sb.Length > 0 && sb[^1] != '\n')
                sb.Append('\n');
            sb.Append("<!--ffspan:").Append(i).Append("-->\n");
            position = start;
        }
        sb.Append(text, position, text.Length - position);
        return sb.ToString();
    }

    /// <summary>
    /// Removes the markers and returns the clean text with the spans re-expressed over it: each span runs from where
    /// its marker was to the next surviving marker (or the end). Spans whose marker did not survive are dropped.
    /// </summary>
    public static (string Text, IReadOnlyList<SourceSpan> Spans) Extract(string text, IReadOnlyList<SourceSpan> original)
    {
        if (original.Count == 0)
            return (text, []);

        var positions = new List<(int Index, int Position)>();
        var sb = new StringBuilder(text.Length);
        var last = 0;
        foreach (Match m in MarkerPattern().Matches(text))
        {
            sb.Append(text, last, m.Index - last);
            if (int.TryParse(m.Groups[1].ValueSpan, out var index) && index < original.Count)
                positions.Add((index, sb.Length));
            last = m.Index + m.Length;
        }
        sb.Append(text, last, text.Length - last);

        var clean = sb.ToString();
        var spans = new List<SourceSpan>(positions.Count);
        for (var i = 0; i < positions.Count; i++)
        {
            var end = i + 1 < positions.Count ? positions[i + 1].Position : clean.Length;
            var start = positions[i].Position;
            if (end <= start)
                continue;
            spans.Add(original[positions[i].Index] with { Start = start, End = end });
        }
        return (clean, spans);
    }

    /// <summary>Removes any markers without reading them (for text that must not carry them onward).</summary>
    public static string Strip(string text) => MarkerPattern().Replace(text, string.Empty);

    /// <summary>
    /// Writes the pages and times of the spans a chunk overlaps onto its location: first page to last page, earliest
    /// start to latest end. A chunk that overlaps no span is left as it is.
    /// </summary>
    public static void Apply(SourceLocation location, IReadOnlyList<SourceSpan> spans)
    {
        if (spans.Count == 0)
            return;

        int? firstPage = null, lastPage = null;
        TimeSpan? startTime = null, endTime = null;
        foreach (var span in spans)
        {
            var overlaps = span.Start < location.EndChar && location.StartChar < span.End
                || (location.StartChar == location.EndChar && span.Start <= location.StartChar && location.StartChar < span.End);
            if (!overlaps)
                continue;
            if (span.Page is { } page)
            {
                firstPage = firstPage is null ? page : Math.Min(firstPage.Value, page);
                lastPage = lastPage is null ? page : Math.Max(lastPage.Value, page);
            }
            if (span.StartTime is { } s)
                startTime = startTime is null || s < startTime ? s : startTime;
            if (span.EndTime is { } e)
                endTime = endTime is null || e > endTime ? e : endTime;
        }

        if (firstPage is not null)
        {
            location.StartPage = firstPage;
            location.EndPage = lastPage;
        }
        if (startTime is not null)
            location.StartTime = startTime;
        if (endTime is not null)
            location.EndTime = endTime;
    }
}
