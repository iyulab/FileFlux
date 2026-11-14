using FileFlux.Core;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Conversion;
using FileFlux.Infrastructure.Factories;
using FluxCurator.Infrastructure.Chunking;
using Xunit;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// A fenced code block is code: the text-level cleaning, normalizing and converting steps leave it as it is, and the
/// fence lines that make it a block stay.
/// </summary>
public sealed class FencedCodeTextStepsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fileflux-fencedsteps-" + Guid.NewGuid().ToString("N"));

    public FencedCodeTextStepsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<string> WriteAsync(string name, string text)
    {
        var path = Path.Combine(_root, name);
        await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);
        return path;
    }

    private static string FourBlocks() =>
        "# Guide\n\n" +
        "Intro text of the guide.\n\n" +
        string.Concat(Enumerable.Range(1, 4).Select(i =>
            $"## Step {i}\n\nRun this:\n\n```bash\n# step {i}\nrun {i}\nEND\n42\n```\n\nThen continue.\n\n"));

    [Fact]
    public async Task The_legacy_refining_keeps_every_closing_fence_of_a_document_with_several_code_blocks()
    {
        // A line that repeats three times and has no lowercase letter looked like a running page header: the closing
        // fence (and the marker comment after it) of a document with three code blocks were removed.
        var path = await WriteAsync("guide.md", FourBlocks());
        var processor = new FluxDocumentProcessor(new DocumentReaderFactory(), new DocumentParserFactory(), new ChunkerFactory());
        var ct = TestContext.Current.CancellationToken;
        var parsed = await processor.ParseAsync(await processor.ExtractAsync(path, ct), null, ct);

        var refined = await processor.RefineAsync(parsed, new RefiningOptions(), ct);

        var lines = refined.Text.Split('\n').Select(l => l.Trim()).ToArray();
        Assert.Equal(4, lines.Count(l => l == "```"));
        Assert.Equal(4, lines.Count(l => l.StartsWith("```bash", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task The_legacy_refining_leaves_the_lines_of_a_code_block_alone()
    {
        // «END» repeated three times and a lone number are page furniture in prose, and code in a block.
        var path = await WriteAsync("guide.md", FourBlocks());
        var processor = new FluxDocumentProcessor(new DocumentReaderFactory(), new DocumentParserFactory(), new ChunkerFactory());
        var ct = TestContext.Current.CancellationToken;
        var parsed = await processor.ParseAsync(await processor.ExtractAsync(path, ct), null, ct);

        var refined = await processor.RefineAsync(parsed, new RefiningOptions { RemovePageNumbers = true }, ct);

        var text = refined.Text.ReplaceLineEndings("\n");
        for (var i = 1; i <= 4; i++)
            Assert.Contains($"```bash\n# step {i}\nrun {i}\nEND\n42\n```", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_markdown_normalizer_does_not_read_a_code_line_as_a_heading()
    {
        const string markdown =
            "# Title\n\n" +
            "```bash\n#\n# (note)\n##### deep\n```\n\n" +
            "## After\n";

        var result = new MarkdownNormalizer().Normalize(markdown);

        // No empty heading removed, no annotation demoted, no level jump repaired - inside the block.
        Assert.Contains("```bash\n#\n# (note)\n##### deep\n```", result.Markdown, StringComparison.Ordinal);
        Assert.Equal(2, result.Stats.HeadingsFound);
    }

    [Fact]
    public async Task The_markdown_converter_adds_no_blank_line_inside_a_code_block()
    {
        const string text = "# Title\n\n```bash\n# comment\necho a\n```\n\n## After\n\nBody.\n";

        var result = await new MarkdownConverter().ConvertAsync(
            new RawContent { Text = text },
            new MarkdownConversionOptions { NormalizeWhitespace = true, PreserveHeadings = true, DetectCodeBlocks = true },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains("```bash\n# comment\necho a\n```", result.Markdown.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_markdown_converter_keeps_the_blank_lines_inside_a_code_block()
    {
        // The converter is wired into the default pipeline: its whitespace step collapsed the blank lines of a block.
        const string text = "# Title\n\n```python\nreturn None\n\n\ndef g():\n    pass\n```\n\nAfter.\n";

        var result = await new MarkdownConverter().ConvertAsync(
            new RawContent { Text = text },
            new MarkdownConversionOptions { NormalizeWhitespace = true, PreserveHeadings = true, DetectCodeBlocks = true },
            TestContext.Current.CancellationToken);

        Assert.Contains("```python\nreturn None\n\n\ndef g():\n    pass\n```", result.Markdown.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_markdown_converter_finds_a_block_by_its_fence_not_by_toggling_on_every_fence_looking_line()
    {
        // A four-backtick block that shows a fence: its inner fence lines used to flip the converter in and out of code.
        const string text = "# Title\n\n````md\n```\n# not a heading\n```\n````\n\n## Real\n\nBody.\n";

        var result = await new MarkdownConverter().ConvertAsync(
            new RawContent { Text = text },
            new MarkdownConversionOptions { NormalizeWhitespace = true, PreserveHeadings = true, DetectCodeBlocks = true },
            TestContext.Current.CancellationToken);

        Assert.Contains("````md\n```\n# not a heading\n```\n````", result.Markdown.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Equal(1, result.Statistics.CodeBlockCount);
    }

    [Fact]
    public void A_tilde_fence_hides_its_hash_lines_from_the_heading_scan_too()
    {
        // (The reader writes every fence with backticks, so this reaches the tilde reading only here.)
        var sections = SectionPathCalculator.BuildSections("# A\n\n~~~\n# not a heading\n~~~\n\n## B\n");

        Assert.Equal(["A", "B"], sections.Select(s => s.Title));
    }

    [Fact]
    public void Chunk_content_keeps_an_html_comment_that_is_part_of_a_code_block()
    {
        const string content = "<!-- CODE_START -->\n```html\n<!-- nav -->\n<p>x</p>\n```\n<!-- CODE_END -->";

        var clean = FileFlux.Infrastructure.Adapters.FluxCuratorChunkAdapter.StripInternalMarkers(content);

        Assert.Equal("```html\n<!-- nav -->\n<p>x</p>\n```", clean);
    }

    [Fact]
    public void Normalizer_actions_name_the_lines_of_the_text_that_was_passed_in()
    {
        // A three-line block comes before the heading the normalizer repairs: the action is on line 8 of the original.
        const string markdown = "# T\n\n```\na\nb\n```\n\n##### Deep\n";

        var result = new MarkdownNormalizer().Normalize(markdown);

        var action = Assert.Single(result.Actions, a => a.Type == NormalizationActionType.HeadingLevelAdjusted);
        Assert.Equal(8, action.LineNumber);
    }

    [Fact]
    public async Task The_legacy_refining_does_not_take_a_line_that_starts_with_Joo_for_a_company_header()
    {
        // «^(주)|(주식회사)...» was a regex alternation: its first branch matched every line starting with 주 (주의, 주요).
        var path = await WriteAsync("kr.txt", "주요 기능은 다음과 같다.\n\n주의: 반드시 확인한다.\n\n(주)이유랩\n\n끝.\n");
        var processor = new FluxDocumentProcessor(new DocumentReaderFactory(), new DocumentParserFactory(), new ChunkerFactory());

        var chunks = await processor.ProcessAsync(path, null, TestContext.Current.CancellationToken);

        var text = string.Join("\n", chunks.Select(c => c.Content));
        Assert.Contains("주요 기능은 다음과 같다.", text, StringComparison.Ordinal);
        Assert.Contains("주의: 반드시 확인한다.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("(주)이유랩", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_markdown_reader_drops_a_backtick_from_a_tilde_fence_info_string()
    {
        // Markdown a reader writes as a backtick fence: its info string holds no backtick, or the line is text.
        const string markdown = "# Title\n\n~~~ a`b\ncode\n~~~\n\nAfter.\n";
        var path = await WriteAsync("tilde.md", markdown);

        var raw = await new DocumentReaderFactory().GetReader(path)!.ExtractAsync(path, null, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("```a`b", raw.Text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("```ab\ncode\n```", raw.Text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_markdown_reader_fences_a_code_block_that_shows_a_fence_with_a_longer_fence()
    {
        const string markdown = "# Title\n\n````markdown\n```bash\n# comment\n```\n````\n\nAfter.\n";
        var path = await WriteAsync("nested.md", markdown);

        var raw = await new DocumentReaderFactory().GetReader(path)!.ExtractAsync(path, null, TestContext.Current.CancellationToken);

        Assert.Contains("````markdown\n```bash\n# comment\n```\n````", raw.Text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }
}
