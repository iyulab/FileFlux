using AwesomeAssertions;
using FileFlux.Core;
using FileFlux.Infrastructure;
using Xunit;

namespace FileFlux.Tests.Services;

/// <summary>
/// A fenced code block is code, not prose to tidy: the refinement passes tell a model to join lines that end without
/// punctuation, fix look-alike characters (<c>O</c> for <c>0</c>), drop what repeats and turn <c>#</c> lines into headings,
/// and a real model followed that on code (measured: gemma-4 E4B rewrote the identifier <c>O0</c> to <c>00</c> in a code block
/// when the OCR pass was sent one). The blocks are hidden from the model and put back exactly as written.
/// </summary>
public sealed class LlmRefinerFencedCodeTests
{
    private const string Fence = "```";

    private static readonly string Code = Fence + "text\nl1 = O0 + 1l\nrn = m + rn\n" + Fence;

    private static LlmRefineOptions Passes(string only) => new()
    {
        RestoreSentences = only == "RestoreSentences",
        RemoveNoise = only == "RemoveNoise",
        CorrectOcrErrors = only == "CorrectOcrErrors",
        RestructureSections = only == "RestructureSections",
        MergeDuplicates = only == "MergeDuplicates",
    };

    private static readonly LlmRefineOptions AllPasses = new();

