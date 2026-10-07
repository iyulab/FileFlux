using FileFlux;
using FileFlux.Core;
using FileFlux.Infrastructure;
using Xunit;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// <see cref="ExtractOptions.PageReading"/>: a selected page is rendered and read through the image-to-text service, and
/// the read replaces the page's text only where the page could not be read — never from an incomplete render, never over
/// a page with tables, and over a page that lost part of its content only when the read keeps what the page had.
/// </summary>
public class PageVisionReadingTests
{
    private const string Page1 = "First page text.";
    private const string Page3 = "Third page text.";

    // Pages 1 and 3 have text; page 2 produced none (a scan), so it has no span.
    private static RawContent ThreePages(params PageQuality[] overrides)
    {
        var text = Page1 + "\n\n" + Page3;
        var quality = new List<PageQuality>
        {
            new(1) { Characters = Page1.Length, TextOperators = 3 },
            new(2) { ImageOperators = 1, ImageCoverage = 0.98 },
            new(3) { Characters = Page3.Length, TextOperators = 3 }
        };
        foreach (var q in overrides)
            quality[q.Page - 1] = q;

        return new RawContent
        {
            Text = text,
            Spans = [new SourceSpan(0, Page1.Length) { Page = 1 }, new SourceSpan(Page1.Length + 2, text.Length) { Page = 3 }],
            Quality = new ExtractionQuality { Pages = quality }
        };
    }

    private static Func<int, int, RenderedPageImage> Render(List<int> rendered, int textGaps = 0) => (page, _) =>
    {
        rendered.Add(page);
        return new RenderedPageImage([0x89, 0x50, 0x4E, 0x47], textGaps, 0, 0);
    };

    private static Task Apply(RawContent content, PageReadingOptions options, Func<int, int, RenderedPageImage> render, string read) =>
        PageVisionReading.ApplyAsync(content, options, render, new FixedReader(read), TestContext.Current.CancellationToken);

    [Fact]
    public async Task APageWithoutText_IsReplacedByItsRead_InPageOrder()
    {
        var content = ThreePages();
        var rendered = new List<int>();

        await Apply(content, new PageReadingOptions { SelectPages = q => !q.HasTextLayer }, Render(rendered), "Scanned page text.");

        Assert.Equal([2], rendered);
        Assert.Equal(Page1 + "\n\nScanned page text.\n\n" + Page3, content.Text);
        Assert.Equal([1, 2, 3], content.Spans.Select(s => s.Page!.Value));
        Assert.All(content.Spans, s => Assert.Equal(s.Page switch { 1 => Page1, 2 => "Scanned page text.", _ => Page3 }, content.Text[s.Start..s.End]));
        var read = Assert.Single(content.PageReads);
        Assert.Equal(PageReadOutcome.Replaced, read.Outcome);
        Assert.Null(read.Reason);
    }

    [Fact]
    public async Task ImagesOnAReplacedPage_AreMarkedReadAsPage()
    {
        var content = ThreePages();
        content.Images.Add(new ImageInfo { Id = "scan", PageNumber = 2 });
        content.Images.Add(new ImageInfo { Id = "logo", PageNumber = 1 });

        await Apply(content, new PageReadingOptions { SelectPages = _ => true }, Render([]), "read");

        Assert.True(content.Images.Single(i => i.Id == "scan").ReadAsPage);
        Assert.False(content.Images.Single(i => i.Id == "logo").ReadAsPage);
    }

    [Fact]
    public async Task AnIncompleteRender_NeverReplacesText()
    {
        var content = ThreePages(new PageQuality(3) { Characters = Page3.Length, TextOperators = 3, SuppressedTextRuns = 2 });
        var before = content.Text;

        await Apply(content, new PageReadingOptions { SelectPages = q => q.SuppressedTextRuns > 0 }, Render([], textGaps: 1), "Third page text, complete.");

        Assert.Equal(before, content.Text);
        var read = Assert.Single(content.PageReads);
        Assert.Equal(PageReadOutcome.Kept, read.Outcome);
        Assert.Equal(PageRead.RenderGaps, read.Reason);
        Assert.Equal(1, read.UnrenderedTextRuns);
        Assert.Equal("Third page text, complete.", read.Text);
    }

