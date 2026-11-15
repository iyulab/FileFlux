using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure;

/// <summary>
/// Finds the fenced code blocks of a Markdown text — the one scan every text-level heading, list and whitespace rule
/// shares, so a line inside a fence (a shell comment starting with <c>#</c>, a list-looking line of a sample) is never
/// read as document structure — and hides them from text-level steps (<see cref="Shielded"/>).
/// </summary>
/// <remarks>
/// CommonMark fences: an opening line of three or more backticks or tildes (a backtick fence's info string holds no
/// backtick); a closing line of the same character, at least as long as the opening, indented at most three spaces
/// deeper than the opening, and nothing after it but whitespace; a fence that is never closed runs to the end of the
/// text. The opening's indentation is not limited to three spaces — a fence inside a list item sits at the item's content
/// indent.
/// </remarks>
internal static partial class MarkdownFences
{
    // The placeholder of a hidden block: private-use delimiters around a short label, so no cleaning pattern for markup
    // (an HTML-comment remover), whitespace or repeated characters (the index digits are separated) rewrites it.
    private const char PlaceholderStart = '';
    private const char PlaceholderEnd = '';
    private const string PlaceholderLabel = "fenced";

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
    /// here (an unclosed one is left to the step: a tilde line is as often a separator as a fence), and a bare line of four
    /// or more tildes is a separator, not a fence. If the step removed a placeholder, the blocks cannot be put back in
    /// place and the text is returned unchanged: a skipped step is better than lost code. A text with no fence goes to
    /// the step as it is.
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
        // A text that already holds the placeholder delimiters cannot be shielded unambiguously: nothing is hidden. A model
        // must not be sent such a text with fenced code in it (UnsafeToSend).
        if (text.Contains(PlaceholderStart, StringComparison.Ordinal) || text.Contains(PlaceholderEnd, StringComparison.Ordinal))
            return new FenceShield(text, [], unsafeToSend: FindRanges(text, includeUnclosed: false).Any(range => !IsSeparatorBanner(text, range)));

        var ranges = FindRanges(text, includeUnclosed: false).Where(range => !IsSeparatorBanner(text, range)).ToList();
        if (ranges.Count == 0)
            return new FenceShield(text, []);

        var shielded = new StringBuilder(text.Length);
        var blocks = new List<FencedBlock>(ranges.Count);
        var position = 0;
        var shieldedLine = 0;
        foreach (var (start, end) in ranges)
        {
            shieldedLine += CountNewlines(text, position, start);
            shielded.Append(text, position, start - position);

            // The line ending stays in the text, so the placeholder is a line of its own like the block was.
            var endingLength = text[start..end] switch
            {
                var block when block.EndsWith("\r\n", StringComparison.Ordinal) => 2,
                var block when block.EndsWith('\n') => 1,
                _ => 0,
            };
            var placeholder = Placeholder(blocks.Count);
            var blockText = text.Substring(start, end - start - endingLength);
            blocks.Add(new FencedBlock(blockText, shielded.Length, start, shieldedLine, CountNewlines(blockText, 0, blockText.Length), placeholder.Length));
            shielded.Append(placeholder).Append(text, end - endingLength, endingLength);
            shieldedLine += CountNewlines(text, end - endingLength, end);
            position = end;
        }

