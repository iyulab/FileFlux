using FileFlux.Core;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FluxCurator.Core.Core;
using FluxCurator.Infrastructure.Chunking;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// Mirrors the Filer consumer tripwire (FileFluxMarkerStripTests) at the source level:
/// the native IDocumentProcessor.ProcessAsync -> Result.Chunks[].Content path must NOT
/// leak internal structural markers (&lt;!-- HEADING_* --&gt; / TABLE_* / LIST_* / CODE_*).
/// This is the native-path regression guard requested in issue
/// ISSUE-FileFlux-20260601-094128-structural-marker-content-leak (line 124).
/// </summary>
public class NativeProcessAsyncMarkerStripTests
{
    private readonly IDocumentReaderFactory _readerFactory = new DocumentReaderFactory();
    private readonly IChunkerFactory _chunkerFactory = new ChunkerFactory();

    private const string MarkdownWithAllStructures =
        "# Heading One\n\n" +
        "Intro paragraph under the heading.\n\n" +
        "## Heading Two\n\n" +
        "| Col A | Col B |\n" +
        "| --- | --- |\n" +
        "| a1 | b1 |\n" +
        "| a2 | b2 |\n\n" +
        "- list item one\n" +
        "- list item two\n" +
        "- list item three\n\n" +
        "```csharp\nvar x = 1;\nConsole.WriteLine(x);\n```\n\n" +
        "Closing paragraph.\n";

    private IDocumentProcessor CreateProcessor(string filePath) =>
        new DocumentProcessorFactory(
            _readerFactory,
            _chunkerFactory,
            loggerFactory: NullLoggerFactory.Instance).Create(filePath);

