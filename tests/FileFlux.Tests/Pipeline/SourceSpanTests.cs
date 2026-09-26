using FileFlux.Core;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Conversion;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// A reader's source spans (pages, time ranges) survive refinement and land on the chunks that overlap them.
/// </summary>
public class SourceSpanTests
{
    private static readonly string[] Fruits = ["apples", "bananas", "cherries"];

    private static readonly string[] Pages =
    [
        "Page one talks about apples.\n\nApples are red.",
        "Page two talks about bananas.\n\nBananas are yellow.",
        "Page three talks about cherries.\n\nCherries are dark red.",
    ];

    private static (string Text, List<SourceSpan> Spans) ThreePages(string separator = "\n\n")
    {
        var spans = new List<SourceSpan>();
        var text = "";
        for (var i = 0; i < Pages.Length; i++)
        {
            if (i > 0)
                text += separator;
            spans.Add(new SourceSpan(text.Length, text.Length + Pages[i].Length) { Page = i + 1 });
            text += Pages[i];
        }
        return (text, spans);
    }

    [Fact]
    public void Markers_RoundTripToTheSameOffsets()
    {
        var (text, spans) = ThreePages();

        var (clean, extracted) = SourceSpanMarkers.Extract(SourceSpanMarkers.Insert(text, spans), spans);

        Assert.Equal(text, clean);
        Assert.Equal([1, 2, 3], extracted.Select(s => s.Page!.Value));
        Assert.Equal(0, extracted[0].Start);
        Assert.Equal(clean.IndexOf("Page two", StringComparison.Ordinal), extracted[1].Start);
        Assert.Equal(clean.Length, extracted[^1].End);
    }

    [Fact]
    public void Markers_ThatDoNotSurvive_DropTheirSpan()
    {
        var (text, spans) = ThreePages();
        var marked = SourceSpanMarkers.Insert(text, spans).Replace("<!--ffspan:1-->\n", "", StringComparison.Ordinal);

        var (clean, extracted) = SourceSpanMarkers.Extract(marked, spans);

        Assert.Equal([1, 3], extracted.Select(s => s.Page!.Value));
        Assert.Contains("bananas", clean[extracted[0].Start..extracted[0].End], StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_CombinesTheOverlappedSpans()
    {
        var spans = new List<SourceSpan>
        {
            new(0, 10) { Page = 1, StartTime = TimeSpan.FromSeconds(0), EndTime = TimeSpan.FromSeconds(4) },
            new(10, 20) { Page = 2, StartTime = TimeSpan.FromSeconds(4), EndTime = TimeSpan.FromSeconds(9) },
            new(20, 30) { Page = 3 },
        };

        var within = new SourceLocation { StartChar = 2, EndChar = 8 };
        var across = new SourceLocation { StartChar = 5, EndChar = 15 };
        var outside = new SourceLocation { StartChar = 40, EndChar = 50 };
        SourceSpanMarkers.Apply(within, spans);
        SourceSpanMarkers.Apply(across, spans);
        SourceSpanMarkers.Apply(outside, spans);

        Assert.Equal((1, 1), (within.StartPage, within.EndPage));
        Assert.Equal((1, 2), (across.StartPage, across.EndPage));
        Assert.Equal((TimeSpan.Zero, TimeSpan.FromSeconds(9)), (across.StartTime!.Value, across.EndTime!.Value));
        Assert.Null(outside.StartPage);
    }

    /// <summary>
    /// The default refine path — noise cleaning, markdown conversion, normalization, FluxCurator's text refiner —
    /// changes whitespace and removes lines, and the spans still name the right stretches of the refined text.
    /// </summary>
    [Fact]
    public async Task DefaultRefine_CarriesSpansOverTheRefinedText()
    {
        var (text, spans) = ThreePages(separator: "\n\n\n\n\n## Paragraph 7\n\n");
        var raw = new RawContent
        {
            Text = text.Replace("Apples are red.", "Apples    are    red.", StringComparison.Ordinal),
            File = new SourceFileInfo { Name = "fruit.txt", Extension = ".txt" },
        };
        raw.Spans = SpansFor(raw.Text);
        var refiner = new DocumentRefiner(new MarkdownConverter(), logger: NullLogger<DocumentRefiner>.Instance);

        var refined = await refiner.RefineAsync(raw, cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("ffspan", refined.Text, StringComparison.Ordinal);
        Assert.Equal([1, 2, 3], refined.Spans.Select(s => s.Page!.Value));
        for (var i = 0; i < 3; i++)
        {
            var slice = refined.Text[refined.Spans[i].Start..refined.Spans[i].End];
            Assert.Contains(Fruits[i], slice, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task NoSpans_LeavesRefinementUnchanged()
    {
        var raw = new RawContent { Text = "Just text.", File = new SourceFileInfo { Name = "a.txt", Extension = ".txt" } };
        var refiner = new DocumentRefiner(new MarkdownConverter(), logger: NullLogger<DocumentRefiner>.Instance);

        var refined = await refiner.RefineAsync(raw, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(refined.Spans);
        Assert.Equal("Just text.", refined.Text);
    }

    private static List<SourceSpan> SpansFor(string text)
    {
        var starts = new[] { 0, text.IndexOf("Page two", StringComparison.Ordinal), text.IndexOf("Page three", StringComparison.Ordinal) };
        return
        [
            new SourceSpan(starts[0], starts[1]) { Page = 1 },
            new SourceSpan(starts[1], starts[2]) { Page = 2 },
            new SourceSpan(starts[2], text.Length) { Page = 3 },
        ];
    }
}
