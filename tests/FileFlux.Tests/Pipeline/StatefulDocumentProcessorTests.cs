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