    [Fact]
    public async Task NativeProcessAsync_DefaultAuto_DoesNotLeakStructuralMarkers()
    {
        // Arrange — mirror Filer: factory.Create(".md") -> ProcessAsync default (Auto) -> Result.Chunks
        var tempFile = Path.Combine(Path.GetTempPath(), $"fileflux-marker-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(tempFile, MarkdownWithAllStructures);

        try
        {
            using var processor = CreateProcessor(tempFile);

            // Act
            await processor.ProcessAsync();

            // Assert — no chunk content may contain any internal HTML-comment marker
            Assert.NotNull(processor.Result.Chunks);
            Assert.NotEmpty(processor.Result.Chunks!);

            foreach (var chunk in processor.Result.Chunks!)
            {
                Assert.DoesNotContain("<!--", chunk.Content, StringComparison.Ordinal);
                Assert.DoesNotContain("HEADING_START", chunk.Content, StringComparison.Ordinal);
                Assert.DoesNotContain("TABLE_START", chunk.Content, StringComparison.Ordinal);
                Assert.DoesNotContain("LIST_START", chunk.Content, StringComparison.Ordinal);
                Assert.DoesNotContain("CODE_START", chunk.Content, StringComparison.Ordinal);
            }

            // AC#2 (no info loss): the heading level lifted before stripping must survive as metadata.
            // Guards against a future refactor dropping the lift while marker-absence still passes.
            // (Robust to chunk count: assert the H1 level was captured somewhere, levels stay 1-6.)
            var headingLevels = processor.Result.Chunks!
                .Where(c => c.Props.ContainsKey(ChunkPropsKeys.HierarchyHeadingLevel))
                .Select(c => Assert.IsType<int>(c.Props[ChunkPropsKeys.HierarchyHeadingLevel]))
                .ToList();
            Assert.NotEmpty(headingLevels);
            Assert.All(headingLevels, level => Assert.InRange(level, 1, 6));
            Assert.Contains(1, headingLevels);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private const string MarkdownWithReferenceDefinition =
        "# TCP vs UDP\n\n" +
        "| Protocol | Use |\n" +
        "| --- | --- |\n" +
        "| TCP | Streaming, DNS, games |\n\n" +
        "See the [spec][1] for details.\n\n" +
        "[1]: https://example.com/spec\n";

    [Fact]
    public async Task NativeProcessAsync_LinkReferenceDefinition_DoesNotLeakBracketColon()
    {
        // Arrange — mirror Filer's exact path: factory.Create(".md") -> ProcessAsync -> Result.Chunks.
        // A labeled link reference definition must not surface as `[]:` / `[label]: url` in chunk content,
        // while the referencing link's display text survives (no content loss). Regression for the
        // reference-definition-group leak (Filer upstream issue 20260607).
        var tempFile = Path.Combine(Path.GetTempPath(), $"fileflux-refdef-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(tempFile, MarkdownWithReferenceDefinition);

        try
        {
            using var processor = CreateProcessor(tempFile);

            // Act
            await processor.ProcessAsync();

            // Assert
            Assert.NotNull(processor.Result.Chunks);
            Assert.NotEmpty(processor.Result.Chunks!);

            foreach (var chunk in processor.Result.Chunks!)
            {
                Assert.DoesNotContain("[]:", chunk.Content, StringComparison.Ordinal);
                Assert.DoesNotContain("[1]: https://example.com", chunk.Content, StringComparison.Ordinal);
            }

            // No content loss: the table cell and the link display text must remain in chunk content.
            var allContent = string.Concat(processor.Result.Chunks!.Select(c => c.Content));
            Assert.Contains("Streaming, DNS, games", allContent, StringComparison.Ordinal);
            Assert.Contains("spec", allContent, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private const string MarkdownWithoutReferenceDefinitions =
        "# TCP vs UDP\n\n" +
        "| Feature | TCP | UDP |\n" +
        "| --- | --- | --- |\n" +
        "| Use cases | Web, email, files | Streaming, DNS, games |\n";

    [Fact]
    public async Task NativeProcessAsync_DocWithoutReferenceDefinitions_DoesNotLeakBracketColon()
    {
        // Arrange — the exact Filer shape: a document with ZERO reference definitions still gets
        // an empty LinkReferenceDefinitionGroup appended by Markdig, which used to render a bare
        // []: onto the tail. Verify the native chunk path Filer indexes stays clean.
        var tempFile = Path.Combine(Path.GetTempPath(), $"fileflux-norefdef-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(tempFile, MarkdownWithoutReferenceDefinitions);

        try
        {
            using var processor = CreateProcessor(tempFile);

            // Act
            await processor.ProcessAsync();

            // Assert
            Assert.NotNull(processor.Result.Chunks);
            Assert.NotEmpty(processor.Result.Chunks!);

            foreach (var chunk in processor.Result.Chunks!)
            {
                Assert.DoesNotContain("[]:", chunk.Content, StringComparison.Ordinal);
            }

            var allContent = string.Concat(processor.Result.Chunks!.Select(c => c.Content));
            Assert.Contains("Streaming, DNS, games", allContent, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private const string MarkdownWithImagesAndEmptyLink =
        "# Visual Guide\n\n" +
        "![Architecture diagram](images/arch.png)\n\n" +
        "Intro paragraph.\n\n" +
        "See [](https://example.com) for the empty-text link case.\n\n" +
        "![](images/unnamed.png)\n";

    [Fact]
    public async Task NativeProcessAsync_ImagesAndEmptyLink_PreserveMarkerAndDropEmptyBrackets()
    {
        // Arrange — mirror Filer's exact path: factory.Create(".md") -> ProcessAsync -> Result.Chunks.
        // Images must keep their `!` marker (not masquerade as links) and empty-text links must not
        // leave bare `[]` bracket noise in indexed chunk content. Native-path guard for the
        // LinkInline image/empty-link fix (issue 20260607-linkinline-image-marker-lost); the
        // refdef leak showed reader-unit coverage alone can miss the path Filer actually indexes.
        var tempFile = Path.Combine(Path.GetTempPath(), $"fileflux-image-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(tempFile, MarkdownWithImagesAndEmptyLink);

        try
        {
            using var processor = CreateProcessor(tempFile);

            // Act
            await processor.ProcessAsync();

            // Assert
            Assert.NotNull(processor.Result.Chunks);
            Assert.NotEmpty(processor.Result.Chunks!);

            var allContent = string.Concat(processor.Result.Chunks!.Select(c => c.Content));

            // Image `!` markers + paths survive (images stay images, not demoted to links).
            Assert.Contains("![Architecture diagram](images/arch.png)", allContent, StringComparison.Ordinal);
            Assert.Contains("![](images/unnamed.png)", allContent, StringComparison.Ordinal);
            // Empty-text link emits the url only — the `[](url)` bracket form must not survive.
            // (Note: an empty-alt image legitimately keeps `![]`, so we check the link form
            // specifically rather than a blanket `[]` absence.)
            Assert.Contains("https://example.com", allContent, StringComparison.Ordinal);
            Assert.DoesNotContain("[](https://example.com)", allContent, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ChunkStreamAsync_DefaultAuto_DoesNotLeakStructuralMarkers()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"fileflux-marker-stream-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(tempFile, MarkdownWithAllStructures);

        try
        {
            using var processor = CreateProcessor(tempFile);

            // Act — streaming chunk path must strip markers identically to the batch path
            var streamed = new List<DocumentChunk>();
            await foreach (var chunk in processor.ChunkStreamAsync())
            {
                streamed.Add(chunk);
            }

            // Assert
            Assert.NotEmpty(streamed);
            foreach (var chunk in streamed)
            {
                Assert.DoesNotContain("<!--", chunk.Content, StringComparison.Ordinal);
                Assert.DoesNotContain("HEADING_START", chunk.Content, StringComparison.Ordinal);
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
