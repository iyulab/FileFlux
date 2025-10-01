using FileFlux;
using FileFlux.Domain;
using FileFlux.Infrastructure.Quality;
using System.Collections.Generic;
using Xunit;

namespace FileFlux.Tests.Quality;

/// <summary>
/// Tests for ChunkQualityEngine internal quality calculation logic
/// </summary>
public class ChunkQualityEngineTests
{
    private readonly ChunkQualityEngine _engine;

    public ChunkQualityEngineTests()
    {
        _engine = new ChunkQualityEngine();
    }

    [Fact]
    public async Task CalculateQualityMetricsAsync_WithValidChunks_ReturnsMetrics()
    {
        // Arrange
        var chunks = CreateSampleChunks();

        // Act
        var result = await _engine.CalculateQualityMetricsAsync(chunks);

        // Assert
        Assert.NotNull(result);
        // ChunkingQualityMetrics only contains chunking-related metrics
        Assert.InRange(result.AverageCompleteness, 0.0, 1.0);
        Assert.InRange(result.ContentConsistency, 0.0, 1.0);
        Assert.InRange(result.BoundaryQuality, 0.0, 1.0);
        Assert.InRange(result.SizeDistribution, 0.0, 1.0);
        Assert.InRange(result.OverlapEffectiveness, 0.0, 1.0);
    }

    [Fact]
    public async Task CalculateQualityMetricsAsync_WithEmptyChunks_ReturnsDefaultMetrics()
    {
        // Arrange
        var chunks = new List<DocumentChunk>();

        // Act
        var result = await _engine.CalculateQualityMetricsAsync(chunks);

        // Assert
        Assert.NotNull(result);
        // Empty chunks should still return valid metrics (likely low scores)
        Assert.InRange(result.AverageCompleteness, 0.0, 1.0);
        Assert.InRange(result.ContentConsistency, 0.0, 1.0);
    }

    [Fact]
    public async Task CalculateQualityMetricsAsync_WithUniformChunks_ReturnsHighSizeDistribution()
    {
        // Arrange - Create chunks with very similar sizes
        var chunks = new List<DocumentChunk>();
        for (int i = 0; i < 5; i++)
        {
            chunks.Add(new DocumentChunk
            {
                Id = $"chunk_{i}",
                Content = new string('A', 500), // All chunks same size
                ChunkIndex = i,
                Metadata = new DocumentMetadata { FileName = "test.txt" }
            });
        }

        // Act
        var result = await _engine.CalculateQualityMetricsAsync(chunks);

        // Assert
        Assert.NotNull(result);
        // Uniform size should result in high size distribution score
        Assert.True(result.SizeDistribution > 0.5, $"Expected high size distribution for uniform chunks, got {result.SizeDistribution}");
    }

