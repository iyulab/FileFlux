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
    /// closing fence line; a fence that is never closed runs to the end of the text (CommonMark) unless
    /// <paramref name="includeUnclosed"/> is false. Ordered and disjoint.
    /// </summary>
    /// <param name="text">The Markdown text.</param>
    /// <param name="includeUnclosed">Whether an unclosed fence counts as a block to the end of the text.</param>
    internal static List<(int Start, int End)> FindRanges(string text, bool includeUnclosed = true)
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

        if (state.IsOpen && includeUnclosed)
            ranges.Add((fenceStart, text.Length));

        return ranges;
    }

    /// <summary>
    /// Runs a text-level <paramref name="step"/> (a cleaner, a normalizer, a deduplicator) over <paramref name="text"/>
    /// with every fenced block hidden from it: each block, fence lines included, is replaced by one inert placeholder
    /// line for the step and put back exactly as written afterwards. A step that tidies prose - trims lines, drops a line
    /// that repeats, collapses blank lines, takes a lone number for a page number - never touches code, and the step still
    /// sees one continuous text (a line repeated across the blocks is counted as before). Only a closed fence is a block
    /// here (an unclosed one is left to the step: a tilde line is as often a separator as a fence). If the step removed a
    /// placeholder, the blocks cannot be put back in place and the text is returned unchanged: a skipped step is better
    /// than lost code. A text with no fence goes to the step as it is.
    /// </summary>
    internal static string Shielded(string text, Func<string, string> step)
    {
        var shield = Shield(text);
        if (shield.IsEmpty)
            return step(text);

        return shield.Restore(step(shield.Text)) ?? text;
    }

    /// <summary>
    /// Replaces each fenced block of <paramref name="text"/> by a placeholder line; <see cref="FenceShield.Restore"/> puts the
    /// blocks back into the (processed) text.
    /// </summary>
    internal static FenceShield Shield(string text)
    {
        // Only a closed fence is hidden: a text-level step is a heuristic over text of any origin, and a line of tildes
        // ("~~~~~", an ASCII separator a refiner turns into a rule) must not hand the whole rest of the document to code.
        var ranges = FindRanges(text, includeUnclosed: false);
        if (ranges.Count == 0)
            return new FenceShield(text, []);

        var nonce = Guid.NewGuid().ToString("N");
        var shielded = new StringBuilder(text.Length);
        var blocks = new List<(string Placeholder, string Block)>(ranges.Count);
        var position = 0;
        foreach (var (start, end) in ranges)
        {
            shielded.Append(text, position, start - position);

            // The line ending stays in the text, so the placeholder is a line of its own like the block was.
            var endingLength = text[start..end] switch
            {
                var block when block.EndsWith("\r\n", StringComparison.Ordinal) => 2,
                var block when block.EndsWith('\n') => 1,
                _ => 0,
            };
            var placeholder = $"<!-- fenced-block {nonce} {blocks.Count} -->";
            blocks.Add((placeholder, text.Substring(start, end - start - endingLength)));
            shielded.Append(placeholder).Append(text, end - endingLength, endingLength);
            position = end;
        }

        shielded.Append(text, position, text.Length - position);
        return new FenceShield(shielded.ToString(), blocks);
    }

    /// <summary>A text with its fenced blocks replaced by placeholder lines (<see cref="Shield"/>).</summary>
    internal sealed class FenceShield(string text, List<(string Placeholder, string Block)> blocks)
    {
        /// <summary>The text with a placeholder line where each fenced block was.</summary>
        internal string Text { get; } = text;

        /// <summary>Whether the text had no fenced block (nothing is hidden).</summary>
        internal bool IsEmpty => blocks.Count == 0;

        /// <summary>
        /// <paramref name="processed"/> (the text after a step) with every block put back in place of its placeholder, or
        /// null when the step removed a placeholder.
        /// </summary>
        internal string? Restore(string processed)
        {
            foreach (var (placeholder, _) in blocks)
            {
                if (!processed.Contains(placeholder, StringComparison.Ordinal))
                    return null;
            }

            return RestorePresent(processed);
        }

        /// <summary>
        /// <paramref name="part"/> (a piece of the shielded text, e.g. one section) with the blocks whose placeholder it
        /// holds put back; a placeholder it does not hold is not needed.
        /// </summary>
        internal string RestorePresent(string part)
        {
            foreach (var (placeholder, block) in blocks)
                part = part.Replace(placeholder, block, StringComparison.Ordinal);

            return part;
        }
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
