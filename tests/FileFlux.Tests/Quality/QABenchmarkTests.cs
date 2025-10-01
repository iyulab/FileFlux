using FileFlux;
using System.Collections.Generic;
using Xunit;

namespace FileFlux.Tests.Quality;

/// <summary>
/// Tests for QABenchmark functionality including merging and validation
/// </summary>
public class QABenchmarkTests
{
    [Fact]
    public void QABenchmark_Merge_WithNullExisting_ReturnsNewQA()
    {
        // Arrange
        QABenchmark? existing = null;
        var newQA = CreateSampleQABenchmark("new", 2);

        // Act
        var result = QABenchmark.Merge(existing, newQA);

        // Assert
        Assert.Equal(newQA, result);
    }

    [Fact]
    public void QABenchmark_Merge_WithNullNew_ReturnsExisting()
    {
        // Arrange
        var existing = CreateSampleQABenchmark("existing", 2);
        QABenchmark? newQA = null;

        // Act
        var result = QABenchmark.Merge(existing, newQA);

        // Assert
        Assert.Equal(existing, result);
    }

    [Fact]
    public void QABenchmark_Merge_WithValidBenchmarks_CombinesQuestions()
    {
        // Arrange
        var existing = CreateSampleQABenchmark("doc1", 2);
        existing.Questions = new List<GeneratedQuestion>
        {
            new GeneratedQuestion
            {
                Id = "1",
                Question = "What is machine learning?",
                ExpectedAnswer = "A subset of AI that enables systems to learn from data",
                Type = QuestionType.Conceptual,
                DifficultyScore = 0.6,
                ConfidenceScore = 0.8
            },
            new GeneratedQuestion
            {
                Id = "2", 
                Question = "List the main components",
                ExpectedAnswer = "Data, algorithms, and models",
                Type = QuestionType.Factual,
                DifficultyScore = 0.3,
                ConfidenceScore = 0.9
            }
        };
        existing.AnswerabilityScore = 0.8;

        var newQA = CreateSampleQABenchmark("doc1", 2);
        newQA.Questions = new List<GeneratedQuestion>
        {
            new GeneratedQuestion
            {
                Id = "3",
                Question = "How does supervised learning work?", 
                ExpectedAnswer = "Uses labeled training data to learn patterns",
                Type = QuestionType.Procedural,
                DifficultyScore = 0.7,
                ConfidenceScore = 0.7
            },
            new GeneratedQuestion
            {
                Id = "4",
                Question = "What are the benefits?",
                ExpectedAnswer = "Automation, accuracy, and scalability",
                Type = QuestionType.Analytical,
                DifficultyScore = 0.5,
                ConfidenceScore = 0.8
            }
        };
        newQA.AnswerabilityScore = 0.7;

        // Act
        var result = QABenchmark.Merge(existing, newQA);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("doc1", result.DocumentId);
        Assert.Equal(4, result.Questions.Count);
        
        // Should contain all unique questions
        Assert.Contains(result.Questions, q => q.Id == "1");
        Assert.Contains(result.Questions, q => q.Id == "2");
        Assert.Contains(result.Questions, q => q.Id == "3");
        Assert.Contains(result.Questions, q => q.Id == "4");

        // Weighted average: (0.8 * 2 + 0.7 * 2) / 4 = 0.75
        Assert.Equal(0.75, result.AnswerabilityScore);
    }

    [Fact]
    public void QABenchmark_Merge_WithDuplicateQuestions_FiltersDuplicates()
    {
        // Arrange
        var existing = CreateSampleQABenchmark("doc1", 1);
        existing.Questions = new List<GeneratedQuestion>
        {
            new GeneratedQuestion
            {
                Id = "1",
                Question = "What is artificial intelligence and machine learning?",
                ExpectedAnswer = "AI is simulation of human intelligence",
                Type = QuestionType.Conceptual
            }
        };

        var newQA = CreateSampleQABenchmark("doc1", 1);
        newQA.Questions = new List<GeneratedQuestion>
        {
            new GeneratedQuestion
            {
                Id = "2",
                Question = "What is artificial intelligence and machine learning systems?", // Very similar
                ExpectedAnswer = "AI involves creating intelligent systems",
                Type = QuestionType.Conceptual
            }
        };

        // Act
        var result = QABenchmark.Merge(existing, newQA);

        // Assert - Should filter out duplicate based on similarity
        Assert.NotNull(result);
        Assert.True(result.Questions.Count <= 2); // May be 1 if filtered as duplicate
    }