        shielded.Append(text, position, text.Length - position);
        var shieldedText = shielded.ToString();
        // A line that looks like a model marker outside the blocks (a page about this library) could not be told from one.
        return new FenceShield(shieldedText, blocks, unsafeToSend: ModelMarker().IsMatch(shieldedText));
    }

    [GeneratedRegex(@"\[\[code-block (\d+)\]\]", RegexOptions.CultureInvariant)]
    private static partial Regex ModelMarker();

    private static string ModelMarkerText(int index) => $"[[code-block {index.ToString(CultureInfo.InvariantCulture)}]]";

    private static string Placeholder(int index) =>
        PlaceholderStart + PlaceholderLabel + string.Join('x', index.ToString(CultureInfo.InvariantCulture).AsEnumerable()) + PlaceholderEnd;

    private static int CountNewlines(string text, int start, int end)
    {
        var count = 0;
        for (var i = start; i < end; i++)
        {
            if (text[i] == '\n')
                count++;
        }

        return count;
    }

    /// <summary>
    /// Whether a «block» is a pair of separator lines (a bare run of four or more tildes, the ASCII rule a refiner turns
    /// into <c>---</c>) rather than a fence; a real tilde fence has an info string or is exactly three tildes.
    /// </summary>
    private static bool IsSeparatorBanner(string text, (int Start, int End) range)
    {
        var lineEnd = text.IndexOf('\n', range.Start);
        var firstLine = text.AsSpan(range.Start, (lineEnd < 0 ? text.Length : lineEnd) - range.Start).Trim();
        return firstLine.Length >= 4 && firstLine.IndexOfAnyExcept('~') < 0;
    }

    internal readonly record struct FencedBlock(string Text, int ShieldedStart, int OriginalStart, int ShieldedLine, int ExtraLines, int PlaceholderLength);

    /// <summary>A text with its fenced blocks replaced by placeholder lines (<see cref="Shield"/>).</summary>
    internal sealed class FenceShield
    {
        private readonly List<FencedBlock> _blocks;

        internal FenceShield(string text, List<FencedBlock> blocks, bool unsafeToSend = false)
        {
            Text = text;
            _blocks = blocks;
            UnsafeToSend = unsafeToSend;
        }

        /// <summary>The text with a placeholder line where each fenced block was.</summary>
        internal string Text { get; }

        /// <summary>
        /// Whether the text holds fenced code that cannot be hidden from a language model without ambiguity: it already holds
        /// the placeholder's private-use delimiters (nothing was hidden) or a <c>[[code-block N]]</c> line of its own (the
        /// markers of <see cref="ModelText"/> could not be told from it). Such a text is not sent to a model.
        /// </summary>
        internal bool UnsafeToSend { get; }

        /// <summary>Whether nothing is hidden (the text had no fenced block).</summary>
        internal bool IsEmpty => _blocks.Count == 0;

        /// <summary>
        /// <see cref="Text"/> for a language model: each marker written as <c>[[code-block N]]</c>. The private-use delimiters of
        /// the placeholder are not seen by a model — measured with a local model, which answered every one as a backtick fence
        /// (<c>```fenced0```</c>) — while a marker of plain characters comes back as it was sent.
        /// </summary>
        internal string ModelText => _modelText ??= BuildModelText();

        private string? _modelText;

        private string BuildModelText()
        {
            var text = Text;
            for (var i = 0; i < _blocks.Count; i++)
                text = text.Replace(Placeholder(i), ModelMarkerText(i), StringComparison.Ordinal);

            return text;
        }

        /// <summary>
        /// A model's answer to <see cref="ModelText"/> with every block put back, or null when the answer does not carry each
        /// marker exactly once and in the order it was sent: a marker the model dropped loses a block, one it repeated would put
        /// the block in twice and one it moved would reorder the code — the rule-based steps never do that, a model might.
        /// </summary>
        internal string? RestoreModelAnswer(string answer)
        {
            // Nothing was hidden from such a text (or its markers cannot be told from the text): no answer can be vouched for.
            if (UnsafeToSend)
                return null;

            var markers = ModelMarker().Matches(answer);
            if (markers.Count != _blocks.Count)
                return null;

            for (var i = 0; i < markers.Count; i++)
            {
                if (!int.TryParse(markers[i].Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index != i)
                    return null;
            }

            // Each marker is a line of its own - spaces around it are the model's, not the block's: a marker a model wrapped in
            // backticks or set in a sentence would leave stray characters next to the block. The line becomes the placeholder.
            var rebuilt = new StringBuilder(answer.Length);
            var position = 0;
            foreach (Match marker in markers)
            {
                var lineStart = marker.Index == 0 ? 0 : answer.LastIndexOf('\n', marker.Index - 1) + 1;
                var next = answer.IndexOf('\n', marker.Index + marker.Length);
                var lineEnd = next < 0 ? answer.Length : next;
                if (lineEnd > marker.Index + marker.Length && answer[lineEnd - 1] == '\r')
                    lineEnd--;
                if (!string.IsNullOrWhiteSpace(answer[lineStart..marker.Index]) || !string.IsNullOrWhiteSpace(answer[(marker.Index + marker.Length)..lineEnd]))
                    return null;

                rebuilt.Append(answer, position, lineStart - position).Append(Placeholder(int.Parse(marker.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture)));
                position = lineEnd;
            }

            rebuilt.Append(answer, position, answer.Length - position);
            var restored = Restore(rebuilt.ToString());
            if (restored is null)
                return null;

            // The code in the restored text is the code that was hidden, block for block: a model that wrapped a marker in a
            // fence pair of its own, or added a fence, changed what the text reads as code.
            return Shield(restored).BlockTexts.SequenceEqual(BlockTexts) ? restored : null;
        }

        /// <summary>The text of each hidden block, in order.</summary>
        internal IReadOnlyList<string> BlockTexts => _blocks.ConvertAll(block => block.Text);

        /// <summary>
        /// <paramref name="processed"/> (the text after a step) with every block put back in place of its placeholder, or
        /// null when the step removed a placeholder. A block always comes back on lines of its own: a step that glued the
        /// placeholder to the prose around it does not glue the fence to it.
        /// </summary>
        internal string? Restore(string processed) => RestoreBlocks(processed, requireAll: true);

        /// <summary>
        /// <paramref name="part"/> (a piece of the shielded text, e.g. one section) with the blocks whose placeholder it
        /// holds put back; a placeholder it does not hold is not needed.
        /// </summary>
        internal string RestorePresent(string part) => RestoreBlocks(part, requireAll: false) ?? part;

        /// <summary>A character offset in the shielded text, as an offset in the original text.</summary>
        internal int ToOriginalOffset(int shieldedOffset)
        {
            var delta = 0;
            foreach (var block in _blocks)
            {
                if (shieldedOffset < block.ShieldedStart)
                    break;
                if (shieldedOffset < block.ShieldedStart + block.PlaceholderLength)
                    return block.OriginalStart;

                delta = block.OriginalStart + block.Text.Length - (block.ShieldedStart + block.PlaceholderLength);
            }

            return shieldedOffset + delta;
        }

        /// <summary>A zero-based line index in the shielded text, as a line index in the original text.</summary>
        internal int ToOriginalLine(int shieldedLine)
        {
            var extra = 0;
            foreach (var block in _blocks)
            {
                if (block.ShieldedLine >= shieldedLine)
                    break;

                extra += block.ExtraLines;
            }

            return shieldedLine + extra;
        }

        private string? RestoreBlocks(string text, bool requireAll)
        {
            var result = new StringBuilder(text.Length + 64);
            var seen = new bool[_blocks.Count];
            var position = 0;
            while (position < text.Length)
            {
                var open = text.IndexOf(PlaceholderStart, position);
                var close = open < 0 ? -1 : text.IndexOf(PlaceholderEnd, open + 1);
                if (close < 0)
                    break;

                result.Append(text, position, open - position);
                if (TryReadIndex(text.AsSpan(open + 1, close - open - 1), out var index) && index < _blocks.Count)
                {
                    if (result.Length > 0 && result[^1] != '\n')
                        result.Append('\n');

                    result.Append(_blocks[index].Text);
                    seen[index] = true;
                    position = close + 1;
                    if (position < text.Length && text[position] is not ('\n' or '\r'))
                        result.Append('\n');
                }
                else
                {
                    result.Append(text, open, close + 1 - open);
                    position = close + 1;
                }
            }

            result.Append(text, position, text.Length - position);
            return requireAll && Array.Exists(seen, found => !found) ? null : result.ToString();
        }

        private static bool TryReadIndex(ReadOnlySpan<char> label, out int index)
        {
            index = 0;
            if (!label.StartsWith(PlaceholderLabel, StringComparison.Ordinal))
                return false;

            var digits = label[PlaceholderLabel.Length..];
            var value = 0;
            for (var i = 0; i < digits.Length; i++)
            {
                if (i % 2 == 1)
                {
                    if (digits[i] != 'x')
                        return false;

                    continue;
                }

                if (digits[i] is < '0' or > '9' || value > (int.MaxValue - 9) / 10)
                    return false;

                value = (value * 10) + (digits[i] - '0');
            }

            index = value;
            return digits.Length > 0 && digits.Length % 2 == 1;
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
        private int _indent;

        internal readonly bool IsOpen => _marker != '\0';

        internal FenceLine Feed(ReadOnlySpan<char> line)
        {
            var trimmed = line.TrimStart();
            var indent = Indent(line[..(line.Length - trimmed.Length)]);
            var (marker, length) = Run(trimmed);

            if (IsOpen)
            {
                // A closing fence sits at most three columns deeper than its opening (CommonMark): a more deeply indented
                // fence-looking line is code inside the block, not its end.
                if (marker == _marker && length >= _length && indent <= _indent + 3 && trimmed[length..].IsWhiteSpace())
                {
                    _marker = '\0';
                    _length = 0;
                    _indent = 0;
                    return FenceLine.Closes;
                }

                return FenceLine.Inside;
            }

            if (length >= 3 && (marker == '~' || !trimmed[length..].Contains('`')))
            {
                _marker = marker;
                _length = length;
                _indent = indent;
                return FenceLine.Opens;
            }

            return FenceLine.Outside;
        }

        private static int Indent(ReadOnlySpan<char> whitespace)
        {
            var columns = 0;
            foreach (var c in whitespace)
                columns += c == '\t' ? 4 : 1;

            return columns;
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
