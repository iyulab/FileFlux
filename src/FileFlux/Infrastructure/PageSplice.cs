using System.Text;
using FileFlux.Core;

namespace FileFlux.Infrastructure;

/// <summary>
/// Rewrites page text in place and re-expresses the page spans over the result — the one splice behind page-scoped
/// refinement and page reading. Text between spans is kept as it is.
/// </summary>
internal static class PageSplice
{
    /// <summary>
    /// <paramref name="text"/> with each span in <paramref name="replace"/> swapped for its new text, and each page in
    /// <paramref name="insert"/> (a page that has no span — it produced no text) placed between the pages around it,
    /// separated by a blank line. Spans keep their page and are moved to where their text now is.
    /// </summary>
    public static (string Text, IReadOnlyList<SourceSpan> Spans) Apply(
        string text,
        IReadOnlyList<SourceSpan> spans,
        IReadOnlyDictionary<SourceSpan, string> replace,
        IReadOnlyList<(int Page, string Text)>? insert = null)
    {
        var pending = new Queue<(int Page, string Text)>((insert ?? []).OrderBy(i => i.Page));
        var output = new StringBuilder(text.Length);
        var result = new List<SourceSpan>(spans.Count + pending.Count);
        var cursor = 0;

        foreach (var span in spans.OrderBy(s => s.Start))
        {
            output.Append(text, cursor, span.Start - cursor);
            while (span.Page is int page && pending.Count > 0 && pending.Peek().Page < page)
            {
                var (insertPage, insertText) = pending.Dequeue();
                var at = output.Length;
                output.Append(insertText);
                result.Add(new SourceSpan(at, output.Length) { Page = insertPage });
                output.Append("\n\n");
            }

            var start = output.Length;
            output.Append(replace.TryGetValue(span, out var replacement) ? replacement : text[span.Start..span.End]);
            result.Add(span with { Start = start, End = output.Length });
            cursor = span.End;
        }

        output.Append(text, cursor, text.Length - cursor);
        while (pending.Count > 0)
        {
            var (insertPage, insertText) = pending.Dequeue();
            if (output.Length > 0)
            {
                while (output.Length > 0 && output[^1] is '\n' or '\r' or ' ' or '\t')
                    output.Length--;
                output.Append("\n\n");
            }

            var at = output.Length;
            output.Append(insertText);
            result.Add(new SourceSpan(at, output.Length) { Page = insertPage });
        }

        return (output.ToString(), result);
    }

    /// <summary>
    /// <paramref name="body"/> in the place of <paramref name="native"/>: the native text's leading and trailing whitespace
    /// is kept, so a page's text keeps its place between its neighbours.
    /// </summary>
    public static string Reframe(string native, string body)
    {
        var lead = native.Length - native.TrimStart().Length;
        var trail = native.Length - native.TrimEnd().Length;
        return lead + trail >= native.Length ? body : native[..lead] + body + native[^trail..];
    }
}
