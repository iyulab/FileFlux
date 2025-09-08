using FileFlux.Domain;
using FileFlux.Infrastructure.Services;
using FileFlux.Tests.Mocks;
using Xunit;

namespace FileFlux.Tests.Services;

public class LLMChunkFilterTests
{
    private readonly MockTextCompletionService _mockLLM;
    private readonly LLMChunkFilter _filter;

    public LLMChunkFilterTests()
    {
        _mockLLM = new MockTextCompletionService();
        _filter = new LLMChunkFilter();
    }

    [Fact]
    public async Task FilterChunksAsync_FiltersBasedOnRelevance()
    {
        // Arrange
        var chunks = new List<DocumentChunk>
        {
            CreateChunk("This is about machine learning and AI models.", 0),
            CreateChunk("The weather today is sunny and warm.", 1),
            CreateChunk("Deep learning neural networks are powerful.", 2),
            CreateChunk("I had pizza for lunch yesterday.", 3),
            CreateChunk("Natural language processing transforms text.", 4)
        };

        var options = new ChunkFilterOptions
        {
            MinRelevanceScore = 0.75, // Increased threshold to filter out low-relevance chunks
            UseSelfReflection = true,
            UseCriticValidation = false,
            QualityWeight = 0.2 // Reduce quality weight to focus on relevance
        };

        // Act
        var result = await _filter.FilterChunksAsync(
            chunks, "machine learning AI", _mockLLM, options);
        var filtered = result.ToList();

        // Assert
        Assert.NotEmpty(filtered);
        
        // Check that filtering worked - we should have fewer chunks than we started with
        Assert.True(filtered.Count < chunks.Count, 
            $"Expected fewer than {chunks.Count} chunks, but got {filtered.Count}");
        
        // ML and deep learning chunks should be present due to high relevance
        Assert.Contains(filtered, fc => fc.Chunk.Content.Contains("machine learning"));
        
        // Weather and pizza chunks should be filtered out due to low relevance
        var hasIrrelevant = filtered.Any(fc => 
            fc.Chunk.Content.Contains("weather") || fc.Chunk.Content.Contains("pizza"));
        Assert.False(hasIrrelevant, "Irrelevant chunks (weather/pizza) should be filtered out");
    }

    [Fact]
    public async Task AssessChunkAsync_PerformsThreeStageAssessment()
    {
        // Arrange
        var chunk = CreateChunk("Deep learning models require large datasets for training.", 0);
        _filter.UseCriticValidation = true;

        // Act
        var assessment = await _filter.AssessChunkAsync(
            chunk, "deep learning training", _mockLLM);

        // Assert
        Assert.True(assessment.InitialScore > 0);
        Assert.NotNull(assessment.ReflectionScore); // Should have reflection
        Assert.NotNull(assessment.CriticScore); // Should have critic
        Assert.True(assessment.FinalScore > 0);
        Assert.NotEmpty(assessment.Factors);
        Assert.NotEmpty(assessment.Reasoning);
        Assert.True(assessment.Confidence > 0);
    }

    [Fact]
    public async Task FilterChunksAsync_RespectsMaxChunksOption()
    {
        // Arrange
        var chunks = Enumerable.Range(0, 10)
            .Select(i => CreateChunk($"Content {i} about AI and ML", i))
            .ToList();

        var options = new ChunkFilterOptions
        {
            MinRelevanceScore = 0.3,
            MaxChunks = 3,
            UseSelfReflection = false,
            UseCriticValidation = false
        };

        // Act
        var result = await _filter.FilterChunksAsync(
            chunks, "AI ML", _mockLLM, options);
        var filtered = result.ToList();

        // Assert
        Assert.Equal(3, filtered.Count);
        // Should be ordered by score (best first) when MaxChunks is set
        Assert.True(filtered[0].CombinedScore >= filtered[1].CombinedScore);
    }

