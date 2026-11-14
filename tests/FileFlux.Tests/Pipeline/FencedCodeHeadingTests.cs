using System.Text;
using FileFlux.Core;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FluxCurator.Infrastructure.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// A line that starts with <c>#</c> inside a fenced code block is code (a shell comment, a preprocessor directive), not a
/// heading: it must not become a section, so it never shows up in a chunk's <c>HeadingPath</c> and never hides the real
/// heading above it. Both chunk-producing paths.
/// </summary>
public sealed class FencedCodeHeadingTests : IDisposable
{
    private static readonly string[] Names = ["Alpha", "Beta", "Gamma", "Delta"];
    private static readonly string[] AfterTheFencePath = ["Doc", "After The Fence"];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "fileflux-fencedheading-" + Guid.NewGuid().ToString("N"));

    public FencedCodeHeadingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static string Sentences(string name, string part, int count)
    {
        var sb = new StringBuilder();
        for (var i = 1; i <= count; i++)
            sb.Append(FormattableString.Invariant($"This is sentence {i} of the {part} text in section {name}. "));
        return sb.ToString().TrimEnd();
    }

    /// <summary>A title and four real sections, each with text, a fenced shell snippet whose comment starts with <c>#</c>, and more text.</summary>
    private static string Document(string openFence, string closeFence)
    {
        var sb = new StringBuilder("# Probe Title\n\nIntroduction to the probe document.\n\n");
        foreach (var name in Names)
        {
            sb.Append("## Real Section ").Append(name).Append("\n\n");
            sb.Append(Sentences(name, "leading", 40)).Append("\n\n");
            sb.Append(openFence).Append("bash\n# fake-comment-").Append(name).Append(" not a heading\necho run").Append(name).Append('\n').Append(closeFence).Append("\n\n");
            sb.Append(Sentences(name, "trailing", 40)).Append("\n\n");
        }
        return sb.ToString();
    }

    private static ChunkingOptions Options() =>
        new() { Strategy = ChunkingStrategies.Paragraph, MaxChunkSize = 256, MinChunkSize = 1, OverlapSize = 32 };

    private async Task<string> WriteAsync(string text)
    {
        var path = Path.Combine(_root, "probe.md");
        await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);
        return path;
    }

    private async Task<DocumentChunk[][]> BothPathsAsync(string text)
    {
        var path = await WriteAsync(text);
        var legacy = await new FluxDocumentProcessor(new DocumentReaderFactory(), new DocumentParserFactory(), new ChunkerFactory())
            .ProcessAsync(path, Options(), TestContext.Current.CancellationToken);

        await using var stateful = new DocumentProcessorFactory(new DocumentReaderFactory(), new ChunkerFactory(), loggerFactory: NullLoggerFactory.Instance)
            .Create(path);
        await stateful.ProcessAsync(new ProcessingOptions { Chunking = Options() }, TestContext.Current.CancellationToken);

        return [legacy.ToArray(), stateful.Result.Chunks!.ToArray()];
    }

    private static void AssertOnlyRealHeadings(DocumentChunk[] chunks)
    {
        Assert.NotEmpty(chunks);
        Assert.DoesNotContain(chunks, c => c.Location.HeadingPath.Any(h => h.Contains("fake-comment", StringComparison.Ordinal)));
        Assert.All(chunks, c => Assert.All(c.Location.HeadingPath, h =>
            Assert.True(h == "Probe Title" || h.StartsWith("Real Section ", StringComparison.Ordinal), $"unexpected heading '{h}'")));

        // Every real section still appears: the fenced comment did not take its place.
        var paths = string.Join(" | ", chunks.Select(c => $"[{c.Location.StartChar}-{c.Location.EndChar} len={c.Content.Length}] " + string.Join(" > ", c.Location.HeadingPath)));
        foreach (var name in Names)
            Assert.True(
                chunks.Any(c => c.Location.HeadingPath.SequenceEqual(new[] { "Probe Title", "Real Section " + name })),
                $"no chunk has the path 'Probe Title > Real Section {name}'; chunk paths: {paths}");
    }

    [Fact]
    public async Task A_hash_line_in_a_backtick_fence_is_not_a_heading()
    {
        foreach (var chunks in await BothPathsAsync(Document("```", "```")))
            AssertOnlyRealHeadings(chunks);
    }

    [Fact]
    public async Task A_hash_line_in_a_tilde_fence_is_not_a_heading()
    {
        foreach (var chunks in await BothPathsAsync(Document("~~~", "~~~")))
            AssertOnlyRealHeadings(chunks);
    }

    [Fact]
    public async Task A_longer_fence_is_closed_only_by_an_equal_or_longer_fence_of_the_same_character()
    {
        // The inner ``` lines are content of the four-backtick fence; the heading-looking line after them is still code.
        var text = Document("````", "````").Replace("echo run", "```\n# still code\n```\necho run", StringComparison.Ordinal);

        foreach (var chunks in await BothPathsAsync(text))
        {
            AssertOnlyRealHeadings(chunks);
            Assert.DoesNotContain(chunks, c => c.Location.HeadingPath.Any(h => h.Contains("still code", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public async Task A_heading_after_a_closed_fence_is_a_heading_again()
    {
        var text =
            "# Doc\n\n" + Sentences("Doc", "intro", 40) + "\n\n" +
            "```bash\n# comment in code\n```\n\n" +
            "## After The Fence\n\n" + Sentences("Doc", "body", 40) + "\n";

        foreach (var chunks in await BothPathsAsync(text))
        {
            Assert.True(
                !chunks.Any(c => c.Location.HeadingPath.Any(h => h.Contains("comment in code", StringComparison.Ordinal))),
                "a fenced comment became a heading; chunk paths: " + Describe(chunks));
            Assert.True(
                chunks.Any(c => c.Location.HeadingPath.SequenceEqual(AfterTheFencePath)),
                "no chunk has the path 'Doc > After The Fence'; chunk paths: " + Describe(chunks));
        }
    }

    private static string Describe(DocumentChunk[] chunks) =>
        string.Join(" | ", chunks.Select(c => $"[{c.Location.StartChar}-{c.Location.EndChar}] " + string.Join(" > ", c.Location.HeadingPath)));
}
