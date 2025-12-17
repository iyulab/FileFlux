using FileFlux.Core;
using FileFlux.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// Tests for DocumentRefiner - Stage 2 of the pipeline.
/// </summary>
public class DocumentRefinerTests
{
    private readonly DocumentRefiner _refiner;

    public DocumentRefinerTests()
    {
        _refiner = new DocumentRefiner(
            markdownConverter: null,
            logger: NullLogger<DocumentRefiner>.Instance);
    }

    [Fact]
    public void Properties_ReturnsCorrectValues()
    {
        Assert.Equal("DocumentRefiner", _refiner.RefinerType);
        Assert.False(_refiner.SupportsLlm);
    }

    [Fact]
    public async Task RefineAsync_BasicText_RefinesSuccessfully()
    {
        // Arrange
        var raw = CreateRawContent("This is basic test content.");

        // Act
        var result = await _refiner.RefineAsync(raw);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Text);
        Assert.Equal(raw.Id, result.RawId);
    }

    [Fact]
    public async Task RefineAsync_WithMarkdownHeadings_ExtractsSections()
    {
        // Arrange
        var content = "# Heading 1\n\nContent under heading 1.\n\n## Heading 2\n\nContent under heading 2.";
        var raw = CreateRawContent(content);

        // Act
        var result = await _refiner.RefineAsync(raw, new RefineOptions { BuildSections = true });

        // Assert
        Assert.NotEmpty(result.Sections);
    }

    [Fact]
    public async Task RefineAsync_WithCodeBlock_ExtractsStructuredElement()
    {
        // Arrange
        var content = "Some text before.\n\n```csharp\npublic class Test { }\n```\n\nSome text after.";
        var raw = CreateRawContent(content);

        // Act
        var result = await _refiner.RefineAsync(raw, new RefineOptions { ExtractStructures = true });

        // Assert
        Assert.NotEmpty(result.Structures);
        var codeStructure = result.Structures.FirstOrDefault(s => s.Type == StructureType.Code);
        Assert.NotNull(codeStructure);
    }

    [Fact]
    public async Task RefineAsync_WithMarkdownTable_ExtractsStructuredElement()
    {
        // Arrange
        var content = "| Header 1 | Header 2 |\n|----------|----------|\n| Cell 1   | Cell 2   |\n| Cell 3   | Cell 4   |";
        var raw = CreateRawContent(content);

        // Act
        var result = await _refiner.RefineAsync(raw, new RefineOptions { ExtractStructures = true });

        // Assert
        var tableStructure = result.Structures.FirstOrDefault(s => s.Type == StructureType.Table);
        Assert.NotNull(tableStructure);
    }

    [Fact]
    public async Task RefineAsync_WithNoiseContent_CleansNoise()
    {
        // Arrange
        var content = "Content with\n\n\n\nmultiple blank lines\n\n\n\nand extra   spaces.";
        var raw = CreateRawContent(content);

        // Act
        var result = await _refiner.RefineAsync(raw, new RefineOptions { CleanNoise = true });

        // Assert
        Assert.DoesNotContain("\n\n\n", result.Text);
    }

    [Fact]
    public async Task RefineAsync_Metadata_PreservesFileInfo()
    {
        // Arrange
        var raw = CreateRawContent("Test content", fileName: "test-document.md");

        // Act
        var result = await _refiner.RefineAsync(raw);

        // Assert
        Assert.NotNull(result.Metadata);
        Assert.Equal("test-document.md", result.Metadata.FileName);
    }

    [Fact]
    public async Task RefineAsync_Quality_CalculatesScores()
    {
        // Arrange
        var raw = CreateRawContent("This is test content with enough text to calculate quality scores.");

        // Act
        var result = await _refiner.RefineAsync(raw);

        // Assert
        Assert.NotNull(result.Quality);
        Assert.True(result.Quality.RetentionScore > 0);
        Assert.True(result.Quality.ConfidenceScore > 0);
    }

    [Fact]
    public async Task RefineAsync_Info_RecordsMetadata()
    {
        // Arrange
        var raw = CreateRawContent("Test content");

        // Act
        var result = await _refiner.RefineAsync(raw);

        // Assert
        Assert.NotNull(result.Info);
        Assert.Equal("DocumentRefiner", result.Info.RefinerType);
    }

    [Fact]
    public async Task RefineAsync_EmptyContent_HandlesGracefully()
    {
        // Arrange
        var raw = CreateRawContent("");

        // Act
        var result = await _refiner.RefineAsync(raw);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Text);
    }

    [Fact]
    public async Task RefineAsync_WithCancelledToken_HandlesGracefully()
    {
        // Arrange
        var raw = CreateRawContent("Test content");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act - Without LLM services, processing is fast and may complete before cancellation check
        try
        {
            var result = await _refiner.RefineAsync(raw, cancellationToken: cts.Token);
            // If completed, verify result is valid
            Assert.NotNull(result);
        }
        catch (OperationCanceledException)
        {
            // Expected behavior if cancellation is checked early
        }
    }

    #region Helper Methods

    private static RawContent CreateRawContent(string text, string fileName = "test.txt")
    {
        return new RawContent
        {
            Id = Guid.NewGuid(),
            Text = text,
            File = new SourceFileInfo
            {
                Name = fileName,
                Extension = Path.GetExtension(fileName),
                Size = text.Length,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            }
        };
    }

    #endregion
}
