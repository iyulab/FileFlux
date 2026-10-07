using FileFlux.Core;

namespace FileFlux.Infrastructure;

/// <summary>A rendered page and what the render could not paint.</summary>
internal sealed record RenderedPageImage(byte[] Png, int UnrenderedTextRuns, int UnrenderedImages, int UnrenderedContentStreams)
{
    public bool IsComplete => UnrenderedTextRuns == 0 && UnrenderedImages == 0 && UnrenderedContentStreams == 0;
}

/// <summary>
/// <see cref="ExtractOptions.PageReading"/>: renders the pages the consumer's predicate selects, reads each through an
/// <see cref="IImageToTextService"/>, and lets a read replace a page's text only where the page could not be read (see
/// <see cref="PageReadingOptions"/>). Every selected page gets a <see cref="PageRead"/>. Rendering is a delegate so the
/// rule is the same for every source of pages.
/// </summary>
internal static class PageVisionReading
{
    public static async Task ApplyAsync(
        RawContent content,
        PageReadingOptions options,
        Func<int, int, RenderedPageImage> render,
        IImageToTextService reader,
        CancellationToken cancellationToken)
    {
        if (options.SelectPages is not { } select)
            return;

        var selected = content.Quality.Pages.Where(select).OrderBy(q => q.Page).ToList();
        if (selected.Count == 0)
            return;

        var spansByPage = content.Spans
            .Where(s => s.Page is not null)
            .GroupBy(s => s.Page!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Start).ToList());
        var tablePages = content.Tables.Select(t => t.PageNumber).ToHashSet();
        var reads = new List<PageRead>(selected.Count);
        var replace = new Dictionary<SourceSpan, string>();
        var insert = new List<(int Page, string Text)>();
        var rendered = 0;

        foreach (var quality in selected)
        {
            var page = quality.Page;
            if (options.MaxPages is int max && rendered >= max)
            {
                reads.Add(new PageRead(page) { Outcome = PageReadOutcome.Skipped, Reason = PageRead.OverBudget });
                continue;
            }

            rendered++;
            RenderedPageImage image;
            try
            {
                image = render(page, options.Dpi);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                reads.Add(new PageRead(page) { Outcome = PageReadOutcome.Failed, Reason = PageRead.RenderFailed });
                continue;
            }

            var record = new PageRead(page)
            {
                UnrenderedTextRuns = image.UnrenderedTextRuns,
                UnrenderedImages = image.UnrenderedImages,
                UnrenderedContentStreams = image.UnrenderedContentStreams
            };

            string? read;
            try
            {
                var result = await reader.ExtractTextAsync(
                    image.Png,
                    new ImageToTextOptions { ImageTypeHint = "document", ExtractStructure = true },
                    cancellationToken).ConfigureAwait(false);
                read = string.IsNullOrEmpty(result.ErrorMessage) ? result.ExtractedText?.Trim() : null;
                if (read is null)
                {
                    reads.Add(record with { Outcome = PageReadOutcome.Failed, Reason = PageRead.ReadFailed });
                    continue;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                reads.Add(record with { Outcome = PageReadOutcome.Failed, Reason = PageRead.ReadFailed });
                continue;
            }

            if (read.Length == 0)
            {
                reads.Add(record with { Outcome = PageReadOutcome.Failed, Reason = PageRead.EmptyRead });
                continue;
            }

            record = record with { Text = read };
            var spans = spansByPage.GetValueOrDefault(page) ?? [];
            var native = string.Concat(spans.Select(s => content.Text[s.Start..s.End]));
            var absent = !quality.HasTextLayer || quality.Characters == 0 || quality.OcrLayerSuppressed || native.Trim().Length == 0;
            var lost = quality.UndecodableContentStreams > 0 || quality.SuppressedTextRuns > 0 || quality.ReplacementCharacters > 0;

            if (!absent && !lost)
            {
                reads.Add(record with { Outcome = PageReadOutcome.Kept, Reason = PageRead.NativeTextReadable });
                continue;
            }

            if (!image.IsComplete)
            {
                reads.Add(record with { Outcome = PageReadOutcome.Kept, Reason = PageRead.RenderGaps });
                continue;
            }

            if (tablePages.Contains(page))
            {
                reads.Add(record with { Outcome = PageReadOutcome.Kept, Reason = PageRead.PageHasTables });
                continue;
            }

            if (!absent)
            {
                var coverage = PageScopedRefinement.TokenCoverage(native, read);
                record = record with { NativeCoverage = coverage };
                if (coverage < options.MinNativeCoverage)
                {
                    reads.Add(record with { Outcome = PageReadOutcome.Kept, Reason = PageRead.LowCoverage });
                    continue;
                }
            }

            if (spans.Count == 0)
            {
                insert.Add((page, read));
            }
            else
            {
                // The page's text goes where its first span was; any further span of the page is emptied.
                replace[spans[0]] = PageSplice.Reframe(content.Text[spans[0].Start..spans[0].End], read);
                foreach (var extra in spans.Skip(1))
                    replace[extra] = string.Empty;
            }

            reads.Add(record with { Outcome = PageReadOutcome.Replaced });
        }

        if (replace.Count > 0 || insert.Count > 0)
        {
            var (text, newSpans) = PageSplice.Apply(content.Text, content.Spans, replace, insert);
            content.Text = text;
            content.Spans = newSpans.Where(s => s.End > s.Start).ToList();
        }

        content.PageReads = reads;
        var replacedPages = reads.Where(r => r.Outcome == PageReadOutcome.Replaced).Select(r => r.Page).ToHashSet();
        foreach (var image in content.Images.Where(i => i.PageNumber is int p && replacedPages.Contains(p)))
            image.ReadAsPage = true;
        var replaced = reads.Count(r => r.Outcome == PageReadOutcome.Replaced);
        content.Hints["page_reads"] = reads.Count;
        content.Hints["page_reads_replaced"] = replaced;
        foreach (var failed in reads.Where(r => r.Outcome == PageReadOutcome.Failed))
            content.Warnings.Add($"Page {failed.Page} could not be read as an image ({failed.Reason}); it keeps its text");
    }
}
