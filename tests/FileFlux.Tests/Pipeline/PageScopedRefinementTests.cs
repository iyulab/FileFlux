using FileFlux.Core;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FluxCurator.Infrastructure.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// <see cref="LlmRefineScope.Pages"/>: each page goes alone to the refiner, an output that drops words or changes a
/// number is rejected and the page keeps its text, and the page spans are re-expressed over the result.
/// </summary>
public class PageScopedRefinementTests
{
    private const string Page1 = "Revenue rose to 1,265 in 2024. The board met twice.";
    private const string Page2 = "Second page text about the plant in Ulsan.";
    private const string Page3 = "Third page lists 3 risks and 12 mitigations.";

    private static RefinedContent ThreePages()
    {
        var text = Page1 + "\n\n" + Page2 + "\n\n" + Page3;
        var s2 = Page1.Length + 2;
        var s3 = s2 + Page2.Length + 2;
        return new RefinedContent
        {
            Text = text,
            Spans =
            [
                new SourceSpan(0, Page1.Length) { Page = 1 },
                new SourceSpan(s2, s2 + Page2.Length) { Page = 2 },
                new SourceSpan(s3, s3 + Page3.Length) { Page = 3 },
            ]
        };
    }

    private static RefinedContent OnePage(string text) => new()
    {
        Text = text,
        Spans = [new SourceSpan(0, text.Length) { Page = 1 }]
    };

    private static readonly LlmRefineOptions PageScope = new() { Scope = LlmRefineScope.Pages };

    private static Task<LlmRefinedContent> Refine(Func<string, string> rewrite, LlmRefineOptions? options = null,
        IReadOnlyList<PageQuality>? quality = null) =>
        PageScopedRefinement.RefineAsync(new ScriptedRefiner(rewrite), ThreePages(), quality ?? [], options ?? PageScope,
            TestContext.Current.CancellationToken);

    private static string TextOf(LlmRefinedContent result, int page)
    {
        var span = result.Spans.Single(s => s.Page == page);
        return result.Text[span.Start..span.End];
    }

    [Fact]
    public async Task AnOutputThatKeepsEveryWordAndNumber_ReplacesItsPage_AndTheSpansFollow()
    {
        // Reordering is allowed: same words, same numbers.
        var result = await Refine(t => t == Page2 ? "In Ulsan: second page text about the plant." : t);

        var page2 = result.Pages.Single(p => p.Page == 2);
        Assert.Equal(PageRefinementOutcome.Refined, page2.Outcome);
        Assert.Equal(1.0, page2.NativeCoverage);
        Assert.Equal("In Ulsan: second page text about the plant.", TextOf(result, 2));
        Assert.Equal(Page1, TextOf(result, 1));
        Assert.Equal(Page3, TextOf(result, 3));
        Assert.Equal(PageRefinementOutcome.Native, result.Pages.Single(p => p.Page == 1).Outcome);
    }