    [Fact]
    public async Task FilterChunksAsync_PreservesOrderWhenRequested()
    {
        // Arrange
        var chunks = new List<DocumentChunk>
        {
            CreateChunk("Highly relevant AI content", 0),
            CreateChunk("Less relevant content", 1),
            CreateChunk("Very relevant ML content", 2)
        };

        var options = new ChunkFilterOptions
        {
            MinRelevanceScore = 0.3,
            PreserveOrder = true,
            UseSelfReflection = false,
            UseCriticValidation = false
        };

        // Act
        var result = await _filter.FilterChunksAsync(
            chunks, "AI ML", _mockLLM, options);
        var filtered = result.ToList();

        // Assert
        Assert.True(filtered.Count > 0);
        // Check that chunk indices are in order
        for (int i = 1; i < filtered.Count; i++)
        {
            Assert.True(filtered[i].Chunk.ChunkIndex > filtered[i - 1].Chunk.ChunkIndex);
        }
    }

    [Fact]
    public async Task AssessChunkAsync_DetectsStructuralImportance()
    {
        // Arrange
        var headingChunk = CreateChunk("# Introduction to Machine Learning", 0);
        var codeChunk = CreateChunk("```python\ndef train_model():\n    pass\n```", 1);
        var regularChunk = CreateChunk("This is regular text content.", 2);

        // Act
        var headingAssessment = await _filter.AssessChunkAsync(
            headingChunk, null, _mockLLM);
        var codeAssessment = await _filter.AssessChunkAsync(
            codeChunk, null, _mockLLM);
        var regularAssessment = await _filter.AssessChunkAsync(
            regularChunk, null, _mockLLM);

        // Assert
        // Structural elements should have importance even without query
        var headingStructural = headingAssessment.Factors
            .FirstOrDefault(f => f.Name == "Structural Importance");
        var codeStructural = codeAssessment.Factors
            .FirstOrDefault(f => f.Name == "Structural Importance");
        var regularStructural = regularAssessment.Factors
            .FirstOrDefault(f => f.Name == "Structural Importance");

        Assert.NotNull(headingStructural);
        Assert.NotNull(codeStructural);
        Assert.NotNull(regularStructural);
        
        // Headings and code should score higher than regular text
        Assert.True(headingStructural.Contribution > regularStructural.Contribution);
        Assert.True(codeStructural.Contribution > regularStructural.Contribution);
    }

    [Fact]
    public async Task FilterChunksAsync_AppliesCustomCriteria()
    {
        // Arrange
        var chunks = new List<DocumentChunk>
        {
            CreateChunk("Scientific paper with [1] citation.", 0),
            CreateChunk("Casual text without citations.", 1),
            CreateChunk("Research shows [2] that AI improves.", 2)
        };

        var options = new ChunkFilterOptions
        {
            MinRelevanceScore = 0.3,
            Criteria = new List<FilterCriterion>
            {
                new FilterCriterion
                {
                    Type = CriterionType.FactualContent,
                    Weight = 1.0,
                    IsMandatory = false
                }
            },
            UseSelfReflection = false,
            UseCriticValidation = false
        };

        // Act
        var result = await _filter.FilterChunksAsync(
            chunks, null, _mockLLM, options);
        var filtered = result.ToList();

        // Assert
        Assert.NotEmpty(filtered);
        // Verify that factual content criterion was applied
        var citationChunks = filtered.Where(fc => 
            fc.Chunk.Content.Contains("[") && fc.Chunk.Content.Contains("]")).ToList();
        var noCitationChunks = filtered.Where(fc => 
            !fc.Chunk.Content.Contains("[") || !fc.Chunk.Content.Contains("]")).ToList();
        
        // At least verify that the assessment included the factual content factor
        if (citationChunks.Any())
        {
            var firstCitation = citationChunks.First();
            Assert.NotNull(firstCitation.Assessment);
            Assert.Contains(firstCitation.Assessment.Factors, 
                f => f.Name == CriterionType.FactualContent.ToString());
        }
    }

