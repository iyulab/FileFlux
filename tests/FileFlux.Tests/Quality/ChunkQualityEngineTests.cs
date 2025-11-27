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
                Content = new string('A', 500), // All chunks same size
                Index = i,
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
                Content = "Short",
                Index = 0,
                Metadata = new DocumentMetadata { FileName = "test.txt" }
            },
            new DocumentChunk
            {
                Content = new string('A', 2000), // Very large chunk
                Index = 1,
                Metadata = new DocumentMetadata { FileName = "test.txt" }
            },
            new DocumentChunk
            {
                Content = new string('B', 500),
                Index = 2,
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
    public void CalculateOverallQualityScore_WithValidMetrics_ReturnsValidScore()
    {
        // Arrange
        var chunkingMetrics = new ChunkingQualityMetrics
        {
            AverageCompleteness = 0.8,
            ContentConsistency = 0.7,
            BoundaryQuality = 0.9,
            SizeDistribution = 0.85,
            OverlapEffectiveness = 0.75
        };
        var densityMetrics = new InformationDensityMetrics
        {
            AverageInformationDensity = 0.8,
            KeywordRichness = 0.7,
            FactualContentRatio = 0.75,
            RedundancyLevel = 0.1
        };
        var structureMetrics = new StructuralCoherenceMetrics
        {
            StructurePreservation = 0.85,
            ContextContinuity = 0.8,
            ReferenceIntegrity = 0.9,
            MetadataRichness = 0.75
        };

        // Act
        var score = _engine.CalculateOverallQualityScore(chunkingMetrics, densityMetrics, structureMetrics);

        // Assert
        Assert.InRange(score, 0.0, 1.0);
    }

    [Fact]
    public void GenerateRecommendations_WithLowMetrics_ReturnsRecommendations()
    {
        // Arrange
        var chunkingMetrics = new ChunkingQualityMetrics
        {
            AverageCompleteness = 0.4, // Low
            ContentConsistency = 0.3, // Low
            BoundaryQuality = 0.5,
            SizeDistribution = 0.2, // Low
            OverlapEffectiveness = 0.6
        };
        var densityMetrics = new InformationDensityMetrics
        {
            AverageInformationDensity = 0.5,
            KeywordRichness = 0.4,
            FactualContentRatio = 0.5,
            RedundancyLevel = 0.8 // High redundancy is bad
        };
        var structureMetrics = new StructuralCoherenceMetrics
        {
            StructurePreservation = 0.4, // Low
            ContextContinuity = 0.5,
            ReferenceIntegrity = 0.6,
            MetadataRichness = 0.3
        };
        var options = new ChunkingOptions();

        // Act
        var recommendations = _engine.GenerateRecommendations(chunkingMetrics, densityMetrics, structureMetrics, options);

        // Assert
        Assert.NotNull(recommendations);
        Assert.NotEmpty(recommendations);
    }

    [Fact]
    public void GenerateRecommendations_WithHighMetrics_ReturnsFewerRecommendations()
    {
        // Arrange
        var highChunkingMetrics = new ChunkingQualityMetrics
        {
            AverageCompleteness = 0.9,
            ContentConsistency = 0.95,
            BoundaryQuality = 0.88,
            SizeDistribution = 0.92,
            OverlapEffectiveness = 0.85
        };
        var highDensityMetrics = new InformationDensityMetrics
        {
            AverageInformationDensity = 0.9,
            KeywordRichness = 0.85,
            FactualContentRatio = 0.88,
            RedundancyLevel = 0.1 // Low redundancy is good
        };
        var highStructureMetrics = new StructuralCoherenceMetrics
        {
            StructurePreservation = 0.9,
            ContextContinuity = 0.88,
            ReferenceIntegrity = 0.92,
            MetadataRichness = 0.85
        };

        var lowChunkingMetrics = new ChunkingQualityMetrics
        {
            AverageCompleteness = 0.3,
            ContentConsistency = 0.25,
            BoundaryQuality = 0.4,
            SizeDistribution = 0.35,
            OverlapEffectiveness = 0.3
        };
        var lowDensityMetrics = new InformationDensityMetrics
        {
            AverageInformationDensity = 0.4,
            KeywordRichness = 0.3,
            FactualContentRatio = 0.35,
            RedundancyLevel = 0.85 // High redundancy is bad
        };
        var lowStructureMetrics = new StructuralCoherenceMetrics
        {
            StructurePreservation = 0.4,
            ContextContinuity = 0.35,
            ReferenceIntegrity = 0.3,
            MetadataRichness = 0.25
        };
        var options = new ChunkingOptions();

        // Act
        var highRecommendations = _engine.GenerateRecommendations(highChunkingMetrics, highDensityMetrics, highStructureMetrics, options);
        var lowRecommendations = _engine.GenerateRecommendations(lowChunkingMetrics, lowDensityMetrics, lowStructureMetrics, options);

        // Assert
        Assert.True(highRecommendations.Count <= lowRecommendations.Count,
            "High quality metrics should result in fewer or equal recommendations");
    }

    private static List<DocumentChunk> CreateSampleChunks()
    {
        return new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Content = "This is the introduction section of our document. It provides an overview of the main concepts and sets the stage for detailed discussion.",
                Index = 0,
                Location = new() { StartChar = 0, EndChar = 128 },
                Metadata = new DocumentMetadata
                {
                    FileName = "sample.txt",
                    FileType = "text/plain",
                    ProcessedAt = DateTime.UtcNow
                }
            },
            new DocumentChunk
            {
                Content = "The technical implementation details are covered here. We discuss algorithms, data structures, and performance considerations.",
                Index = 1,
                Location = new() { StartChar = 129, EndChar = 250 },
                Metadata = new DocumentMetadata
                {
                    FileName = "sample.txt",
                    FileType = "text/plain",
                    ProcessedAt = DateTime.UtcNow
                }
            },
            new DocumentChunk
            {
                Content = "Finally, we conclude with best practices and recommendations for future improvements and maintenance.",
                Index = 2,
                Location = new() { StartChar = 251, EndChar = 350 },
                Metadata = new DocumentMetadata
                {
                    FileName = "sample.txt",
                    FileType = "text/plain",
                    ProcessedAt = DateTime.UtcNow
                }
            }
        };
    }
}
