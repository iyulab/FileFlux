using FileFlux;
using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Exceptions;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Quality;
using FileFlux.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;
using Xunit;

namespace FileFlux.Tests.Quality;

/// <summary>
/// Tests for DocumentQualityAnalyzer to ensure RAG quality analysis functionality
/// </summary>
public class DocumentQualityAnalyzerTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IDocumentProcessor _processor;
    private readonly string _testFilePath;
    private const string TestContent = @"
# Document Quality Analysis Test

This is a comprehensive test document for quality analysis.

## Section 1: Introduction
This document contains multiple sections and various content types to test the quality analysis capabilities.

## Section 2: Technical Content
Here we have some technical information:
- API endpoints
- Database schemas  
- Performance metrics

## Section 3: Business Logic
The business logic section contains:
1. Process flows
2. Decision trees
3. Business rules

## Conclusion
This document should generate multiple chunks with good quality scores.";

    public DocumentQualityAnalyzerTests()
    {
        var services = new ServiceCollection();
        services.AddFileFlux();
        services.AddSingleton<ITextCompletionService, MockTextCompletionService>();
        services.AddSingleton<ILogger<DocumentProcessor>>(NullLogger<DocumentProcessor>.Instance);
        
        _serviceProvider = services.BuildServiceProvider();
        _processor = _serviceProvider.GetRequiredService<IDocumentProcessor>();

        // Create test file
        _testFilePath = Path.Combine(Path.GetTempPath(), "quality_test.txt");
        File.WriteAllText(_testFilePath, TestContent);
    }

    [Fact]
    public async Task AnalyzeQualityAsync_WithValidDocument_ReturnsQualityReport()
    {
        // Arrange
        var qualityEngine = new ChunkQualityEngine();
        var analyzer = new DocumentQualityAnalyzer(qualityEngine, _processor);
        var options = new ChunkingOptions { Strategy = "Intelligent", MaxChunkSize = 512 };

        // Act
        var result = await analyzer.AnalyzeQualityAsync(_testFilePath, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(Path.GetFileNameWithoutExtension(_testFilePath), result.DocumentId);
        Assert.Equal(_testFilePath, result.DocumentPath);
        Assert.True(result.OverallQualityScore >= 0.0 && result.OverallQualityScore <= 1.0);
        
        Assert.NotNull(result.ChunkingQuality);
        Assert.True(result.ChunkingQuality.AverageCompleteness >= 0.0);
        Assert.True(result.ChunkingQuality.ContentConsistency >= 0.0);
        Assert.True(result.ChunkingQuality.BoundaryQuality >= 0.0);
        
        Assert.NotNull(result.InformationDensity);
        Assert.True(result.InformationDensity.AverageInformationDensity >= 0.0);
        
        Assert.NotNull(result.StructuralCoherence);
        Assert.True(result.StructuralCoherence.StructurePreservation >= 0.0);
        
        Assert.NotNull(result.Recommendations);
        Assert.Equal(options, result.ProcessingOptions);
    }

    [Fact]
    public async Task AnalyzeQualityAsync_WithInvalidFile_ThrowsException()
    {
        // Arrange
        var qualityEngine = new ChunkQualityEngine();
        var analyzer = new DocumentQualityAnalyzer(qualityEngine, _processor);
        var invalidPath = "nonexistent_file.txt";

        // Act & Assert
        await Assert.ThrowsAsync<DocumentProcessingException>(
            () => analyzer.AnalyzeQualityAsync(invalidPath));
    }

    [Fact]
    public async Task EvaluateChunksAsync_WithValidChunks_ReturnsMetrics()
    {
        // Arrange
        var qualityEngine = new ChunkQualityEngine();
        var analyzer = new DocumentQualityAnalyzer(qualityEngine, _processor);
        
        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Id = "1",
                Content = "This is a complete chunk with meaningful content about testing quality analysis.",
                ChunkIndex = 0,
                StartPosition = 0,
                EndPosition = 77,
                Metadata = new DocumentMetadata { FileName = "test.txt" }
            },
            new DocumentChunk
            {
                Id = "2", 
                Content = "Another chunk discussing technical implementation details and best practices.",
                ChunkIndex = 1,
                StartPosition = 78,
                EndPosition = 151,
                Metadata = new DocumentMetadata { FileName = "test.txt" }
            }
        };

        // Act
        var result = await analyzer.EvaluateChunksAsync(chunks);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.AverageCompleteness >= 0.0 && result.AverageCompleteness <= 1.0);
        Assert.True(result.ContentConsistency >= 0.0 && result.ContentConsistency <= 1.0);
        Assert.True(result.BoundaryQuality >= 0.0 && result.BoundaryQuality <= 1.0);
        Assert.True(result.SizeDistribution >= 0.0 && result.SizeDistribution <= 1.0);
        Assert.True(result.OverlapEffectiveness >= 0.0 && result.OverlapEffectiveness <= 1.0);
    }

    [Fact]
    public async Task GenerateQABenchmarkAsync_WithValidDocument_ReturnsQABenchmark()
    {
        // Arrange
        var qualityEngine = new ChunkQualityEngine();
        var analyzer = new DocumentQualityAnalyzer(qualityEngine, _processor);
        const int questionCount = 5;

        // Act
        var result = await analyzer.GenerateQABenchmarkAsync(_testFilePath, questionCount);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(Path.GetFileNameWithoutExtension(_testFilePath), result.DocumentId);
        Assert.Equal(_testFilePath, result.DocumentPath);
        Assert.NotNull(result.Questions);
        Assert.Equal(questionCount, result.Questions.Count);
        Assert.True(result.AnswerabilityScore >= 0.0 && result.AnswerabilityScore <= 1.0);
        Assert.NotNull(result.ValidationResult);
        Assert.Equal(questionCount, result.ValidationResult.TotalQuestions);
    }

    [Fact]
    public async Task BenchmarkChunkingAsync_WithMultipleStrategies_ReturnsComparison()
    {
        // Arrange
        var qualityEngine = new ChunkQualityEngine();
        var analyzer = new DocumentQualityAnalyzer(qualityEngine, _processor);
        var strategies = new[] { "Intelligent", "Semantic", "FixedSize" };

        // Act
        var result = await analyzer.BenchmarkChunkingAsync(_testFilePath, strategies);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(Path.GetFileNameWithoutExtension(_testFilePath), result.DocumentId);
        Assert.NotNull(result.QualityReports);
        Assert.Equal(3, result.QualityReports.Count);
        Assert.NotNull(result.ComparisonMetrics);
        Assert.True(result.ComparisonMetrics.ContainsKey("best_strategy"));
        Assert.True(result.ComparisonMetrics.ContainsKey("best_score"));
        Assert.NotEmpty(result.RecommendedStrategy);
    }

    [Fact]
    public async Task DocumentProcessor_AnalyzeQualityAsync_ProducesConsistentResults()
    {
        // Arrange
        var options = new ChunkingOptions { Strategy = "Intelligent" };

        // Act - Test through DocumentProcessor API
        var processorResult = await _processor.AnalyzeQualityAsync(_testFilePath, options);

        // Assert - Verify API consistency 
        Assert.NotNull(processorResult);
        Assert.True(processorResult.OverallQualityScore >= 0.0 && processorResult.OverallQualityScore <= 1.0);
        Assert.NotNull(processorResult.ChunkingQuality);
        Assert.NotNull(processorResult.InformationDensity);
        Assert.NotNull(processorResult.StructuralCoherence);
        Assert.NotNull(processorResult.Recommendations);
    }

    [Fact]
    public async Task DocumentProcessor_GenerateQAAsync_WithoutExistingQA_ReturnsNewBenchmark()
    {
        // Arrange
        const int questionCount = 3;

        // Act
        var result = await _processor.GenerateQAAsync(_testFilePath, questionCount);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(questionCount, result.Questions.Count);
        Assert.True(result.AnswerabilityScore >= 0.0);
    }

    [Fact]
    public async Task DocumentProcessor_GenerateQAAsync_WithExistingQA_MergesResults()
    {
        // Arrange
        const int initialQuestionCount = 2;
        const int additionalQuestionCount = 3;

        var existingQA = new QABenchmark
        {
            DocumentId = "existing",
            Questions = new List<GeneratedQuestion>
            {
                new GeneratedQuestion 
                { 
                    Question = "What is the main topic?", 
                    ExpectedAnswer = "Quality analysis",
                    Type = QuestionType.Factual
                },
                new GeneratedQuestion 
                { 
                    Question = "How many sections are there?", 
                    ExpectedAnswer = "Three sections",
                    Type = QuestionType.Factual
                }
            },
            ValidationResult = new QAValidationResult { TotalQuestions = 2 },
            AnswerabilityScore = 0.8
        };

        // Act
        var result = await _processor.GenerateQAAsync(_testFilePath, additionalQuestionCount, existingQA);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Questions.Count >= initialQuestionCount); // Should have at least the existing questions
        Assert.True(result.Questions.Count <= initialQuestionCount + additionalQuestionCount); // May have duplicates filtered out
    }

    public void Dispose()
    {
        if (File.Exists(_testFilePath))
        {
            File.Delete(_testFilePath);
        }
        
        _serviceProvider?.Dispose();
    }
}