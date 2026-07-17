using FileFlux.Core;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FluxCurator.Core.Core;
using FluxCurator.Infrastructure.Chunking;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// Tests for StatefulDocumentProcessor - the 4-stage pipeline implementation.
/// </summary>
public class StatefulDocumentProcessorTests
{
    private readonly IDocumentReaderFactory _readerFactory;
    private readonly IChunkerFactory _chunkerFactory;

    public StatefulDocumentProcessorTests()
    {
        _readerFactory = new DocumentReaderFactory();
        _chunkerFactory = new ChunkerFactory();
    }

    [Fact]
    public void Constructor_WithValidFilePath_InitializesCorrectly()
    {
        // Arrange
        var tempFile = CreateTempTextFile("Test content");

        try
        {
            // Act
            using var processor = CreateProcessor(tempFile);

            // Assert
            Assert.Equal(ProcessorState.Created, processor.State);
            Assert.Equal(tempFile, processor.FilePath);
            Assert.NotNull(processor.Result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Constructor_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange & Act & Assert
        Assert.Throws<FileNotFoundException>(() =>
            CreateProcessor("nonexistent.txt"));
    }

    [Fact]
    public async Task ExtractAsync_ValidTextFile_ExtractsContent()
    {
        // Arrange
        var content = "This is test content for extraction.";
        var tempFile = CreateTempTextFile(content);

        try
        {
            using var processor = CreateProcessor(tempFile);

            // Act
            await processor.ExtractAsync();

            // Assert
            Assert.Equal(ProcessorState.Extracted, processor.State);
            Assert.NotNull(processor.Result.Raw);
            Assert.Contains("test content", processor.Result.Raw.Text);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RefineAsync_AfterExtract_RefinesContent()
    {
        // Arrange
        var content = "# Heading\n\nParagraph content here.";
        var tempFile = CreateTempTextFile(content, ".md");

        try
        {
            using var processor = CreateProcessor(tempFile);
            await processor.ExtractAsync();

            // Act
            await processor.RefineAsync();

            // Assert
            Assert.Equal(ProcessorState.Refined, processor.State);
            Assert.NotNull(processor.Result.Refined);
            Assert.NotEmpty(processor.Result.Refined.Text);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RefineAsync_BeforeExtract_AutoRunsExtract()
    {
        // Arrange
        var content = "Test content";
        var tempFile = CreateTempTextFile(content);

        try
        {
            using var processor = CreateProcessor(tempFile);

            // Act - call Refine without Extract
            await processor.RefineAsync();

            // Assert - should have auto-run Extract
            Assert.Equal(ProcessorState.Refined, processor.State);
            Assert.NotNull(processor.Result.Raw);
            Assert.NotNull(processor.Result.Refined);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ChunkAsync_WithContent_CreatesChunks()
    {
        // Arrange
        var content = string.Join("\n\n", Enumerable.Range(1, 10).Select(i =>
            $"Paragraph {i}: This is some content that will be chunked. " +
            $"It needs to be long enough to create multiple chunks."));
        var tempFile = CreateTempTextFile(content);

        try
        {
            using var processor = CreateProcessor(tempFile);
            await processor.ExtractAsync();
            await processor.RefineAsync();

            // Act
            await processor.ChunkAsync(new ChunkingOptions { MaxChunkSize = 200 });

            // Assert
            Assert.Equal(ProcessorState.Chunked, processor.State);
            Assert.NotNull(processor.Result.Chunks);
            Assert.NotEmpty(processor.Result.Chunks);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ChunkAsync_BeforeRefine_AutoRunsPreviousStages()
    {
        // Arrange
        var content = "Test content for auto-run test.";
        var tempFile = CreateTempTextFile(content);

        try
        {
            using var processor = CreateProcessor(tempFile);

            // Act - call Chunk directly
            await processor.ChunkAsync();

            // Assert - should have auto-run Extract and Refine
            Assert.Equal(ProcessorState.Chunked, processor.State);
            Assert.NotNull(processor.Result.Raw);
            Assert.NotNull(processor.Result.Refined);
            Assert.NotNull(processor.Result.Chunks);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task EnrichAsync_WithChunks_EnrichesAndBuildsGraph()
    {
        // Arrange
        var content = string.Join("\n\n", Enumerable.Range(1, 5).Select(i =>
            $"Section {i}: Important content about topic {i}."));
        var tempFile = CreateTempTextFile(content);

        try
        {
            using var processor = CreateProcessor(tempFile);
            await processor.ExtractAsync();
            await processor.RefineAsync();
            await processor.ChunkAsync();

            // Act
            await processor.EnrichAsync(new EnrichOptions { BuildGraph = true });

            // Assert
            Assert.Equal(ProcessorState.Enriched, processor.State);
            Assert.NotNull(processor.Result.Graph);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ProcessAsync_FullPipeline_ExecutesAllStages()
    {
        // Arrange
        var content = "# Test Document\n\nThis is test content for full pipeline processing.";
        var tempFile = CreateTempTextFile(content, ".md");

        try
        {
            using var processor = CreateProcessor(tempFile);

            // Act
            await processor.ProcessAsync(new ProcessingOptions
            {
                IncludeEnrich = true,
                Enrich = new EnrichOptions { BuildGraph = true }
            });

            // Assert
            Assert.Equal(ProcessorState.Enriched, processor.State);
            Assert.NotNull(processor.Result.Raw);
            Assert.NotNull(processor.Result.Refined);
            Assert.NotNull(processor.Result.Chunks);
            Assert.NotNull(processor.Result.Graph);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ProcessAsync_WithoutEnrich_StopsAtChunk()
    {
        // Arrange
        var content = "Test content";
        var tempFile = CreateTempTextFile(content);

        try
        {
            using var processor = CreateProcessor(tempFile);

            // Act
            await processor.ProcessAsync(new ProcessingOptions { IncludeEnrich = false });

            // Assert
            Assert.Equal(ProcessorState.Chunked, processor.State);
            Assert.NotNull(processor.Result.Chunks);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ChunkStreamAsync_YieldsChunksProgressively()
    {
        // Arrange
        var content = string.Join("\n\n", Enumerable.Range(1, 5).Select(i =>
            $"Paragraph {i} with enough content to create chunks."));
        var tempFile = CreateTempTextFile(content);

        try
        {
            using var processor = CreateProcessor(tempFile);
            await processor.ExtractAsync();
            await processor.RefineAsync();

            // Act
            var chunks = new List<DocumentChunk>();
            await foreach (var chunk in processor.ChunkStreamAsync())
            {
                chunks.Add(chunk);
            }

            // Assert
            Assert.NotEmpty(chunks);
            Assert.Equal(ProcessorState.Chunked, processor.State);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Result_Metrics_TracksDurations()
    {
        // Arrange
        var content = "Test content for metrics tracking.";
        var tempFile = CreateTempTextFile(content);

        try
        {
            using var processor = CreateProcessor(tempFile);

            // Act
            await processor.ProcessAsync();

            // Assert
            Assert.True(processor.Result.Metrics.ExtractDuration > TimeSpan.Zero);
            Assert.True(processor.Result.Metrics.RefineDuration > TimeSpan.Zero);
            Assert.True(processor.Result.Metrics.ChunkDuration > TimeSpan.Zero);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Stage_Idempotent_DoesNotRerunCompletedStage()
    {
        // Arrange
        var content = "Test content";
        var tempFile = CreateTempTextFile(content);

        try
        {
            using var processor = CreateProcessor(tempFile);
            await processor.ExtractAsync();
            var firstRawText = processor.Result.Raw!.Text;

            // Act - call Extract again
            await processor.ExtractAsync();

            // Assert - should be same result (not re-extracted)
            Assert.Equal(firstRawText, processor.Result.Raw!.Text);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Dispose_ClearsResult()
    {
        // Arrange
        var tempFile = CreateTempTextFile("Test");

        try
        {
            var processor = CreateProcessor(tempFile);

            // Act
            processor.Dispose();

            // Assert
            Assert.Equal(ProcessorState.Disposed, processor.State);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #region Helper Methods

    [Fact]
    public async Task ChunkAsync_AutoStrategy_ResolvesByContentInsteadOfSentenceFallback()
    {
        // Arrange — regression guard: Auto used to silently degrade to the factory's Sentence
        // fallback on the ProcessAsync path. A long multi-paragraph document must resolve to
        // Paragraph via content analysis (same behavior as the IFluxCurator orchestrator).
        var content = string.Join("\n\n", Enumerable.Range(1, 6).Select(p =>
            string.Join(" ", Enumerable.Range(1, 6).Select(s =>
                $"Paragraph number {p} sentence number {s} carries distinct wording about topic {p * 10 + s}."))));
        var tempFile = CreateTempTextFile(content, ".md");

        try
        {
            using var processor = CreateProcessor(tempFile);

            // Act
            await processor.ChunkAsync(new ChunkingOptions
            {
                Strategy = ChunkingStrategies.Auto,
                MaxChunkSize = 200,
                MinChunkSize = 30
            });

            // Assert
            var chunks = processor.Result.Chunks;
            Assert.NotNull(chunks);
            Assert.NotEmpty(chunks);
            Assert.All(chunks, c => Assert.Equal("Paragraph", c.Strategy));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ChunkAsync_MarkdownWithHeadings_PopulatesHeadingPathAndSection()
    {
        // Arrange — regression guard: the ProcessAsync (stateful) path used to leave
        // Location.HeadingPath/Section empty even though the schema advertises them.
        var intro = string.Join(" ", Enumerable.Repeat("Intro paragraph text under the root heading with enough length.", 5));
        var body = string.Join(" ", Enumerable.Repeat("Body sentence living under the sub section heading with detail.", 5));
        var content = $"""
            # Root Title

            {intro}

            ## Sub Section

            {body}
            """;
        var tempFile = CreateTempTextFile(content, ".md");

        try
        {
            using var processor = CreateProcessor(tempFile);

            // Act — small MaxChunkSize forces multiple chunks so that chunks start inside sections
            await processor.ChunkAsync(new ChunkingOptions
            {
                Strategy = ChunkingStrategies.Paragraph,
                MaxChunkSize = 150,
                MinChunkSize = 30
            });

            // Assert
            var chunks = processor.Result.Chunks;
            Assert.NotNull(chunks);
            Assert.NotEmpty(chunks);

            var withHeading = chunks.Where(c => c.Location.HeadingPath.Count > 0).ToList();
            Assert.NotEmpty(withHeading);
            Assert.All(withHeading, c =>
            {
                Assert.Equal(c.Location.HeadingPath[^1], c.Location.Section);
                Assert.Equal(string.Join(" > ", c.Location.HeadingPath), c.Props[ChunkPropsKeys.HierarchyPath]);
            });

            // The deepest chunk must carry the full hierarchical path
            Assert.Contains(chunks, c =>
                c.Location.HeadingPath.Contains("Sub Section") &&
                c.Location.HeadingPath.Contains("Root Title"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private IDocumentProcessor CreateProcessor(string filePath)
    {
        var factory = new DocumentProcessorFactory(
            _readerFactory,
            _chunkerFactory,
            loggerFactory: NullLoggerFactory.Instance);

        return factory.Create(filePath);
    }

    private static string CreateTempTextFile(string content, string extension = ".txt")
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}{extension}");
        File.WriteAllText(tempFile, content);
        return tempFile;
    }

    #endregion
}