    [Fact]
    public async Task The_model_is_never_sent_the_code_of_a_fenced_block()
    {
        var text = "# Notes\n\nthe scan read 1O0 pages of the manual\nand the rest were fine.\n\n" + Code + "\n\nA closing paragraph about the build.";
        var service = new RecordingService(prompt => TextOf(prompt));

        await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = text }, AllPasses, TestContext.Current.CancellationToken);

        service.Prompts.Should().NotBeEmpty();
        service.Prompts.Should().OnlyContain(p => !p.Contains("O0 + 1l", StringComparison.Ordinal) && !p.Contains("rn = m", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_model_that_rewrites_whatever_it_is_sent_leaves_the_code_as_written_and_still_improves_the_prose()
    {
        var text = "the scan read 1O0 pages of the manual\nand the rest were fine.\n\n" + Code + "\n\nA closing paragraph about the build.";
        // A model that applies the OCR rule to everything it sees: O -> 0 between digits and letters.
        var service = new RecordingService(prompt => TextOf(prompt).Replace("1O0", "100", StringComparison.Ordinal).Replace("O0", "00", StringComparison.Ordinal));

        var result = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = text }, Passes("CorrectOcrErrors"), TestContext.Current.CancellationToken);

        result.Text.Should().Contain("read 100 pages", "the prose is still the model's to correct");
        result.Text.Should().Contain(Code, "the code comes back exactly as it was written");
        result.Info.Passes.Should().ContainSingle().Which.Outcome.Should().Be(LlmRefinementPassOutcome.Applied);
    }

    [Fact]
    public async Task A_code_block_that_only_looks_like_an_ocr_error_does_not_start_the_ocr_pass()
    {
        var text = "The build is described below and nothing in it is garbled.\n\n" + Code + "\n\nThat is the whole page.";
        var service = new RecordingService(prompt => TextOf(prompt));

        var result = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = text }, Passes("CorrectOcrErrors"), TestContext.Current.CancellationToken);

        service.Prompts.Should().BeEmpty("`O0` and `1l` are identifiers here, not scan errors");
        result.Info.Passes.Should().ContainSingle().Which.Outcome.Should().Be(LlmRefinementPassOutcome.NotNeeded);
    }

    [Fact]
    public async Task Lines_of_a_shell_block_are_no_broken_sentences()
    {
        var text = "Run the commands below.\n\n" + Fence + "bash\ncd build\nmake all\nrm -rf dist\n" + Fence + "\n\nThat is all.";
        var service = new RecordingService(prompt => TextOf(prompt));

        var result = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = text }, Passes("RestoreSentences"), TestContext.Current.CancellationToken);

        service.Prompts.Should().BeEmpty("the sentence pass would join `cd build` and `make all`");
        result.Info.Passes.Should().ContainSingle().Which.Outcome.Should().Be(LlmRefinementPassOutcome.NotNeeded);
    }

    [Fact]
    public async Task A_hash_comment_in_a_code_block_is_no_heading_for_the_restructure_pass()
    {
        var text = "Install it first.\n\n" + Fence + "bash\n# install the tool\nmake install\n" + Fence + "\n\nThen run it.";
        var service = new RecordingService(prompt => TextOf(prompt));

        var result = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = text }, Passes("RestructureSections"), TestContext.Current.CancellationToken);

        service.Prompts.Should().BeEmpty();
        result.Info.Passes.Should().ContainSingle().Which.Outcome.Should().Be(LlmRefinementPassOutcome.NotNeeded);
    }

    [Fact]
    public async Task Code_does_not_count_toward_the_length_a_pass_needs()
    {
        // 700 characters of text, 600 of them a code block: the noise pass needs 500 of prose and the duplicate pass 1000.
        var block = Fence + "text\n" + string.Join('\n', Enumerable.Repeat("a line of the sample output", 21)) + "\n" + Fence;
        var text = "A short intro.\n\n" + block + "\n\nA short outro.";
        text.Length.Should().BeGreaterThan(500);
        var service = new RecordingService(prompt => TextOf(prompt));

        var noise = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = text }, Passes("RemoveNoise"), TestContext.Current.CancellationToken);

        service.Prompts.Should().BeEmpty();
        noise.Info.Passes.Should().ContainSingle().Which.Outcome.Should().Be(LlmRefinementPassOutcome.NotNeeded);
    }

    [Fact]
    public async Task An_answer_that_loses_a_marker_is_not_applied_and_says_why()
    {
        var text = "the scan read 1O0 pages of the manual\nand the rest were fine.\n\n" + Code + "\n\nA closing paragraph about the build.";
        // The model drops the marker line.
        var service = new RecordingService(prompt => string.Join('\n', TextOf(prompt).Split('\n').Where(l => l.Length == 0 || !IsMarker(l))));

        var result = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = text }, Passes("CorrectOcrErrors"), TestContext.Current.CancellationToken);

        result.Text.Should().Be(text.Trim());
        var pass = result.Info.Passes.Should().ContainSingle().Subject;
        pass.Outcome.Should().Be(LlmRefinementPassOutcome.Failed);
        pass.Reason.Should().Be(LlmRefinementPass.CodeBlockLost);
        result.Info.Warnings.Should().ContainSingle().Which.Should().StartWith("CorrectOcrErrors:").And.Contain("code block marker");
    }

    [Fact]
    public async Task The_prompt_tells_the_model_what_a_marker_line_is()
    {
        var text = "the scan read 1O0 pages of the manual\nand the rest were fine.\n\n" + Code + "\n\nA closing paragraph about the build.";
        var service = new RecordingService(prompt => TextOf(prompt));

        await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = text }, Passes("CorrectOcrErrors"), TestContext.Current.CancellationToken);

        var prompt = service.Prompts.Should().ContainSingle().Subject;
        prompt.Should().Contain("such as [[code-block 0]] stands for a code block");
        prompt.Split('\n').Should().Contain("[[code-block 0]]", "the block is one marker line of plain characters, which a model keeps (private-use delimiters came back as backtick fences)");
        prompt.Should().NotContain("fenced", "the shield's own placeholder never reaches the model");
    }

    [Fact]
    public async Task A_marker_the_model_repeats_is_not_applied_so_the_code_is_not_written_twice()
    {
        var text = "the scan read 1O0 pages of the manual\nand the rest were fine.\n\n" + Code + "\n\nA closing paragraph about the build.";
        var service = new RecordingService(prompt => TextOf(prompt) + "\n[[code-block 0]]");

        var result = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = text }, Passes("CorrectOcrErrors"), TestContext.Current.CancellationToken);

        result.Text.Should().Be(text.Trim());
        result.Info.Passes.Should().ContainSingle().Which.Reason.Should().Be(LlmRefinementPass.CodeBlockLost);
    }

    [Fact]
    public async Task Markers_the_model_reorders_are_not_applied_so_the_blocks_keep_their_order()
    {
        var text = "the scan read 1O0 pages of the manual\nand the rest were fine.\n\n" + Code + "\n\nbetween\n\n" + Fence + "bash\nmake all\n" + Fence + "\n\nA closing paragraph.";
        var service = new RecordingService(prompt => TextOf(prompt).Replace("[[code-block 0]]", "[[code-block X]]", StringComparison.Ordinal)
            .Replace("[[code-block 1]]", "[[code-block 0]]", StringComparison.Ordinal).Replace("[[code-block X]]", "[[code-block 1]]", StringComparison.Ordinal));

        var result = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = text }, Passes("CorrectOcrErrors"), TestContext.Current.CancellationToken);

        result.Text.Should().Be(text.Trim());
        result.Info.Passes.Should().ContainSingle().Which.Reason.Should().Be(LlmRefinementPass.CodeBlockLost);
    }

    [Fact]
    public async Task Two_blocks_come_back_each_in_its_place()
    {
        var second = Fence + "bash\ncd build\nmake all\n" + Fence;
        var text = "the scan read 1O0 pages of the manual\nand the rest were fine.\n\n" + Code + "\n\nbetween the two\n\n" + second + "\n\nA closing paragraph.";
        var service = new RecordingService(prompt => TextOf(prompt).Replace("1O0", "100", StringComparison.Ordinal));

        var result = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = text }, Passes("CorrectOcrErrors"), TestContext.Current.CancellationToken);

        result.Text.Should().Be(text.Replace("1O0", "100", StringComparison.Ordinal).Trim());
        result.Info.Passes.Should().ContainSingle().Which.Outcome.Should().Be(LlmRefinementPassOutcome.Applied);
    }

    [Fact]
    public async Task A_text_with_no_fence_is_sent_the_prompt_it_always_was()
    {
        var service = new RecordingService(prompt => TextOf(prompt));

        await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = "the scan read 1O0 pages of the manual\nand the rest were fine." }, Passes("CorrectOcrErrors"), TestContext.Current.CancellationToken);

        service.Prompts.Should().ContainSingle().Which.Should().NotContain("code block");
    }

    [Fact]
    public async Task The_markers_of_a_text_with_many_blocks_do_not_start_the_ocr_pass()
    {
        // Marker 10 is `[[code-block 10]]`: its digits look like the `10`/`1l` runs the OCR gate looks for.
        var blocks = string.Join("\n\nA sentence between the blocks.\n\n", Enumerable.Range(0, 12).Select(i => Fence + "text\nplain " + (char)('a' + i) + "\n" + Fence));
        var text = "Nothing in this page is garbled.\n\n" + blocks + "\n\nThat is the page.";
        var service = new RecordingService(prompt => TextOf(prompt));

        var result = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = text }, Passes("CorrectOcrErrors"), TestContext.Current.CancellationToken);

        service.Prompts.Should().BeEmpty();
        result.Info.Passes.Should().ContainSingle().Which.Outcome.Should().Be(LlmRefinementPassOutcome.NotNeeded);
    }

    [Theory]
    [InlineData("`{0}`")]
    [InlineData("see {0} here")]
    [InlineData("```\n{0}\n```")]
    public async Task A_marker_the_model_sets_inside_a_line_or_wraps_is_not_applied(string shape)
    {
        var text = "the scan read 1O0 pages of the manual\nand the rest were fine.\n\n" + Code + "\n\nA closing paragraph about the build.";
        var service = new RecordingService(prompt => TextOf(prompt).Replace("[[code-block 0]]", string.Format(System.Globalization.CultureInfo.InvariantCulture, shape, "[[code-block 0]]"), StringComparison.Ordinal));

        var result = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = text }, Passes("CorrectOcrErrors"), TestContext.Current.CancellationToken);

        result.Text.Should().Be(text.Trim());
        result.Info.Passes.Should().ContainSingle().Which.Reason.Should().Be(LlmRefinementPass.CodeBlockLost);
    }

    [Fact]
    public async Task A_marker_with_spaces_around_it_on_its_own_line_is_restored()
    {
        var text = "the scan read 1O0 pages of the manual\nand the rest were fine.\n\n" + Code + "\n\nA closing paragraph about the build.";
        var service = new RecordingService(prompt => TextOf(prompt).Replace("1O0", "100", StringComparison.Ordinal).Replace("[[code-block 0]]", "  [[code-block 0]]  ", StringComparison.Ordinal));

        var result = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = text }, Passes("CorrectOcrErrors"), TestContext.Current.CancellationToken);

        result.Text.Should().Be(text.Replace("1O0", "100", StringComparison.Ordinal).Trim());
        result.Info.Passes.Should().ContainSingle().Which.Outcome.Should().Be(LlmRefinementPassOutcome.Applied);
    }

    [Fact]
    public async Task A_text_that_already_holds_a_marker_line_is_not_sent_with_its_code()
    {
        var text = "This page documents the `[[code-block 0]]` marker.\n\nthe scan read 1O0 pages\nand the rest were fine.\n\n[[code-block 0]]\n\n" + Code + "\n\nThe end.";
        var service = new RecordingService(prompt => TextOf(prompt));

        var result = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = text }, Passes("CorrectOcrErrors"), TestContext.Current.CancellationToken);

        service.Prompts.Should().BeEmpty("the model's markers could not be told from the page's own line");
        var pass = result.Info.Passes.Should().ContainSingle().Subject;
        pass.Outcome.Should().Be(LlmRefinementPassOutcome.Failed);
        pass.Reason.Should().Be(LlmRefinementPass.CodeBlockLost);
        result.Text.Should().Be(text.Trim());
    }

    [Fact]
    public async Task A_marker_line_inside_a_code_block_is_only_code()
    {
        var code = Fence + "text\n[[code-block 0]] is what a model sees\n" + Fence;
        var text = "the scan read 1O0 pages of the manual\nand the rest were fine.\n\n" + code + "\n\nA closing paragraph.";
        var service = new RecordingService(prompt => TextOf(prompt).Replace("1O0", "100", StringComparison.Ordinal));

        var result = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = text }, Passes("CorrectOcrErrors"), TestContext.Current.CancellationToken);

        result.Text.Should().Be(text.Replace("1O0", "100", StringComparison.Ordinal).Trim());
        result.Info.Passes.Should().ContainSingle().Which.Outcome.Should().Be(LlmRefinementPassOutcome.Applied);
    }

    [Fact]
    public async Task A_text_with_the_shields_private_use_delimiters_and_fenced_code_is_not_sent()
    {
        var withCode = "a PDF glyph  in the prose\nthe scan read 1O0 pages\nand the rest were fine.\n\n" + Code + "\n\nThe end.";
        var withoutCode = "a PDF glyph  in the prose\nthe scan read 1O0 pages\nand the rest were fine.\n\nThe end.";
        var service = new RecordingService(prompt => TextOf(prompt));

        var blocked = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = withCode }, Passes("CorrectOcrErrors"), TestContext.Current.CancellationToken);
        service.Prompts.Should().BeEmpty("nothing could be hidden, and the text has code");
        blocked.Info.Passes.Should().ContainSingle().Which.Reason.Should().Be(LlmRefinementPass.CodeBlockLost);

        await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = withoutCode }, Passes("CorrectOcrErrors"), TestContext.Current.CancellationToken);
        service.Prompts.Should().ContainSingle("a text with no code is sent as before");
    }

    private static bool IsMarker(string line) => line.StartsWith("[[code-block ", StringComparison.Ordinal);

    /// <summary>The text a refinement prompt carries — what a model that changes nothing answers.</summary>
    private static string TextOf(string prompt)
    {
        var start = prompt.IndexOf("Text:\n", StringComparison.Ordinal) + "Text:\n".Length;
        var end = prompt.IndexOf("\n\nReturn only", start, StringComparison.Ordinal);
        return prompt[start..end];
    }

    private sealed class RecordingService(Func<string, string> answer) : IDocumentAnalysisService
    {
        public List<string> Prompts { get; } = [];

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default) =>
            GenerateAsync(prompt, GenerationSettings.Default, cancellationToken);

        public Task<string> GenerateAsync(string prompt, GenerationSettings settings, CancellationToken cancellationToken = default)
        {
            var normalized = prompt.Replace("\r\n", "\n", StringComparison.Ordinal);
            Prompts.Add(normalized);
            return Task.FromResult(answer(normalized));
        }

        public DocumentAnalysisServiceInfo ProviderInfo => new() { Name = "recording", Type = DocumentAnalysisProviderType.Custom };
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