    [Fact]
    public async Task AnOutputThatDropsASentence_IsRejected_AndThePageKeepsItsText()
    {
        var result = await Refine(t => t == Page1 ? "Revenue rose to 1,265 in 2024." : t);

        var page1 = result.Pages.Single(p => p.Page == 1);
        Assert.Equal(PageRefinementOutcome.Rejected, page1.Outcome);
        Assert.Equal(PageRefinement.LowCoverage, page1.Reason);
        Assert.True(page1.NativeCoverage < 0.95);
        Assert.Equal(Page1, TextOf(result, 1));
        Assert.Contains(result.Info.Warnings, w => w.Contains("Page 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnOutputThatChangesANumber_IsRejected()
    {
        var result = await Refine(t => t == Page3 ? "Third page lists 3 risks and 13 mitigations." : t,
            new LlmRefineOptions { Scope = LlmRefineScope.Pages, MinNativeCoverage = 0.5 });

        var page3 = result.Pages.Single(p => p.Page == 3);
        Assert.Equal(PageRefinementOutcome.Rejected, page3.Outcome);
        Assert.Equal(PageRefinement.NumbersChanged, page3.Reason);
        Assert.False(page3.NumbersMatched);
        Assert.Equal(Page3, TextOf(result, 3));
    }

    [Fact]
    public async Task AnOutputThatAddsANumber_IsRejected()
    {
        var result = await Refine(t => t == Page2 ? Page2 + " Built in 1998." : t,
            new LlmRefineOptions { Scope = LlmRefineScope.Pages, MinNativeCoverage = 0.5 });

        Assert.Equal(PageRefinement.NumbersChanged, result.Pages.Single(p => p.Page == 2).Reason);
    }

    [Fact]
    public async Task OnlySelectedPagesAreSent()
    {
        var sent = new List<string>();
        var options = new LlmRefineOptions { Scope = LlmRefineScope.Pages, SelectPages = q => !q.HasTextLayer };
        var quality = new[]
        {
            new PageQuality(1) { TextOperators = 4 },
            new PageQuality(2) { ImageOperators = 1 },
            new PageQuality(3) { TextOperators = 2 },
        };

        var result = await Refine(t => { sent.Add(t); return t; }, options, quality);

        Assert.Equal([Page2], sent);
        Assert.Equal(PageRefinement.NotSelected, result.Pages.Single(p => p.Page == 1).Reason);
        Assert.Equal(PageRefinementOutcome.Skipped, result.Pages.Single(p => p.Page == 3).Outcome);
    }

    [Fact]
    public async Task APageLongerThanTheLimit_IsNotSent()
    {
        var sent = new List<string>();
        var options = new LlmRefineOptions { Scope = LlmRefineScope.Pages, MaxPageCharacters = 45 };

        var result = await Refine(t => { sent.Add(t); return t; }, options);

        Assert.DoesNotContain(Page1, sent);
        Assert.Equal(PageRefinement.TooLong, result.Pages.Single(p => p.Page == 1).Reason);
        Assert.Contains(Page2, sent);
    }

    [Fact]
    public async Task ARefinerThatThrows_KeepsEveryPage()
    {
        var result = await Refine(_ => throw new InvalidOperationException("server down"));

        Assert.All(result.Pages, p => Assert.Equal(PageRefinement.RefinerFailed, p.Reason));
        Assert.Equal(ThreePages().Text, result.Text);
        Assert.False(result.LlmWasUsed);
    }

    [Fact]
    public async Task ACancelledCall_IsNotRecordedAsAFailure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PageScopedRefinement.RefineAsync(
            new ScriptedRefiner(_ => throw new OperationCanceledException(cts.Token)), ThreePages(), [], PageScope, cts.Token));
    }

    [Fact]
    public async Task EachPageGoesToTheRefinerAsADocumentScopePass()
    {
        LlmRefineOptions? seen = null;
        var refiner = new ScriptedRefiner(t => t, o => seen = o);
        var options = new LlmRefineOptions { Scope = LlmRefineScope.Pages, Temperature = 0.05, SelectPages = _ => true };

        await PageScopedRefinement.RefineAsync(refiner, ThreePages(), [new PageQuality(1), new PageQuality(2), new PageQuality(3)],
            options, TestContext.Current.CancellationToken);

        Assert.NotNull(seen);
        Assert.Equal(LlmRefineScope.Document, seen!.Scope);
        Assert.Null(seen.SelectPages);
        Assert.Equal(0.05, seen.Temperature);
    }

    /// <summary>
    /// A justified line wrap splits words without a hyphen; joining them back is the repair page refinement is for, and
    /// drops nothing. The word view sees two words lost per join; the gate does not.
    /// </summary>
    [Fact]
    public async Task AnOutputThatOnlyJoinsWordsALineWrapSplit_ReplacesItsPage()
    {
        const string native = "단기자금시 장의 금리가 상승하였으며 이에 대응하 여 상호저 축은행과 자산운용 사의 신 탁계정이 증가 등 으로 확대되었다.";
        const string joined = "단기자금시장의 금리가 상승하였으며 이에 대응하여 상호저축은행과 자산운용사의 신탁계정이 증가 등으로 확대되었다.";
        var result = await PageScopedRefinement.RefineAsync(new ScriptedRefiner(t => t == native ? joined : t), OnePage(native), [],
            PageScope, TestContext.Current.CancellationToken);

        var page = result.Pages.Single();
        Assert.Equal(PageRefinementOutcome.Refined, page.Outcome);
        Assert.Equal(1.0, page.NativeCoverage);
        Assert.True(page.TokenCoverage < 0.95, $"the word view should see the joins as losses: {page.TokenCoverage}");
        Assert.Equal(joined, result.Text);
    }

    /// <summary>The positive control for the fact above: the same page with a clause dropped is still rejected.</summary>
    [Fact]
    public async Task AnOutputThatJoinsWordsButDropsAClause_IsRejected()
    {
        const string native = "단기자금시 장의 금리가 상승하였으며 이에 대응하 여 상호저 축은행과 자산운용 사의 신 탁계정이 증가 등 으로 확대되었다.";
        const string dropped = "단기자금시장의 금리가 상승하였으며 이에 대응하여 상호저축은행이 확대되었다.";
        var result = await PageScopedRefinement.RefineAsync(new ScriptedRefiner(t => t == native ? dropped : t), OnePage(native), [],
            PageScope, TestContext.Current.CancellationToken);

        var page = result.Pages.Single();
        Assert.Equal(PageRefinement.LowCoverage, page.Reason);
        Assert.Equal(native, result.Text);
    }

    [Theory]
    [InlineData("대응하 여", "대응하여", 1.0)]
    [InlineData("단기자금시장", "단기자금시 장", 1.0)]
    [InlineData("infor-\nmation retrieval", "information retrieval", 1.0)]
    [InlineData("the plant in Ulsan", "in Ulsan, the plant", 1.0)]
    [InlineData("a b c d", "a b", 0.5)]
    [InlineData("revenue rose", "revenue fell", 7.0 / 11)]
    [InlineData("", "anything", 1.0)]
    [InlineData("words", "", 0.0)]
    public void NativeCoverage_FindsEachWordHoweverItIsSpaced(string native, string output, double expected) =>
        Assert.Equal(expected, PageScopedRefinement.NativeCoverage(native, output), 3);

    /// <summary>A page whose text layer is decomposed Hangul (NFD) read against composed output keeps every word.</summary>
    [Fact]
    public void NativeCoverage_FoldsUnicodeNormalization() =>
        Assert.Equal(1.0, PageScopedRefinement.NativeCoverage("대응하 여".Normalize(System.Text.NormalizationForm.FormD), "대응하여"), 3);

    /// <summary>Each occurrence counts once: a repeated word needs as many occurrences in the output.</summary>
    [Fact]
    public void NativeCoverage_UsesEachOccurrenceOnce() =>
        Assert.Equal(0.5, PageScopedRefinement.NativeCoverage("plant plant", "plant"), 3);

    [Theory]
    [InlineData("a b c d", "d c b a", 1.0)]
    [InlineData("a b c d", "a b", 0.5)]
    [InlineData("Ulsan PLANT", "ulsan plant", 1.0)]
    [InlineData("", "anything", 1.0)]
    public void TokenCoverage_CountsTheNativeWordsTheOutputKept(string native, string output, double expected) =>
        Assert.Equal(expected, PageScopedRefinement.TokenCoverage(native, output), 3);

    [Theory]
    [InlineData("1,265 and 3", "3 then 1,265", true)]
    [InlineData("3 and 3", "3", false)]
    [InlineData("1,265", "1265", false)]
    [InlineData("no numbers", "still none", true)]
    public void SameNumbers_ComparesNumbersWithMultiplicity(string native, string output, bool expected) =>
        Assert.Equal(expected, PageScopedRefinement.SameNumbers(native, output));

    /// <summary>
    /// Through the processor, on a real multi-page PDF: page scope keeps the page spans, so chunks still say which
    /// page they came from after refinement — a whole-document rewrite loses them.
    /// </summary>
    [Fact]
    public async Task Processor_PageScope_KeepsPageLocationsOnChunks()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "oai_gpt-oss_model_card.pdf");
        var factory = new DocumentProcessorFactory(
            new DocumentReaderFactory(), new ChunkerFactory(), documentRefiner: null,
            llmRefiner: new ScriptedRefiner(t => t.ToUpperInvariant()), documentEnricher: null,
            loggerFactory: NullLoggerFactory.Instance);

