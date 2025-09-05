using FileFlux.Domain;
using System;
using System.Collections.Generic;

namespace FileFlux;

/// <summary>
/// Comprehensive quality metrics that combines all quality dimensions
/// Used by ChunkQualityEngine for internal calculations
/// </summary>
public class ComprehensiveQualityMetrics
{
    // Chunking Quality Metrics
    public double AverageCompleteness { get; set; }
    public double ContentConsistency { get; set; }
    public double BoundaryQuality { get; set; }
    public double SizeDistribution { get; set; }
    public double OverlapEffectiveness { get; set; }

    // Information Density Metrics
    public double AverageInformationDensity { get; set; }
    public double KeywordRichness { get; set; }
    public double FactualContentRatio { get; set; }
    public double RedundancyLevel { get; set; }

    // Structural Coherence Metrics
    public double StructurePreservation { get; set; }
    public double ContextContinuity { get; set; }
    public double ReferenceIntegrity { get; set; }
    public double MetadataRichness { get; set; }

    /// <summary>
    /// Converts comprehensive metrics to separated category metrics
    /// </summary>
    public (ChunkingQualityMetrics chunking, InformationDensityMetrics information, StructuralCoherenceMetrics structural) ToSeparatedMetrics()
    {
        var chunking = new ChunkingQualityMetrics
        {
            AverageCompleteness = AverageCompleteness,
            ContentConsistency = ContentConsistency,
            BoundaryQuality = BoundaryQuality,
            SizeDistribution = SizeDistribution,
            OverlapEffectiveness = OverlapEffectiveness
        };

        var information = new InformationDensityMetrics
        {
            AverageInformationDensity = AverageInformationDensity,
            KeywordRichness = KeywordRichness,
            FactualContentRatio = FactualContentRatio,
            RedundancyLevel = RedundancyLevel
        };

        var structural = new StructuralCoherenceMetrics
        {
            StructurePreservation = StructurePreservation,
            ContextContinuity = ContextContinuity,
            ReferenceIntegrity = ReferenceIntegrity,
            MetadataRichness = MetadataRichness
        };

        return (chunking, information, structural);
    }
}