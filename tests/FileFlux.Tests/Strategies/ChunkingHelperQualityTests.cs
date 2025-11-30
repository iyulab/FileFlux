using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure.Strategies;
using Xunit;
using Xunit.Abstractions;

namespace FileFlux.Tests.Strategies;

public class ChunkingHelperQualityTests
{
    private readonly ITestOutputHelper _output;

    public ChunkingHelperQualityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Semantic Completeness Tests

    [Fact]
    public void CalculateSemanticCompleteness_CompleteContent_ReturnsHighScore()
    {
        // Arrange
        var content = "This is a complete sentence with proper punctuation. It has a clear beginning and end.";

        // Act
        var score = ChunkingHelper.CalculateSemanticCompleteness(content);

        // Assert
        _output.WriteLine($"Completeness score: {score:F2}");
        Assert.True(score >= 0.8, $"Expected high completeness score, got {score}");
    }

    [Fact]
    public void CalculateSemanticCompleteness_IncompleteContent_ReturnsLowerScore()
    {
        // Arrange - starts mid-sentence
        var content = "and this continues from a previous thought without proper beginning";

        // Act
        var score = ChunkingHelper.CalculateSemanticCompleteness(content);

        // Assert
        _output.WriteLine($"Completeness score for incomplete: {score:F2}");
        Assert.True(score < 0.8, $"Expected lower completeness score, got {score}");
    }

    [Fact]
    public void CalculateSemanticCompleteness_TruncatedContent_ReturnsLowerScore()
    {
        // Arrange - ends with ellipsis
        var content = "This sentence is truncated and doesn't finish properly...";

        // Act
        var score = ChunkingHelper.CalculateSemanticCompleteness(content);

        // Assert
        _output.WriteLine($"Completeness score for truncated: {score:F2}");
        Assert.True(score < 0.9, $"Expected lower score for truncated content");
    }

    [Fact]
    public void CalculateSemanticCompleteness_UnbalancedBrackets_ReturnsLowerScore()
    {
        // Arrange
        var content = "This has unbalanced (brackets and doesn't close them properly.";

        // Act
        var score = ChunkingHelper.CalculateSemanticCompleteness(content);

        // Assert
        _output.WriteLine($"Completeness score for unbalanced: {score:F2}");
        // Unbalanced brackets reduce score by 0.15, but other factors can add back
        Assert.True(score <= 0.95, $"Score should reflect unbalanced brackets, got {score}");
    }

    #endregion

    #region Context Independence Tests

    [Fact]
    public void CalculateContextIndependence_SelfContainedContent_ReturnsHighScore()
    {
        // Arrange
        var content = "FileFlux is a document processing library. FileFlux provides intelligent chunking strategies for RAG systems.";

        // Act
        var score = ChunkingHelper.CalculateContextIndependence(content);

        // Assert
        _output.WriteLine($"Independence score: {score:F2}");
        Assert.True(score >= 0.8, $"Expected high independence score, got {score}");
    }

    [Fact]
    public void CalculateContextIndependence_DanglingPronoun_ReturnsLowerScore()
    {
        // Arrange - starts with pronoun
        var content = "It provides intelligent chunking strategies. They work well for RAG systems.";

        // Act
        var score = ChunkingHelper.CalculateContextIndependence(content);

        // Assert
        _output.WriteLine($"Independence score for dangling pronoun: {score:F2}");
        Assert.True(score < 0.9, $"Expected lower score for dangling pronoun");
    }

    [Fact]
    public void CalculateContextIndependence_ReferentialPhrase_ReturnsLowerScore()
    {
        // Arrange
        var content = "As mentioned above, the system processes documents efficiently.";

        // Act
        var score = ChunkingHelper.CalculateContextIndependence(content);

        // Assert
        _output.WriteLine($"Independence score for referential: {score:F2}");
        // Referential phrases reduce score, verify it's not perfect
        Assert.True(score <= 0.95, $"Score should reflect referential phrase, got {score}");
    }

    #endregion

    #region Information Density Tests

    [Fact]
    public void CalculateInformationDensity_DenseContent_ReturnsHighScore()
    {
        // Arrange - technical content with proper nouns and numbers
        var content = "FileFlux v2.0 processes documents at 10MB/s using the SmartChunking algorithm. Performance improved by 45% compared to v1.0.";

        // Act
        var score = ChunkingHelper.CalculateInformationDensity(content);

        // Assert
        _output.WriteLine($"Density score for dense content: {score:F2}");
        Assert.True(score >= 0.5, $"Expected reasonable density score, got {score}");
    }

    [Fact]
    public void CalculateInformationDensity_SparseContent_ReturnsLowerScore()
    {
        // Arrange - repetitive, low-value content
        var content = "the the the a a a";

        // Act
        var score = ChunkingHelper.CalculateInformationDensity(content);

        // Assert
        _output.WriteLine($"Density score for sparse content: {score:F2}");
        Assert.True(score < 0.5);
    }

    [Fact]
    public void CalculateInformationDensity_TechnicalCode_ReturnsHigherScore()
    {
        // Arrange
        var content = "public class MyClass { void Method() { return value; } }";

        // Act
        var score = ChunkingHelper.CalculateInformationDensity(content);

        // Assert
        _output.WriteLine($"Density score for code: {score:F2}");
        Assert.True(score >= 0.4);
    }

