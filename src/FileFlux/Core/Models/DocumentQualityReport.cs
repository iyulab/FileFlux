using FileFlux.Domain;
using System;
using System.Collections.Generic;

namespace FileFlux;

/// <summary>
/// Comprehensive quality report for document processing results.
/// Designed for advanced metadata tracking and RAG system optimization.
/// </summary>
public class DocumentQualityReport
{
    /// <summary>
    /// Document identifier for tracking and correlation
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// Document file path or identifier
    /// </summary>
    public string DocumentPath { get; set; } = string.Empty;

    /// <summary>
    /// Overall quality score (0.0-1.0) combining all metrics
    /// Higher score indicates better RAG optimization
    /// </summary>
    public double OverallQualityScore { get; set; }

    /// <summary>
    /// Chunking-specific quality metrics
    /// </summary>
    public ChunkingQualityMetrics ChunkingQuality { get; set; } = new();

    /// <summary>
    /// Information density and content richness metrics
    /// </summary>
    public InformationDensityMetrics InformationDensity { get; set; } = new();

    /// <summary>
    /// Structural coherence and document organization metrics
    /// </summary>
    public StructuralCoherenceMetrics StructuralCoherence { get; set; } = new();

    /// <summary>
    /// Automated recommendations for improving RAG performance
    /// </summary>
    public List<QualityRecommendation> Recommendations { get; set; } = new();

    /// <summary>
    /// Detailed metrics for advanced analysis and debugging
    /// Extended metadata in key-value pairs for analysis
    /// </summary>
    public Dictionary<string, object> DetailedMetrics { get; } = new();

    /// <summary>
    /// Processing timestamp for tracking and versioning
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Processing options used for this analysis
    /// </summary>
    public ChunkingOptions? ProcessingOptions { get; set; }
}

/// <summary>
/// Chunking-specific quality metrics for RAG optimization
/// </summary>
public class ChunkingQualityMetrics
{
    /// <summary>
    /// Average chunk completeness score (0.0-1.0)
    /// Measures how self-contained each chunk is
    /// </summary>
    public double AverageCompleteness { get; set; }

    /// <summary>
    /// Content consistency across chunks (0.0-1.0)
    /// Higher score indicates better semantic coherence
    /// </summary>
    public double ContentConsistency { get; set; }

    /// <summary>
    /// Boundary quality score (0.0-1.0)
    /// Measures how well chunks respect semantic boundaries
    /// </summary>
    public double BoundaryQuality { get; set; }

    /// <summary>
    /// Size distribution uniformity (0.0-1.0)
    /// Balanced chunk sizes improve RAG performance
    /// </summary>
    public double SizeDistribution { get; set; }

    /// <summary>
    /// Overlap effectiveness score (0.0-1.0)
    /// Quality of information overlap between adjacent chunks
    /// </summary>
    public double OverlapEffectiveness { get; set; }
}

/// <summary>
/// Information density and content richness metrics
/// </summary>
public class InformationDensityMetrics
{
    /// <summary>
    /// Average information density per chunk (0.0-1.0)
    /// Higher density means more meaningful content per token
    /// </summary>
    public double AverageInformationDensity { get; set; }

    /// <summary>
    /// Keyword richness score (0.0-1.0)
    /// Density of important terms and concepts
    /// </summary>
    public double KeywordRichness { get; set; }

    /// <summary>
    /// Factual content ratio (0.0-1.0)
    /// Proportion of factual vs. structural content
    /// </summary>
    public double FactualContentRatio { get; set; }

    /// <summary>
    /// Redundancy level (0.0-1.0)
    /// Lower values indicate less repetitive content
    /// </summary>
    public double RedundancyLevel { get; set; }
}

/// <summary>
/// Structural coherence and document organization metrics
/// </summary>
public class StructuralCoherenceMetrics
{
    /// <summary>
    /// Hierarchical structure preservation (0.0-1.0)
    /// How well the original document structure is maintained
    /// </summary>
    public double StructurePreservation { get; set; }

    /// <summary>
    /// Context continuity across chunks (0.0-1.0)
    /// Logical flow and context preservation
    /// </summary>
    public double ContextContinuity { get; set; }

    /// <summary>
    /// Reference integrity score (0.0-1.0)
    /// Quality of cross-references and citations handling
    /// </summary>
    public double ReferenceIntegrity { get; set; }

    /// <summary>
    /// Metadata richness (0.0-1.0)
    /// Quality and completeness of chunk metadata
    /// </summary>
    public double MetadataRichness { get; set; }
}