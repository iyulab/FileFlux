using FileFlux.Core;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FileFlux.Infrastructure.Parsers;
using FluxCurator.Infrastructure.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// What a chunk says about its document — file metadata, document keywords, heading path — on both chunk-producing
/// paths: <see cref="FluxDocumentProcessor.ProcessAsync"/> and <see cref="IDocumentProcessor.ProcessAsync"/>. A Markdown
/// reader emits structural markers (<c>&lt;!-- HEADING_START:H1 --&gt;</c>) around each heading; none of these fields may
/// be derived from the markers.
/// </summary>
public sealed class ChunkDocumentFieldsTests : IDisposable
{
    private const string Guide =
        "# Installation Guide\n\n" +
        "This guide explains how to install the tool on a fresh machine. It covers prerequisites and the basic steps.\n\n" +
        "## Prerequisites\n\n" +
        "You need a recent runtime and a package manager. Make sure both are on your path before you start the installation.\n\n" +
        "## Steps\n\n" +
        "Run the installer and follow the prompts. When it finishes, open a new terminal and check the version.\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "fileflux-chunkfields-" + Guid.NewGuid().ToString("N"));

    public ChunkDocumentFieldsTests() => Directory.CreateDirectory(_root);

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

    private static FluxDocumentProcessor Legacy() =>
        new(new DocumentReaderFactory(), new DocumentParserFactory(), new ChunkerFactory());

    private static IDocumentProcessor Stateful(string path) =>
        new DocumentProcessorFactory(new DocumentReaderFactory(), new ChunkerFactory(), loggerFactory: NullLoggerFactory.Instance)
            .Create(path);

    [Fact]
    public async Task ProcessAsync_chunks_carry_the_document_file_metadata()
    {
        var path = await WriteAsync("guide.md", Guide);

        var chunks = await Legacy().ProcessAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk =>
        {
            Assert.Equal("guide.md", chunk.Metadata.FileName);
            Assert.Equal(new FileInfo(path).Length, chunk.Metadata.FileSize);
        });
    }

    [Fact]
    public async Task ProcessAsync_document_keywords_are_words_of_the_document_not_marker_tokens()
    {
        var path = await WriteAsync("guide.md", Guide);

        var chunks = await Legacy().ProcessAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        var keywords = Assert.IsType<List<string>>(chunks[0].Props[ChunkPropsKeys.DocumentKeywords]);
        Assert.Contains("installation", keywords);
        Assert.DoesNotContain(keywords, k => k.Contains("heading", StringComparison.OrdinalIgnoreCase)
                                             || k.Contains('<') || k.Contains('>') || k.Contains(':'));
    }

    [Fact]
    public async Task Parser_keywords_and_summary_ignore_structural_markers()
    {
        var raw = new RawContent
        {
            Text = "<!-- HEADING_START:H1 -->\n# Solar Guide\n<!-- HEADING_END:H1 -->\n\n" +
                   "Solar panels convert light. Solar power is clean.\n\n" +
                   "<!-- TABLE_START -->\n| a | b |\n|---|---|\n| 1 | 2 |\n<!-- TABLE_END -->\n",
            File = new SourceFileInfo { Name = "solar.md", Extension = ".md", Size = 100 },
        };

        var parsed = await new BasicDocumentParser().ParseAsync(raw, new DocumentParsingOptions(), TestContext.Current.CancellationToken);

        Assert.Equal("solar", parsed.Topic);
        Assert.DoesNotContain(parsed.Keywords, k => k.Contains("heading", StringComparison.OrdinalIgnoreCase)
                                                    || k.Contains("table_", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("<!--", parsed.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_heading_path_names_the_headings_not_paragraph_numbers()
    {
        var path = await WriteAsync("guide.md", Guide);

        var chunks = await Legacy().ProcessAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["Installation Guide"], chunks[0].Location.HeadingPath);
        Assert.Equal("Installation Guide", chunks[0].Props[ChunkPropsKeys.HierarchyPath]);
        Assert.DoesNotContain(chunks, c => c.Location.HeadingPath.Any(h => h.StartsWith("Paragraph ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ProcessAsync_without_headings_has_no_heading_path()
    {
        var path = await WriteAsync("plain.txt",
            "First paragraph about solar panels and how they convert light.\n\nSecond paragraph about batteries.\n");

        var chunks = await Legacy().ProcessAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c =>
        {
            Assert.Empty(c.Location.HeadingPath);
            Assert.False(c.Props.ContainsKey(ChunkPropsKeys.HierarchyPath));
        });
    }

    [Fact]
    public async Task ProcessAsync_a_chunk_that_starts_at_a_nested_heading_has_the_full_path()
    {
        var path = await WriteAsync("guide.md", Guide);
        var options = new ChunkingOptions { Strategy = ChunkingStrategies.Paragraph, MaxChunkSize = 40, MinChunkSize = 1, OverlapSize = 0 };

        var legacy = await Legacy().ProcessAsync(path, options, TestContext.Current.CancellationToken);
        await using var stateful = Stateful(path);
        await stateful.ProcessAsync(new ProcessingOptions { Chunking = options }, TestContext.Current.CancellationToken);

        foreach (var chunks in new[] { legacy, stateful.Result.Chunks!.ToArray() })
        {
            var steps = Assert.Single(chunks, c => c.Content.Contains("Run the installer", StringComparison.Ordinal));
            Assert.Equal(["Installation Guide", "Steps"], steps.Location.HeadingPath);
            Assert.Equal("Installation Guide > Steps", steps.Props[ChunkPropsKeys.HierarchyPath]);
        }
    }

    [Fact]
    public async Task Stateful_ProcessAsync_a_chunk_that_starts_at_the_first_heading_has_its_path()
    {
        var path = await WriteAsync("guide.md", Guide);

        await using var processor = Stateful(path);
        await processor.ProcessAsync(cancellationToken: TestContext.Current.CancellationToken);

        var first = processor.Result.Chunks![0];
        Assert.StartsWith("# Installation Guide", first.Content, StringComparison.Ordinal);
        Assert.Equal(["Installation Guide"], first.Location.HeadingPath);
    }

    /// <summary>
    /// Chunk offsets index the text that was chunked (<see cref="RefinedContent.Text"/>), which keeps the structural
    /// markers; chunk content has them removed. Taking the offsets' span of that text and removing the markers gives the
    /// chunk content back.
    /// </summary>
    [Fact]
    public async Task Chunk_offsets_index_the_chunked_text_and_its_span_without_markers_is_the_content()
    {
        var path = await WriteAsync("guide.md", Guide);
        var legacy = Legacy();
        var ct = TestContext.Current.CancellationToken;

        var parsed = await legacy.ParseAsync(await legacy.ExtractAsync(path, ct), null, ct);
        var chunks = await legacy.ChunkAsync(parsed, null, ct);

        Assert.All(chunks, chunk =>
        {
            var span = parsed.Text[chunk.Location.StartChar..chunk.Location.EndChar];
            Assert.Equal(chunk.Content, Infrastructure.Adapters.FluxCuratorChunkAdapter.StripInternalMarkers(span));
        });
    }
}