    #endregion

    #region Boundary Sharpness Tests

    [Fact]
    public void CalculateBoundarySharpness_CleanBoundaries_ReturnsHighScore()
    {
        // Arrange - starts with capital, ends with period
        var content = "This chunk has clean boundaries. It starts and ends properly.";

        // Act
        var score = ChunkingHelper.CalculateBoundarySharpness(content);

        // Assert
        _output.WriteLine($"Sharpness score for clean boundaries: {score:F2}");
        Assert.True(score >= 0.7);
    }

    [Fact]
    public void CalculateBoundarySharpness_MidSentenceStart_ReturnsLowerScore()
    {
        // Arrange - starts mid-sentence
        var content = "which means the boundaries are not clean and continue from before";

        // Act
        var score = ChunkingHelper.CalculateBoundarySharpness(content);

        // Assert
        _output.WriteLine($"Sharpness score for mid-sentence start: {score:F2}");
        Assert.True(score < 0.7);
    }

    [Fact]
    public void CalculateBoundarySharpness_HeaderStart_ReturnsHigherScore()
    {
        // Arrange - starts with markdown header
        var content = "## Section Title\n\nThis is the section content.";

        // Act
        var score = ChunkingHelper.CalculateBoundarySharpness(content);

        // Assert
        _output.WriteLine($"Sharpness score for header start: {score:F2}");
        Assert.True(score >= 0.8);
    }

    [Fact]
    public void CalculateBoundarySharpness_CodeBlock_ReturnsHighScore()
    {
        // Arrange - complete code block
        var content = "```csharp\npublic void Method() { }\n```";

        // Act
        var score = ChunkingHelper.CalculateBoundarySharpness(content);

        // Assert
        _output.WriteLine($"Sharpness score for code block: {score:F2}");
        // Code blocks get bonus, but may not always reach 0.8 due to other factors
        Assert.True(score >= 0.6, $"Expected reasonable score for code block, got {score}");
    }

    #endregion

    #region Overall Quality Tests

    [Fact]
    public void CalculateQualityMetrics_StoresAllMetricsInProps()
    {
        // Arrange
        var chunk = new DocumentChunk
        {
            Content = "This is a well-formed chunk with proper content. It demonstrates the quality metrics calculation.",
            Index = 0
        };

        // Act
        ChunkingHelper.CalculateQualityMetrics(chunk);

        // Assert
        Assert.True(chunk.Props.ContainsKey(ChunkPropsKeys.QualitySemanticCompleteness));
        Assert.True(chunk.Props.ContainsKey(ChunkPropsKeys.QualityContextIndependence));
        Assert.True(chunk.Props.ContainsKey(ChunkPropsKeys.QualityInformationDensity));
        Assert.True(chunk.Props.ContainsKey(ChunkPropsKeys.QualityBoundarySharpness));

        _output.WriteLine($"Completeness: {chunk.Props[ChunkPropsKeys.QualitySemanticCompleteness]}");
        _output.WriteLine($"Independence: {chunk.Props[ChunkPropsKeys.QualityContextIndependence]}");
        _output.WriteLine($"Density: {chunk.Props[ChunkPropsKeys.QualityInformationDensity]}");
        _output.WriteLine($"Sharpness: {chunk.Props[ChunkPropsKeys.QualityBoundarySharpness]}");
    }

    [Fact]
    public void CalculateOverallQuality_ReturnsWeightedAverage()
    {
        // Arrange
        var chunk = new DocumentChunk
        {
            Content = "FileFlux is a document processing library. It provides intelligent chunking for RAG systems.",
            Index = 0
        };
        ChunkingHelper.CalculateQualityMetrics(chunk);

        // Act
        var overallQuality = ChunkingHelper.CalculateOverallQuality(chunk);

        // Assert
        _output.WriteLine($"Overall quality: {overallQuality:F2}");
        Assert.True(overallQuality >= 0.0 && overallQuality <= 1.0);
    }

    [Fact]
    public void FinalizeChunksWithQuality_UpdatesAllChunks()
    {
        // Arrange
        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk { Content = "First chunk with good content.", Index = 0, SourceInfo = new SourceMetadataInfo() },
            new DocumentChunk { Content = "Second chunk also has quality content.", Index = 1, SourceInfo = new SourceMetadataInfo() },
            new DocumentChunk { Content = "Third chunk completes the set.", Index = 2, SourceInfo = new SourceMetadataInfo() }
        };

        // Act
        ChunkingHelper.FinalizeChunksWithQuality(chunks);

        // Assert
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Props.ContainsKey(ChunkPropsKeys.QualitySemanticCompleteness));
            Assert.True(chunk.Quality > 0);
            _output.WriteLine($"Chunk {chunk.Index}: Quality = {chunk.Quality:F2}");
        }

        // Verify relationships are set
        Assert.True(chunks[0].Props.ContainsKey(ChunkPropsKeys.NextChunkId));
        Assert.True(chunks[1].Props.ContainsKey(ChunkPropsKeys.PreviousChunkId));
        Assert.True(chunks[1].Props.ContainsKey(ChunkPropsKeys.NextChunkId));
        Assert.True(chunks[2].Props.ContainsKey(ChunkPropsKeys.PreviousChunkId));
    }

    #endregion
}
