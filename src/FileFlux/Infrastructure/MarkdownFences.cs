using System.Text;

namespace FileFlux.Infrastructure;

/// <summary>
/// Finds the fenced code blocks of a Markdown text — the one scan every text-level heading, list and whitespace rule
/// shares, so a line inside a fence (a shell comment starting with <c>#</c>, a list-looking line of a sample) is never
/// read as document structure.
/// </summary>
/// <remarks>
/// CommonMark fences: an opening line of three or more backticks or tildes (a backtick fence's info string holds no
/// backtick); a closing line of the same character, at least as long as the opening, and nothing after it but
/// whitespace; a fence that is never closed runs to the end of the text. Indentation is not limited to three spaces —
/// a fence inside a list item sits at the item's content indent.
/// </remarks>
internal static class MarkdownFences
{
    /// <summary>
    /// The character ranges of the fenced blocks, each from the start of its opening fence line to the end of its
    /// closing fence line (to the end of the text when the fence is never closed). Ordered and disjoint.
    /// </summary>
    internal static List<(int Start, int End)> FindRanges(string text)
    {
        var ranges = new List<(int Start, int End)>();
        var state = new FenceState();
        var fenceStart = 0;
        var position = 0;

        while (position < text.Length)
        {
            var newline = text.IndexOf('\n', position);
            var contentEnd = newline < 0 ? text.Length : newline;
            var lineEnd = newline < 0 ? text.Length : newline + 1;

            switch (state.Feed(text.AsSpan(position, contentEnd - position)))
            {
                case FenceLine.Opens:
                    fenceStart = position;
                    break;
                case FenceLine.Closes:
                    ranges.Add((fenceStart, lineEnd));
                    break;
            }

            position = lineEnd;
        }

        if (state.IsOpen)
            ranges.Add((fenceStart, text.Length));

        return ranges;
    }

    /// <summary>
    /// Applies <paramref name="transform"/> to the text between the fenced blocks and leaves every fenced block (fence
    /// lines included) exactly as it is. The segments the transform sees are whole lines, so a line-based rule behaves
    /// the same as it would on the whole text. The whitespace where a segment meets a fence is kept as it was: a
    /// transform that trims its input must not glue the fence to the line before or after it. A text with no fence is
    /// transformed whole.
    /// </summary>
    internal static string MapOutside(string text, Func<string, string> transform)
    {
        var ranges = FindRanges(text);
        if (ranges.Count == 0)
            return transform(text);

        var result = new StringBuilder(text.Length);
        var position = 0;
        foreach (var (start, end) in ranges)
        {
            if (start > position)
                result.Append(Transform(text.Substring(position, start - position), keepLeading: position > 0, keepTrailing: true, transform));

            result.Append(text, start, end - start);
            position = end;
        }

        if (position < text.Length)
            result.Append(Transform(text.Substring(position), keepLeading: true, keepTrailing: false, transform));

        return result.ToString();
    }

    private static string Transform(string segment, bool keepLeading, bool keepTrailing, Func<string, string> transform)
    {
        var leadingLength = keepLeading ? segment.Length - segment.TrimStart().Length : 0;
        var core = segment[leadingLength..];
        var trailingLength = keepTrailing ? core.Length - core.TrimEnd().Length : 0;
        core = core[..^trailingLength];

        return segment[..leadingLength] + transform(core) + segment[^trailingLength..];
    }

    /// <summary>
    /// Whether <paramref name="index"/> lies inside one of <paramref name="ranges"/> (from <see cref="FindRanges"/>).
    /// </summary>
    internal static bool Contains(IReadOnlyList<(int Start, int End)> ranges, int index)
    {
        var low = 0;
        var high = ranges.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var (start, end) = ranges[middle];
            if (index < start)
                high = middle - 1;
            else if (index >= end)
                low = middle + 1;
            else
                return true;
        }

        return false;
    }

    /// <summary>
    /// Per line of a document: whether the line belongs to a fenced block (the fence lines themselves included).
    /// </summary>
    internal static bool[] LineMask(IReadOnlyList<string> lines)
    {
        var mask = new bool[lines.Count];
        var state = new FenceState();
        for (var i = 0; i < lines.Count; i++)
            mask[i] = state.Feed(lines[i]) != FenceLine.Outside;

        return mask;
    }

    private enum FenceLine
    {
        Outside,
        Opens,
        Inside,
        Closes,
    }

    private struct FenceState
    {
        private char _marker;
        private int _length;

        internal readonly bool IsOpen => _marker != '\0';

        internal FenceLine Feed(ReadOnlySpan<char> line)
        {
            var trimmed = line.TrimStart();
            var (marker, length) = Run(trimmed);

            if (IsOpen)
            {
                if (marker == _marker && length >= _length && trimmed[length..].IsWhiteSpace())
                {
                    _marker = '\0';
                    _length = 0;
                    return FenceLine.Closes;
                }

                return FenceLine.Inside;
            }

            if (length >= 3 && (marker == '~' || !trimmed[length..].Contains('`')))
            {
                _marker = marker;
                _length = length;
                return FenceLine.Opens;
            }

            return FenceLine.Outside;
        }

        private static (char Marker, int Length) Run(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty || text[0] is not ('`' or '~'))
                return ('\0', 0);

            var length = 1;
            while (length < text.Length && text[length] == text[0])
                length++;

            return (text[0], length);
        }
    }
}