        using var processor = factory.Create(path);
        await processor.LlmRefineAsync(new LlmRefineOptions { Scope = LlmRefineScope.Pages, SelectPages = q => q.Page == 2 },
            TestContext.Current.CancellationToken);
        await processor.ChunkAsync(cancellationToken: TestContext.Current.CancellationToken);

        var llm = processor.Result.LlmRefined!;
        Assert.Equal(PageRefinementOutcome.Refined, llm.Pages.Single(p => p.Page == 2).Outcome);
        Assert.All(llm.Pages.Where(p => p.Page != 2), p => Assert.Equal(PageRefinement.NotSelected, p.Reason));
        var page2 = llm.Spans.Single(s => s.Page == 2);
        var page2Text = llm.Text[page2.Start..page2.End];
        Assert.Equal(page2Text.ToUpperInvariant(), page2Text);
        Assert.NotEmpty(llm.Spans);
        Assert.All(processor.Result.Chunks!, c => Assert.NotNull(c.Location.StartPage));
        Assert.Contains(processor.Result.Chunks!, c => c.Location.StartPage <= 2 && c.Location.EndPage >= 2);
    }

    /// <summary>The contrast that makes the fact above mean something: a whole-document rewrite drops the page spans.</summary>
    [Fact]
    public async Task Processor_DocumentScope_LosesPageLocations()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "oai_gpt-oss_model_card.pdf");
        var factory = new DocumentProcessorFactory(
            new DocumentReaderFactory(), new ChunkerFactory(), documentRefiner: null,
            llmRefiner: new ScriptedRefiner(t => t.ToUpperInvariant()), documentEnricher: null,
            loggerFactory: NullLoggerFactory.Instance);

        using var processor = factory.Create(path);
        await processor.LlmRefineAsync(new LlmRefineOptions { Scope = LlmRefineScope.Document }, TestContext.Current.CancellationToken);
        await processor.ChunkAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(processor.Result.LlmRefined!.Spans);
        Assert.Empty(processor.Result.LlmRefined!.Pages);
        Assert.All(processor.Result.Chunks!, c => Assert.Null(c.Location.StartPage));
    }

    private sealed class ScriptedRefiner(Func<string, string> rewrite, Action<LlmRefineOptions?>? onOptions = null) : ILlmRefiner
    {
        public string RefinerType => "scripted";
        public bool IsAvailable => true;
        public string? ModelName => "scripted";

        public Task<LlmRefinedContent> RefineAsync(RefinedContent refined, LlmRefineOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            onOptions?.Invoke(options);
            var text = rewrite(refined.Text);
            return Task.FromResult(new LlmRefinedContent
            {
                RefinedId = refined.Id,
                RawId = refined.RawId,
                Text = text,
                Info = new LlmRefinementInfo { LlmWasUsed = text != refined.Text }
            });
        }
    }
}
