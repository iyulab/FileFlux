using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FileFlux.Core;

namespace FileFlux.Infrastructure;

/// <summary>
/// <see cref="LlmRefineScope.Pages"/>: sends each selected page alone to an <see cref="ILlmRefiner"/>, keeps the output
/// only when it passes the checks against that page, and re-expresses the page spans over the result. Works with any
/// refiner — the scoping and the checks are here, not in the refiner.
/// </summary>
internal static partial class PageScopedRefinement
{
    public static async Task<LlmRefinedContent> RefineAsync(
        ILlmRefiner refiner,
        RefinedContent refined,
        IReadOnlyList<PageQuality> quality,
        LlmRefineOptions options,
        CancellationToken cancellationToken)
    {
        if (!refined.Spans.Any(s => s.Page is not null))
        {
            var unchanged = LlmRefinedContent.FromRefinedContent(refined);
            return new LlmRefinedContent
            {
                RefinedId = unchanged.RefinedId,
                RawId = unchanged.RawId,
                Text = unchanged.Text,
                Sections = unchanged.Sections,
                Spans = unchanged.Spans,
                Structures = unchanged.Structures,
                Metadata = unchanged.Metadata,
                Quality = unchanged.Quality,
                Info = new LlmRefinementInfo { LlmWasUsed = false, SkipReason = "No page spans to refine page by page" }
            };
        }

        var sw = Stopwatch.StartNew();
        var byPage = quality.GroupBy(q => q.Page).ToDictionary(g => g.Key, g => g.First());
        var pageOptions = options.ForSinglePass();
        var text = refined.Text;
        var replace = new Dictionary<SourceSpan, string>();
        var pages = new List<PageRefinement>();
        int inputTokens = 0, outputTokens = 0;

        foreach (var span in refined.Spans.OrderBy(s => s.Start))
        {
            var pageText = text[span.Start..span.End];
            var result = pageText;

            if (span.Page is int page)
            {
                PageRefinement record;
                if (options.SelectPages is { } select && !(byPage.TryGetValue(page, out var q) && select(q)))
                {
                    record = new PageRefinement(page) { Outcome = PageRefinementOutcome.Skipped, Reason = PageRefinement.NotSelected };
                }
                else if (pageText.Length > options.MaxPageCharacters)
                {
                    record = new PageRefinement(page) { Outcome = PageRefinementOutcome.Skipped, Reason = PageRefinement.TooLong };
                }
                else
                {
                    LlmRefinedContent? refinedPage = null;
                    try
                    {
                        refinedPage = await refiner.RefineAsync(PageContent(refined, pageText), pageOptions, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                    {
                        // Recorded below as refiner_failed; the page keeps its text.
                    }

                    inputTokens += refinedPage?.Info.InputTokens ?? 0;
                    outputTokens += refinedPage?.Info.OutputTokens ?? 0;
                    (record, result) = Judge(page, pageText, refinedPage, options);
                }

                pages.Add(record);
            }

            if (result != pageText)
                replace[span] = result;
        }

        var (newText, spans) = PageSplice.Apply(text, refined.Spans, replace);
        sw.Stop();

        var refinedCount = pages.Count(p => p.Outcome == PageRefinementOutcome.Refined);
        return new LlmRefinedContent
        {
            RefinedId = refined.Id,
            RawId = refined.RawId,
            Text = newText,
            // Sections carry offsets into the text; when it changed, the chunk stage rebuilds them from the new text.
            Sections = refined.Sections,
            Spans = spans,
            Pages = pages,
            Structures = refined.Structures,
            Metadata = refined.Metadata,
            Quality = new LlmRefinementQuality
            {
                InputCharCount = text.Length,
                OutputCharCount = newText.Length,
                ImprovementScore = pages.Count == 0 ? 0.0 : (double)refinedCount / pages.Count,
                ConfidenceScore = 1.0
            },
            Info = new LlmRefinementInfo
            {
                LlmWasUsed = refinedCount > 0,
                Model = refiner.ModelName,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                Duration = sw.Elapsed,
                Improvements = refinedCount > 0 ? [$"Refined {refinedCount} of {pages.Count} page(s)"] : [],
                Warnings = pages
                    .Where(p => p.Outcome == PageRefinementOutcome.Rejected || p.Reason == PageRefinement.PassesFailed)
                    .Select(p => $"Page {p.Page} kept its text: {p.Reason}")
                    .ToList()
            }
        };
    }

    /// <summary>
    /// The verdict on one page's refiner output, and the text the page keeps.
    /// </summary>
    internal static (PageRefinement Record, string Text) Judge(
        int page, string native, LlmRefinedContent? refinedPage, LlmRefineOptions options)
    {
        // A refiner that did not run says so (LlmWasUsed false with a SkipReason); the processor only calls an available
        // refiner with improvements enabled, so that is a failure here.
        if (refinedPage is null || refinedPage.Info is { LlmWasUsed: false, SkipReason: not null })
            return (new PageRefinement(page) { Outcome = PageRefinementOutcome.Rejected, Reason = PageRefinement.RefinerFailed }, native);

        var candidate = refinedPage.Text;
        if (string.IsNullOrWhiteSpace(candidate))
            return (new PageRefinement(page) { Outcome = PageRefinementOutcome.Rejected, Reason = PageRefinement.EmptyOutput }, native);

        var passes = refinedPage.Info.Passes;
        var notes = refinedPage.Info.Warnings;
        if (candidate == native)
        {
            return (new PageRefinement(page)
            {
                Outcome = PageRefinementOutcome.Native,
                Reason = UnchangedReason(passes),
                Passes = passes,
                Notes = notes
            }, native);
        }

        var coverage = NativeCoverage(native, candidate);
        var numbersMatched = SameNumbers(native, candidate);
        var record = new PageRefinement(page)
        {
            NativeCoverage = coverage,
            TokenCoverage = TokenCoverage(native, candidate),
            NumbersMatched = numbersMatched,
            Passes = passes,
            Notes = notes
        };

        if (coverage < options.MinNativeCoverage)
            return (record with { Outcome = PageRefinementOutcome.Rejected, Reason = PageRefinement.LowCoverage }, native);
        if (options.RequireSameNumbers && !numbersMatched)
            return (record with { Outcome = PageRefinementOutcome.Rejected, Reason = PageRefinement.NumbersChanged }, native);

        // A page's text keeps its place between its neighbours: the refiner's surrounding whitespace is not its to set.
        return (record with { Outcome = PageRefinementOutcome.Refined }, PageSplice.Reframe(native, candidate.Trim()));
    }

    /// <summary>
    /// Why a page the refiner returned unchanged was not changed by the model: none of its passes was needed, or every
    /// pass that was sent failed. Null when the model kept the page, or when the refiner does not report its passes.
    /// </summary>
    private static string? UnchangedReason(IReadOnlyList<LlmRefinementPass>? passes)
    {
        if (passes is null)
            return null;

        var sent = passes.Where(p => p.Outcome != LlmRefinementPassOutcome.NotNeeded).ToList();
        if (sent.Count == 0)
            return PageRefinement.NoPassNeeded;
        return sent.All(p => p.Outcome == LlmRefinementPassOutcome.Failed) ? PageRefinement.PassesFailed : null;
    }

    /// <summary>
    /// The share of <paramref name="native"/>'s text that <paramref name="candidate"/> still contains, however it is
    /// spaced: each of the page's words (letters and marks, or digits — compatibility-normalized, case-folded) counts as
    /// kept when it occurs in the output with every space, punctuation mark and symbol removed, each occurrence used once,
    /// weighted by its length. Joining a word a line wrap split, splitting a run-on word, re-wrapping or reordering keeps
    /// 1; a dropped or reworded passage lowers it by its share. 1 when the page has no words.
    /// </summary>
    internal static double NativeCoverage(string native, string candidate)
    {
        // Longest first, so a short word cannot take the place a longer one needs.
        var words = Words().Matches(Fold(native)).Select(m => m.Value).OrderByDescending(w => w.Length).ToList();
        var total = words.Sum(w => w.Length);
        if (total == 0)
            return 1.0;

        var text = Letters(candidate);
        var used = new bool[text.Length];
        var kept = 0;
        foreach (var word in words)
        {
            var at = FindUnused(text, used, word);
            if (at < 0)
                continue;

            Array.Fill(used, true, at, word.Length);
            kept += word.Length;
        }

        return (double)kept / total;
    }

    private static int FindUnused(string text, bool[] used, string word)
    {
        for (var at = text.IndexOf(word, StringComparison.Ordinal); at >= 0; at = text.IndexOf(word, at + 1, StringComparison.Ordinal))
        {
            if (Array.IndexOf(used, true, at, word.Length) < 0)
                return at;
        }

        return -1;
    }

    private static string Fold(string text) => text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();

    /// <summary>The letters, marks and digits of <paramref name="text"/>, folded as the words are, with nothing between them.</summary>
    private static string Letters(string text)
    {
        var folded = Fold(text);
        var sb = new StringBuilder(folded.Length);
        foreach (var c in folded)
        {
            switch (char.GetUnicodeCategory(c))
            {
                case UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
                    or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
                    or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
                    or UnicodeCategory.DecimalDigitNumber:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// The share of <paramref name="native"/>'s word tokens (case-folded, counted with multiplicity) that
    /// <paramref name="candidate"/> also has. 1 when the page has no word tokens.
    /// </summary>
    internal static double TokenCoverage(string native, string candidate)
    {
        var have = Count(Words(), candidate);
        var total = 0;
        var kept = 0;
        foreach (var (token, count) in Count(Words(), native))
        {
            total += count;
            kept += Math.Min(count, have.GetValueOrDefault(token));
        }

        return total == 0 ? 1.0 : (double)kept / total;
    }

    /// <summary>Whether both texts have the same numbers, counted with multiplicity.</summary>
    internal static bool SameNumbers(string native, string candidate)
    {
        var a = Count(Numbers(), native);
        var b = Count(Numbers(), candidate);
        return a.Count == b.Count && a.All(kv => b.GetValueOrDefault(kv.Key) == kv.Value);
    }

    private static Dictionary<string, int> Count(Regex pattern, string text)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in pattern.Matches(text))
        {
            var token = m.Value.ToLowerInvariant();
            counts[token] = counts.GetValueOrDefault(token) + 1;
        }

        return counts;
    }

    private static RefinedContent PageContent(RefinedContent refined, string pageText) => new()
    {
        RawId = refined.RawId,
        Text = pageText,
        Metadata = refined.Metadata
    };

    [GeneratedRegex(@"[\p{L}\p{M}]+|\p{Nd}+")]
    private static partial Regex Words();

    [GeneratedRegex(@"\d+(?:[.,]\d+)*")]
    private static partial Regex Numbers();
}
