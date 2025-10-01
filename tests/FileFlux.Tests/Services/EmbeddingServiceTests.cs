using FileFlux;
using FileFlux.Infrastructure.Services;
using Xunit;

namespace FileFlux.Tests.Services;

public class EmbeddingServiceTests
{
    private readonly MockEmbeddingService _embeddingService;

    public EmbeddingServiceTests()
    {
        _embeddingService = new MockEmbeddingService(dimension: 384);
    }

    [Fact]
    public async Task GenerateEmbedding_ShouldReturnCorrectDimension()
    {
        // Arrange
        var text = "This is a test document about machine learning.";

        // Act
        var embedding = await _embeddingService.GenerateEmbeddingAsync(text);

        // Assert
        Assert.NotNull(embedding);
        Assert.Equal(384, embedding.Length);
        Assert.All(embedding, value => Assert.InRange(value, -1f, 1f));
    }

    [Fact]
    public async Task GenerateEmbedding_ShouldBeConsistent()
    {
        // Arrange
        var text = "Consistent text should produce consistent embeddings.";

        // Act
        var embedding1 = await _embeddingService.GenerateEmbeddingAsync(text);
        var embedding2 = await _embeddingService.GenerateEmbeddingAsync(text);

        // Assert
        Assert.Equal(embedding1, embedding2);
    }

    [Fact]
    public async Task GenerateBatchEmbeddings_ShouldProcessMultipleTexts()
    {
        // Arrange
        var texts = new[]
        {
            "First document about AI.",
            "Second document about databases.",
            "Third document about networking."
        };

        // Act
        var embeddings = await _embeddingService.GenerateBatchEmbeddingsAsync(texts);

        // Assert
        var embeddingArray = embeddings.ToArray();
        Assert.Equal(3, embeddingArray.Length);
        Assert.All(embeddingArray, embedding => Assert.Equal(384, embedding.Length));
    }

    [Fact]
    public void CalculateSimilarity_ShouldReturnHighScoreForSimilarTexts()
    {
        // Arrange
        var embedding1 = new float[] { 0.5f, 0.5f, 0.5f, 0.5f };
        var embedding2 = new float[] { 0.4f, 0.5f, 0.5f, 0.6f };

        // Act
        var similarity = _embeddingService.CalculateSimilarity(embedding1, embedding2);

        // Assert
        Assert.InRange(similarity, 0.9, 1.0); // High similarity expected
    }

    [Fact]
    public void CalculateSimilarity_ShouldReturnLowScoreForDifferentTexts()
    {
        // Arrange
        var embedding1 = new float[] { 1f, 0f, 0f, 0f };
        var embedding2 = new float[] { 0f, 1f, 0f, 0f };

        // Act
        var similarity = _embeddingService.CalculateSimilarity(embedding1, embedding2);

        // Assert
        Assert.InRange(similarity, -0.1, 0.1); // Low similarity expected (orthogonal vectors)
    }

    [Fact]
    public async Task EmbeddingPurpose_ShouldAffectOutput()
    {
        // Arrange
        var text = "This text will be embedded with different purposes.";

        // Act
        var analysisEmbedding = await _embeddingService.GenerateEmbeddingAsync(
            text, EmbeddingPurpose.Analysis);
        var searchEmbedding = await _embeddingService.GenerateEmbeddingAsync(
            text, EmbeddingPurpose.SemanticSearch);
        var storageEmbedding = await _embeddingService.GenerateEmbeddingAsync(
            text, EmbeddingPurpose.Storage);

        // Assert
        // Embeddings should be different based on purpose
        Assert.NotEqual(analysisEmbedding, searchEmbedding);
        Assert.NotEqual(searchEmbedding, storageEmbedding);
        Assert.NotEqual(analysisEmbedding, storageEmbedding);
    }

    [Fact]
    public async Task SemanticFeatures_ShouldDetectCodeBlocks()
    {
        // Arrange
        var codeText = "```python\ndef hello():\n    print('Hello')\n```";
        var normalText = "This is normal text without code.";

        // Act
        var codeEmbedding = await _embeddingService.GenerateEmbeddingAsync(codeText);
        var normalEmbedding = await _embeddingService.GenerateEmbeddingAsync(normalText);

        // Assert
        // Code detection feature should be different
        Assert.NotEqual(codeEmbedding[7], normalEmbedding[7]); // Index 7 is code block feature
    }

    [Fact]
    public async Task TopicFeatures_ShouldDetectTechnicalContent()
    {
        // Arrange
        var technicalText = "The API function processes data through the algorithm and returns system output.";
        var casualText = "The weather today is nice and sunny.";

        // Act
        var techEmbedding = await _embeddingService.GenerateEmbeddingAsync(technicalText);
        var casualEmbedding = await _embeddingService.GenerateEmbeddingAsync(casualText);

        // Assert
        // Technical topic feature should be higher
        Assert.True(techEmbedding[20] > casualEmbedding[20]); // Index 20 is technical topic
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData(null, 0)]
    public async Task GenerateEmbedding_ShouldHandleEmptyInput(string? text, int expectedNonZero)
    {
        // Act
        var embedding = await _embeddingService.GenerateEmbeddingAsync(text ?? string.Empty);

        // Assert
        Assert.NotNull(embedding);
        Assert.Equal(384, embedding.Length);
        Assert.Equal(expectedNonZero, embedding.Count(v => v != 0));
    }

    [Fact]
    public void Properties_ShouldReturnCorrectValues()
    {
        // Assert
        Assert.Equal(384, _embeddingService.EmbeddingDimension);
        Assert.Equal(8192, _embeddingService.MaxTokens);
        Assert.True(_embeddingService.SupportsBatchProcessing);
    }
}