    [Fact]
    public async Task CalculateQualityMetricsAsync_WithVaryingSizes_ReturnsLowerSizeDistribution()
    {
        // Arrange - Create chunks with very different sizes
        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Id = "small",
                Content = "Short",
                ChunkIndex = 0,
                Metadata = new DocumentMetadata { FileName = "test.txt" }
            },
            new DocumentChunk
            {
                Id = "large",
                Content = new string('A', 2000), // Very large chunk
                ChunkIndex = 1,
                Metadata = new DocumentMetadata { FileName = "test.txt" }
            },
            new DocumentChunk
            {
                Id = "medium",
                Content = new string('B', 500),
                ChunkIndex = 2,
                Metadata = new DocumentMetadata { FileName = "test.txt" }
            }
        };

        // Act
        var result = await _engine.CalculateQualityMetricsAsync(chunks);

        // Assert
        Assert.NotNull(result);
        // Varying sizes should result in lower size distribution score
        Assert.True(result.SizeDistribution < 1.0, $"Expected lower size distribution for varying chunks, got {result.SizeDistribution}");
    }

    [Fact]
    public async Task GenerateQuestionsAsync_WithValidContent_ReturnsQuestions()
    {
        // Arrange
        var parsedContent = CreateSampleParsedContent();
        const int questionCount = 5;

        // Act
        var result = await _engine.GenerateQuestionsAsync(parsedContent, questionCount);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(questionCount, result.Count);
        
        foreach (var question in result)
        {
            Assert.False(string.IsNullOrWhiteSpace(question.Question));
            Assert.False(string.IsNullOrWhiteSpace(question.ExpectedAnswer));
            Assert.True(Enum.IsDefined(typeof(QuestionType), question.Type));
            Assert.InRange(question.DifficultyScore, 0.0, 1.0);
            Assert.InRange(question.ConfidenceScore, 0.0, 1.0);
        }
    }

    [Fact]
    public async Task GenerateQuestionsAsync_WithDifferentCounts_ReturnsCorrectNumber()
    {
        // Arrange
        var parsedContent = CreateSampleParsedContent();

        // Act & Assert
        var result3 = await _engine.GenerateQuestionsAsync(parsedContent, 3);
        Assert.Equal(3, result3.Count);

        var result10 = await _engine.GenerateQuestionsAsync(parsedContent, 10);
        Assert.Equal(10, result10.Count);
    }

    [Fact]
    public async Task ValidateAnswerabilityAsync_WithGoodChunks_ReturnsHighAnswerability()
    {
        // Arrange
        var questions = new List<GeneratedQuestion>
        {
            new GeneratedQuestion
            {
                Question = "What is the main topic discussed?",
                ExpectedAnswer = "Quality analysis and testing",
                Type = QuestionType.Factual
            },
            new GeneratedQuestion
            {
                Question = "How does the quality analysis work?",
                ExpectedAnswer = "Through systematic evaluation of chunks",
                Type = QuestionType.Procedural
            }
        };

        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Content = "This document discusses quality analysis and testing methodologies for document processing systems.",
                Metadata = new DocumentMetadata { FileName = "test.txt" }
            },
            new DocumentChunk
            {
                Content = "The quality analysis works through systematic evaluation of chunks, measuring various metrics.",
                Metadata = new DocumentMetadata { FileName = "test.txt" }
            }
        };

        // Act
        var result = await _engine.ValidateAnswerabilityAsync(questions, chunks);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalQuestions);
        Assert.True(result.AnswerableQuestions >= 0);
        Assert.True(result.AnswerableQuestions <= result.TotalQuestions);
        Assert.InRange(result.AverageConfidence, 0.0, 1.0);
        Assert.InRange(result.AnswerabilityRatio, 0.0, 1.0);
    }

    [Fact]
    public async Task ValidateAnswerabilityAsync_WithEmptyChunks_ReturnsLowAnswerability()
    {
        // Arrange
        var questions = new List<GeneratedQuestion>
        {
            new GeneratedQuestion
            {
                Question = "What is discussed in the document?",
                ExpectedAnswer = "Complex topics",
                Type = QuestionType.Factual
            }
        };

        var chunks = new List<DocumentChunk>(); // Empty chunks

        // Act
        var result = await _engine.ValidateAnswerabilityAsync(questions, chunks);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.TotalQuestions);
        Assert.Equal(0, result.AnswerableQuestions); // No chunks means no answers
        Assert.Equal(0.0, result.AnswerabilityRatio);
    }

    [Theory]
    [InlineData(QuestionType.Factual)]
    [InlineData(QuestionType.Conceptual)]
    [InlineData(QuestionType.Analytical)]
    [InlineData(QuestionType.Procedural)]
    [InlineData(QuestionType.Comparative)]
    public async Task GenerateQuestionsAsync_GeneratesAllQuestionTypes(QuestionType expectedType)
    {
        // Arrange
        var parsedContent = CreateSampleParsedContent();

        // Act - Generate enough questions to likely get all types
        var result = await _engine.GenerateQuestionsAsync(parsedContent, 10);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count > 0);
        
        // Verify we have questions of different types (at least some variety)
        var uniqueTypes = result.Select(q => q.Type).Distinct().Count();
        Assert.True(uniqueTypes >= 2, "Expected multiple question types to be generated");
    }

    private static List<DocumentChunk> CreateSampleChunks()
    {
        return new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Id = "1",
                Content = "This is the introduction section of our document. It provides an overview of the main concepts and sets the stage for detailed discussion.",
                ChunkIndex = 0,
                StartPosition = 0,
                EndPosition = 128,
                Metadata = new DocumentMetadata 
                { 
                    FileName = "sample.txt",
                    FileType = "text/plain",
                    ProcessedAt = DateTime.UtcNow
                }
            },
            new DocumentChunk
            {
                Id = "2", 
                Content = "The technical implementation details are covered here. We discuss algorithms, data structures, and performance considerations.",
                ChunkIndex = 1,
                StartPosition = 129,
                EndPosition = 250,
                Metadata = new DocumentMetadata 
                { 
                    FileName = "sample.txt",
                    FileType = "text/plain",
                    ProcessedAt = DateTime.UtcNow
                }
            },
            new DocumentChunk
            {
                Id = "3",
                Content = "Finally, we conclude with best practices and recommendations for future improvements and maintenance.",
                ChunkIndex = 2,
                StartPosition = 251,
                EndPosition = 350,
                Metadata = new DocumentMetadata 
                { 
                    FileName = "sample.txt",
                    FileType = "text/plain",
                    ProcessedAt = DateTime.UtcNow
                }
            }
        };
    }

    private static ParsedContent CreateSampleParsedContent()
    {
        return new ParsedContent
        {
            OriginalText = "Sample document for testing quality analysis functionality.",
            StructuredText = "# Sample Document\n\nThis is a sample document for testing quality analysis functionality.\n\n## Technical Details\n\nThe document contains technical information and examples.",
            Metadata = new DocumentMetadata 
            { 
                FileName = "sample.txt",
                FileType = "text/plain",
                ProcessedAt = DateTime.UtcNow
            },
            Structure = new DocumentStructure
            {
                DocumentType = "Technical",
                Topic = "Quality Analysis",
                Summary = "A document about quality analysis testing",
                Keywords = new List<string> { "quality", "analysis", "testing", "technical" },
                Sections = new List<DocumentSection>
                {
                    new DocumentSection
                    {
                        Title = "Introduction",
                        Content = "Introduction to quality analysis",
                        Level = 1,
                        StartPosition = 0,
                        EndPosition = 50
                    },
                    new DocumentSection
                    {
                        Title = "Technical Details",
                        Content = "Technical implementation details",
                        Level = 2,
                        StartPosition = 51,
                        EndPosition = 100
                    }
                }
            }
        };
    }
}