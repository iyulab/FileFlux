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
    public async Task The_markdown_reader_fences_a_code_block_that_shows_a_fence_with_a_longer_fence()
    {
        const string markdown = "# Title\n\n````markdown\n```bash\n# comment\n```\n````\n\nAfter.\n";
        var path = await WriteAsync("nested.md", markdown);

        var raw = await new DocumentReaderFactory().GetReader(path)!.ExtractAsync(path, null, TestContext.Current.CancellationToken);

        Assert.Contains("````markdown\n```bash\n# comment\n```\n````", raw.Text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }
}