    [Fact]
    public async Task AssessChunkAsync_GeneratesSuggestions()
    {
        // Arrange
        var shortChunk = CreateChunk("Too short.", 0);
        var goodChunk = CreateChunk(
            "This is a well-formed chunk with sufficient content about machine learning " +
            "and artificial intelligence. It contains multiple sentences and covers the topic " +
            "comprehensively with good information density.", 1);

        // Act
        var shortAssessment = await _filter.AssessChunkAsync(
            shortChunk, "machine learning", _mockLLM);
        var goodAssessment = await _filter.AssessChunkAsync(
            goodChunk, "machine learning", _mockLLM);

        // Assert
        Assert.NotEmpty(shortAssessment.Suggestions);
        // Short chunk should have suggestions about length or merging
        Assert.Contains(shortAssessment.Suggestions, 
            s => s.Contains("boundary") || s.Contains("merge") || s.Contains("context"));
        
        // Good chunk might have fewer or no suggestions
        Assert.True(goodAssessment.Suggestions.Count <= shortAssessment.Suggestions.Count);
    }

    [Fact]
    public async Task FilterChunksAsync_HandlesQualityWeighting()
    {
        // Arrange
        var chunks = new List<DocumentChunk>
        {
            CreateChunk("High quality: The empirical evidence [1] demonstrates that neural networks achieve 95% accuracy.", 0),
            CreateChunk("Low quality: stuff about AI", 1),
            CreateChunk("Medium quality: Machine learning is a subset of artificial intelligence.", 2)
        };

        var highQualityOptions = new ChunkFilterOptions
        {
            MinRelevanceScore = 0.3,
            QualityWeight = 0.8, // Prioritize quality
            UseSelfReflection = false,
            UseCriticValidation = false
        };

        var lowQualityOptions = new ChunkFilterOptions
        {
            MinRelevanceScore = 0.3,
            QualityWeight = 0.2, // Prioritize relevance
            UseSelfReflection = false,
            UseCriticValidation = false
        };

        // Act
        var highQualityResult = await _filter.FilterChunksAsync(
            chunks, "AI", _mockLLM, highQualityOptions);
        var lowQualityResult = await _filter.FilterChunksAsync(
            chunks, "AI", _mockLLM, lowQualityOptions);

        // Assert
        var highQualityFiltered = highQualityResult.ToList();
        var lowQualityFiltered = lowQualityResult.ToList();

        Assert.NotEmpty(highQualityFiltered);
        Assert.NotEmpty(lowQualityFiltered);

        // With high quality weight, the detailed chunk should rank higher
        if (highQualityFiltered.Any(fc => fc.Chunk.ChunkIndex == 0))
        {
            var detailedChunk = highQualityFiltered.First(fc => fc.Chunk.ChunkIndex == 0);
            Assert.True(detailedChunk.CombinedScore > 0.5);
        }
    }

    [Fact]
    public void RelevanceThreshold_ClampsBetweenZeroAndOne()
    {
        // Arrange & Act
        _filter.RelevanceThreshold = 1.5;
        var max = _filter.RelevanceThreshold;

        _filter.RelevanceThreshold = -0.5;
        var min = _filter.RelevanceThreshold;

        _filter.RelevanceThreshold = 0.7;
        var normal = _filter.RelevanceThreshold;

        // Assert
        Assert.Equal(1.0, max);
        Assert.Equal(0.0, min);
        Assert.Equal(0.7, normal);
    }

    private DocumentChunk CreateChunk(string content, int index)
    {
        return new DocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            Content = content,
            ChunkIndex = index,
            StartPosition = index * 100,
            EndPosition = (index + 1) * 100,
            Metadata = new DocumentMetadata
            {
                FileName = "test.txt",
                FileType = "Text",
                ProcessedAt = DateTime.UtcNow.AddDays(-index)
            }
        };
    }
}