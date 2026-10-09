using AwesomeAssertions;
using FileFlux.Core;
using FileFlux.Infrastructure;
using Xunit;

namespace FileFlux.Tests.Services;

/// <summary>
/// The refiner reports every enabled pass — not needed, kept, applied, or failed and why — so a page that came back
/// unchanged says whether the model kept it, was never asked, or failed every time it was asked. Before 0.48.0 the three
/// looked the same: «0 refined, 40 native» could mean the text was fine or that every call was cut off.
/// </summary>
public sealed class LlmRefinerPassReportTests
{
    private const string BrokenKorean = "단기자금시장의 금리가 상승하였으며 이에 대응하\n여 상호저축은행의 수신이 늘었다";

    private static readonly LlmRefineOptions SentencesAndOcr = new()
    {
        RestoreSentences = true, CorrectOcrErrors = true, RemoveNoise = false, RestructureSections = false, MergeDuplicates = false,
    };

    private static readonly LlmRefineOptions SentencesOnly = new()
    {
        RestoreSentences = true, CorrectOcrErrors = false, RemoveNoise = false, RestructureSections = false, MergeDuplicates = false,
    };

    [Fact]
    public async Task A_line_broken_mid_sentence_in_Korean_is_sent_to_the_sentence_pass()
    {
        var service = new ScriptedService(TextOf);

        var result = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = BrokenKorean }, SentencesOnly, TestContext.Current.CancellationToken);

        service.Calls.Should().Be(1, "the gate was Latin-only before 0.48.0 and never fired on Hangul");
        result.Info.Passes.Should().ContainSingle().Which.Should().Be(new LlmRefinementPass("RestoreSentences") { Outcome = LlmRefinementPassOutcome.Kept });
    }

    [Fact]
    public async Task Lines_that_end_a_sentence_are_not_sent()
    {
        var service = new ScriptedService(TextOf);

        var result = await new LlmRefiner(service).RefineAsync(
            new RefinedContent { Text = "금리가 상승하였다.\n수신이 늘었다." }, SentencesOnly, TestContext.Current.CancellationToken);

        service.Calls.Should().Be(0);
        result.Info.Passes.Should().ContainSingle().Which.Outcome.Should().Be(LlmRefinementPassOutcome.NotNeeded);
    }

    [Fact]
    public async Task Every_pass_cut_off_at_the_token_limit_is_reported_as_truncated()
    {
        var service = new ScriptedService(_ => throw new GenerationTruncatedException(64));

        var result = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = BrokenKorean + " 대 응 하" }, SentencesAndOcr, TestContext.Current.CancellationToken);

        service.Calls.Should().Be(2);
        result.Info.Passes.Should().HaveCount(2).And.OnlyContain(p => p.Outcome == LlmRefinementPassOutcome.Failed && p.Reason == LlmRefinementPass.Truncated);
        result.Info.Warnings.Should().HaveCount(2);
        result.Info.Passes!.Select(p => p.Detail).Should().Equal(result.Info.Warnings);
    }

    [Fact]
    public async Task An_empty_answer_is_a_failure_with_a_note_not_a_silent_no_change()
    {
        var service = new ScriptedService(_ => "  \n");

        var result = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = BrokenKorean }, SentencesOnly, TestContext.Current.CancellationToken);

        result.Text.Should().Be(BrokenKorean);
        var pass = result.Info.Passes.Should().ContainSingle().Subject;
        pass.Outcome.Should().Be(LlmRefinementPassOutcome.Failed);
        pass.Reason.Should().Be(LlmRefinementPass.EmptyOutput);
        result.Info.Warnings.Should().ContainSingle().Which.Should().StartWith("RestoreSentences:").And.Contain("no text");
    }

    [Fact]
    public async Task A_pass_over_the_declared_context_is_reported_as_context_too_small()
    {
        var service = new ScriptedService(TextOf, contextLength: 20);

        var result = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = BrokenKorean }, SentencesOnly, TestContext.Current.CancellationToken);

        service.Calls.Should().Be(0);
        result.Info.Passes.Should().ContainSingle().Which.Reason.Should().Be(LlmRefinementPass.ContextTooSmall);
    }

    [Fact]
    public async Task Page_scope_every_pass_failed_is_native_with_the_reason_and_the_notes()
    {
        var service = new ScriptedService(_ => throw new GenerationTruncatedException(64));

        var result = await RefinePage(service, BrokenKorean, SentencesOnly);

        var page = result.Pages.Single();
        page.Outcome.Should().Be(PageRefinementOutcome.Native);
        page.Reason.Should().Be(PageRefinement.PassesFailed);
        page.Notes.Should().ContainSingle().Which.Should().Contain("truncated");
        page.Passes.Should().ContainSingle().Which.Reason.Should().Be(LlmRefinementPass.Truncated);
        result.Info.Warnings.Should().ContainSingle().Which.Should().Be("Page 1 kept its text: passes_failed");
    }

    [Fact]
    public async Task Page_scope_no_pass_needed_is_native_without_a_model_call()
    {
        var service = new ScriptedService(TextOf);

        var result = await RefinePage(service, "금리가 상승하였다.\n수신이 늘었다.", SentencesOnly);

        service.Calls.Should().Be(0);
        result.Pages.Single().Reason.Should().Be(PageRefinement.NoPassNeeded);
        result.Info.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// A page's span carries its surrounding whitespace and the refiner trims its output; a page every pass failed on is
    /// still the page unchanged, not «refined» by the trim (measured on a real report before 0.48.1: 11 of 11 pages).
    /// </summary>
    [Fact]
    public async Task Page_scope_a_page_with_surrounding_whitespace_that_every_pass_failed_on_is_native()
    {
        var service = new ScriptedService(_ => throw new HttpRequestException("503"));
        var text = "\n" + BrokenKorean + " 대 응 하\n\n";
        var content = new RefinedContent { Text = text, Spans = [new SourceSpan(0, text.Length) { Page = 1 }] };
        var options = new LlmRefineOptions
        {
            Scope = LlmRefineScope.Pages, RestoreSentences = true, CorrectOcrErrors = true, RemoveNoise = false,
            RestructureSections = false, MergeDuplicates = false,
        };

        var result = await PageScopedRefinement.RefineAsync(new LlmRefiner(service), content, [], options, TestContext.Current.CancellationToken);

        var page = result.Pages.Single();
        page.Outcome.Should().Be(PageRefinementOutcome.Native);
        page.Reason.Should().Be(PageRefinement.PassesFailed);
        page.Passes.Should().OnlyContain(p => p.Outcome == LlmRefinementPassOutcome.Failed && p.Reason == LlmRefinementPass.Error);
        result.Text.Should().Be(text);
    }

    /// <summary>The contrast for the two facts above: a model that was asked and kept the page leaves no reason.</summary>
    [Fact]
    public async Task Page_scope_the_model_kept_the_page_is_native_without_a_reason()
    {
        var service = new ScriptedService(TextOf);

        var result = await RefinePage(service, BrokenKorean, SentencesOnly);

        service.Calls.Should().Be(1);
        var page = result.Pages.Single();
        page.Outcome.Should().Be(PageRefinementOutcome.Native);
        page.Reason.Should().BeNull();
        page.Passes.Should().ContainSingle().Which.Outcome.Should().Be(LlmRefinementPassOutcome.Kept);
    }

    private static Task<LlmRefinedContent> RefinePage(ScriptedService service, string text, LlmRefineOptions passes)
    {
        var options = new LlmRefineOptions
        {
            Scope = LlmRefineScope.Pages,
            RestoreSentences = passes.RestoreSentences, CorrectOcrErrors = passes.CorrectOcrErrors, RemoveNoise = passes.RemoveNoise,
            RestructureSections = passes.RestructureSections, MergeDuplicates = passes.MergeDuplicates,
        };
        var content = new RefinedContent { Text = text, Spans = [new SourceSpan(0, text.Length) { Page = 1 }] };
        return PageScopedRefinement.RefineAsync(new LlmRefiner(service), content, [], options, TestContext.Current.CancellationToken);
    }

    /// <summary>The text a refinement prompt carries — what a model that changes nothing answers.</summary>
    private static string TextOf(string prompt)
    {
        var start = prompt.IndexOf("Text:\n", StringComparison.Ordinal) + "Text:\n".Length;
        var end = prompt.IndexOf("\n\nReturn only", start, StringComparison.Ordinal);
        return prompt[start..end];
    }

    private sealed class ScriptedService(Func<string, string> answer, int contextLength = 0) : IDocumentAnalysisService
    {
        public int Calls { get; private set; }

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default) =>
            GenerateAsync(prompt, GenerationSettings.Default, cancellationToken);

        public Task<string> GenerateAsync(string prompt, GenerationSettings settings, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(answer(prompt.Replace("\r\n", "\n", StringComparison.Ordinal)));
        }

        public DocumentAnalysisServiceInfo ProviderInfo => new() { Name = "scripted", Type = DocumentAnalysisProviderType.Custom, MaxContextLength = contextLength };
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