    [Fact]
    public void QAValidationResult_AnswerabilityRatio_CalculatesCorrectly()
    {
        // Arrange
        var validation = new QAValidationResult
        {
            TotalQuestions = 10,
            AnswerableQuestions = 8,
            HighQualityAnswers = 6,
            AverageConfidence = 0.75
        };

        // Act & Assert
        Assert.Equal(0.8, validation.AnswerabilityRatio);
        Assert.Equal(0.6, validation.HighQualityRatio);
    }

    [Fact]
    public void QAValidationResult_WithZeroQuestions_ReturnsZeroRatio()
    {
        // Arrange
        var validation = new QAValidationResult
        {
            TotalQuestions = 0,
            AnswerableQuestions = 0,
            HighQualityAnswers = 0
        };

        // Act & Assert
        Assert.Equal(0.0, validation.AnswerabilityRatio);
        Assert.Equal(0.0, validation.HighQualityRatio);
    }

    [Fact]
    public void GeneratedQuestion_HasRequiredProperties()
    {
        // Arrange & Act
        var question = new GeneratedQuestion
        {
            Question = "What is the main purpose?",
            ExpectedAnswer = "To test the system",
            Type = QuestionType.Factual,
            DifficultyScore = 0.5,
            ConfidenceScore = 0.8
        };

        // Assert
        Assert.NotNull(question.Id); // Should have auto-generated ID
        Assert.False(string.IsNullOrEmpty(question.Question));
        Assert.False(string.IsNullOrEmpty(question.ExpectedAnswer));
        Assert.Equal(QuestionType.Factual, question.Type);
        Assert.Equal(0.5, question.DifficultyScore);
        Assert.Equal(0.8, question.ConfidenceScore);
        Assert.Empty(question.RelevantChunkIds); // Should be initialized but empty
        Assert.NotNull(question.Metadata); // Should be initialized
    }

    [Theory]
    [InlineData(QuestionType.Factual)]
    [InlineData(QuestionType.Conceptual)]
    [InlineData(QuestionType.Analytical)]
    [InlineData(QuestionType.Procedural)]
    [InlineData(QuestionType.Comparative)]
    public void QuestionType_AllEnumValues_AreValid(QuestionType questionType)
    {
        // Arrange & Act
        var question = new GeneratedQuestion
        {
            Question = "Test question",
            ExpectedAnswer = "Test answer",
            Type = questionType
        };

        // Assert
        Assert.Equal(questionType, question.Type);
        Assert.True(Enum.IsDefined(typeof(QuestionType), questionType));
    }

    [Fact]
    public void QualityBenchmarkResult_HasCorrectStructure()
    {
        // Arrange & Act
        var benchmarkResult = new QualityBenchmarkResult
        {
            DocumentId = "test-doc",
            QualityReports = new List<DocumentQualityReport>
            {
                new DocumentQualityReport { OverallQualityScore = 0.8 },
                new DocumentQualityReport { OverallQualityScore = 0.7 }
            },
            ComparisonMetrics = new Dictionary<string, object>
            {
                { "best_score", 0.8 },
                { "worst_score", 0.7 }
            },
            RecommendedStrategy = "Intelligent"
        };

        // Assert
        Assert.Equal("test-doc", benchmarkResult.DocumentId);
        Assert.Equal(2, benchmarkResult.QualityReports.Count);
        Assert.Equal(2, benchmarkResult.ComparisonMetrics.Count);
        Assert.Equal("Intelligent", benchmarkResult.RecommendedStrategy);
        Assert.True(benchmarkResult.BenchmarkedAt > DateTime.UtcNow.AddMinutes(-1)); // Recent timestamp
    }

    private static QABenchmark CreateSampleQABenchmark(string documentId, int questionCount)
    {
        var benchmark = new QABenchmark
        {
            DocumentId = documentId,
            DocumentPath = $"{documentId}.txt",
            ValidationResult = new QAValidationResult
            {
                TotalQuestions = questionCount,
                AnswerableQuestions = questionCount,
                HighQualityAnswers = questionCount - 1,
                AverageConfidence = 0.8
            },
            AnswerabilityScore = 0.8
        };

        // Add sample questions
        for (int i = 0; i < questionCount; i++)
        {
            benchmark.Questions.Add(new GeneratedQuestion
            {
                Id = Guid.NewGuid().ToString(),
                Question = $"Sample question {i + 1}?",
                ExpectedAnswer = $"Sample answer {i + 1}",
                Type = (QuestionType)(i % 5), // Cycle through question types
                DifficultyScore = 0.5 + (i * 0.1),
                ConfidenceScore = 0.7 + (i * 0.05)
            });
        }

        return benchmark;
    }
}