    [Fact]
    public async Task APageThatLostContent_IsReplacedOnlyWhenTheReadKeepsItsWords()
    {
        var lossy = new PageQuality(3) { Characters = Page3.Length, TextOperators = 3, UndecodableContentStreams = 1 };
        var options = new PageReadingOptions { SelectPages = q => q.UndecodableContentStreams > 0 };

        var keeps = ThreePages(lossy);
        await Apply(keeps, options, Render([]), "Third page text. And the paragraph that was missing.");
        Assert.Equal(PageReadOutcome.Replaced, Assert.Single(keeps.PageReads).Outcome);
        Assert.EndsWith("Third page text. And the paragraph that was missing.", keeps.Text, StringComparison.Ordinal);

        var drops = ThreePages(lossy);
        var before = drops.Text;
        await Apply(drops, options, Render([]), "Something else entirely.");
        var read = Assert.Single(drops.PageReads);
        Assert.Equal(PageReadOutcome.Kept, read.Outcome);
        Assert.Equal(PageRead.LowCoverage, read.Reason);
        Assert.Equal(0.0, read.NativeCoverage);
        Assert.Equal(before, drops.Text);
    }

    [Fact]
    public async Task AReadablePage_KeepsItsText_AndTheReadIsGuidance()
    {
        var content = ThreePages();
        var before = content.Text;

        await Apply(content, new PageReadingOptions { SelectPages = q => q.Page == 1 }, Render([]), "First page text, as read.");

        Assert.Equal(before, content.Text);
        var read = Assert.Single(content.PageReads);
        Assert.Equal(PageReadOutcome.Kept, read.Outcome);
        Assert.Equal(PageRead.NativeTextReadable, read.Reason);
        Assert.Equal("First page text, as read.", read.Text);
    }

    [Fact]
    public async Task APageWithTables_KeepsItsText()
    {
        var content = ThreePages(new PageQuality(3) { Characters = Page3.Length, TextOperators = 3, ReplacementCharacters = 4 });
        content.Tables.Add(new TableData { PageNumber = 3 });
        var before = content.Text;

        await Apply(content, new PageReadingOptions { SelectPages = q => q.ReplacementCharacters > 0 }, Render([]), "Third page text.");

        Assert.Equal(before, content.Text);
        Assert.Equal(PageRead.PageHasTables, Assert.Single(content.PageReads).Reason);
    }

    [Fact]
    public async Task NoSelection_RendersNothing()
    {
        var content = ThreePages();
        var rendered = new List<int>();

        await Apply(content, new PageReadingOptions(), Render(rendered), "unused");

        Assert.Empty(rendered);
        Assert.Empty(content.PageReads);
    }

    [Fact]
    public async Task PagesPastTheBudget_AreSkippedUnrendered()
    {
        var content = ThreePages();
        var rendered = new List<int>();

        await Apply(content, new PageReadingOptions { SelectPages = _ => true, MaxPages = 2 }, Render(rendered), "read");

        Assert.Equal([1, 2], rendered);
        Assert.Equal([PageReadOutcome.Kept, PageReadOutcome.Replaced, PageReadOutcome.Skipped], content.PageReads.Select(r => r.Outcome));
        Assert.Equal(PageRead.OverBudget, content.PageReads[2].Reason);
    }

    [Fact]
    public async Task AFailedRead_KeepsTheTextAndSaysSo()
    {
        var content = ThreePages();
        var before = content.Text;

        await PageVisionReading.ApplyAsync(content, new PageReadingOptions { SelectPages = q => !q.HasTextLayer }, Render([]),
            new FixedReader("", error: "model unavailable"), TestContext.Current.CancellationToken);

        Assert.Equal(before, content.Text);
        Assert.Equal(PageRead.ReadFailed, Assert.Single(content.PageReads).Reason);
        Assert.Contains(content.Warnings, w => w.Contains("Page 2", StringComparison.Ordinal));
    }

    private sealed class FixedReader(string text, string? error = null) : IImageToTextService
    {
        public IEnumerable<string> SupportedImageFormats => ["png"];

        public string ProviderName => "Fixed";

        public Task<ImageToTextResult> ExtractTextAsync(byte[] imageData, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImageToTextResult { ExtractedText = text, ErrorMessage = error });

        public Task<ImageToTextResult> ExtractTextAsync(Stream imageStream, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ImageToTextResult> ExtractTextAsync(string imagePath, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
