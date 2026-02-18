using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileFlux.Domain;

namespace FileFlux.Infrastructure.Quality;

/// <summary>
/// Internal quality engine for chunk quality analysis.
/// Provides consistent quality metrics for document chunks.
/// </summary>
public class ChunkQualityEngine
{
    public ChunkQualityEngine()
    {
    }

    /// <summary>
    /// Calculates comprehensive quality metrics for document chunks.
    /// </summary>
    public static async Task<ChunkingQualityMetrics> CalculateQualityMetricsAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var comprehensive = await CalculateComprehensiveQualityMetricsAsync(chunks, cancellationToken);
        var (chunking, _, _) = comprehensive.ToSeparatedMetrics();
        return chunking;
    }

    /// <summary>
    /// Calculates comprehensive quality metrics for internal use
    /// </summary>
    private static async Task<ComprehensiveQualityMetrics> CalculateComprehensiveQualityMetricsAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        if (chunkList.Count == 0)
            return new ComprehensiveQualityMetrics();

        var metrics = new ComprehensiveQualityMetrics();

        // Calculate individual chunk quality scores
        var chunkQualities = await Task.WhenAll(
            chunkList.Select(async chunk => await AnalyzeChunkQualityAsync(chunk, cancellationToken))
        );

        // Aggregate chunking quality metrics
        metrics.AverageCompleteness = chunkQualities.Average(q => q.Completeness);
        metrics.ContentConsistency = CalculateContentConsistency(chunkList);
        metrics.BoundaryQuality = CalculateBoundaryQuality(chunkList);
        metrics.SizeDistribution = CalculateSizeDistribution(chunkList);
        metrics.OverlapEffectiveness = CalculateOverlapEffectiveness(chunkList);

        // Calculate information density metrics
        metrics.AverageInformationDensity = CalculateInformationDensity(chunkList);
        metrics.KeywordRichness = CalculateKeywordRichness(chunkList);
        metrics.FactualContentRatio = CalculateFactualContentRatio(chunkList);
        metrics.RedundancyLevel = CalculateRedundancyLevel(chunkList);

        // Calculate structural coherence metrics
        metrics.StructurePreservation = CalculateStructurePreservation(chunkList);
        metrics.ContextContinuity = CalculateContextContinuity(chunkList);
        metrics.ReferenceIntegrity = CalculateReferenceIntegrity(chunkList);
        metrics.MetadataRichness = CalculateMetadataRichness(chunkList);

        return metrics;
    }

    /// <summary>
    /// Calculates overall document quality score combining all metrics.
    /// </summary>
    public static double CalculateOverallQualityScore(
        ChunkingQualityMetrics chunkingQuality,
        InformationDensityMetrics informationDensity,
        StructuralCoherenceMetrics structuralCoherence)
    {
        // Weighted combination of metric categories
        var chunkingWeight = 0.4;
        var densityWeight = 0.3;
        var structureWeight = 0.3;

        var chunkingScore = (chunkingQuality.AverageCompleteness +
                           chunkingQuality.ContentConsistency +
                           chunkingQuality.BoundaryQuality +
                           chunkingQuality.SizeDistribution +
                           chunkingQuality.OverlapEffectiveness) / 5;

        var densityScore = (informationDensity.AverageInformationDensity +
                           informationDensity.KeywordRichness +
                           informationDensity.FactualContentRatio +
                           (1 - informationDensity.RedundancyLevel)) / 4;

        var structureScore = (structuralCoherence.StructurePreservation +
                             structuralCoherence.ContextContinuity +
                             structuralCoherence.ReferenceIntegrity +
                             structuralCoherence.MetadataRichness) / 4;

        return (chunkingScore * chunkingWeight) +
               (densityScore * densityWeight) +
               (structureScore * structureWeight);
    }

    /// <summary>
    /// Generates quality recommendations based on analysis results.
    /// </summary>
    public static List<QualityRecommendation> GenerateRecommendations(
        ChunkingQualityMetrics chunkingQuality,
        InformationDensityMetrics informationDensity,
        StructuralCoherenceMetrics structuralCoherence,
        ChunkingOptions currentOptions)
    {
        var recommendations = new List<QualityRecommendation>();

        // Chunk size recommendations
        if (chunkingQuality.SizeDistribution < 0.7)
        {
            recommendations.Add(new QualityRecommendation
            {
                Type = RecommendationType.ChunkSize,
                Priority = (int)RecommendationPriority.High,
                Description = "Consider adjusting chunk size for better size distribution uniformity",
                ExpectedImprovement = 0.15,
                SuggestedParameters = new Dictionary<string, object>
                {
                    ["MaxChunkSize"] = Math.Max(256, currentOptions.MaxChunkSize * 0.8),
                    ["Reason"] = "Current chunking produces uneven chunk sizes"
                }
            });
        }

        // Boundary quality recommendations
        if (chunkingQuality.BoundaryQuality < 0.6)
        {
            recommendations.Add(new QualityRecommendation
            {
                Type = RecommendationType.ChunkingStrategy,
                Priority = (int)RecommendationPriority.Critical,
                Description = "Switch to Intelligent chunking strategy for better semantic boundary detection",
                ExpectedImprovement = 0.25,
                SuggestedParameters = new Dictionary<string, object>
                {
                    ["Strategy"] = "Intelligent",
                    ["Reason"] = "Current strategy produces poor semantic boundaries"
                }
            });
        }

        // Information density recommendations
        if (informationDensity.RedundancyLevel > 0.7)
        {
            recommendations.Add(new QualityRecommendation
            {
                Type = RecommendationType.ContentFiltering,
                Priority = (int)RecommendationPriority.Medium,
                Description = "High redundancy detected - consider content filtering or preprocessing",
                ExpectedImprovement = 0.12,
                SuggestedParameters = new Dictionary<string, object>
                {
                    ["EnableContentFiltering"] = true,
                    ["RedundancyThreshold"] = 0.5
                }
            });
        }

        // Structure preservation recommendations
        if (structuralCoherence.StructurePreservation < 0.7)
        {
            recommendations.Add(new QualityRecommendation
            {
                Type = RecommendationType.StructurePreservation,
                Priority = (int)RecommendationPriority.High,
                Description = "Enable structure preservation to maintain document hierarchy",
                ExpectedImprovement = 0.18,
                SuggestedParameters = new Dictionary<string, object>
                {
                    ["PreserveStructure"] = true,
                    ["IncludeMetadata"] = true
                }
            });
        }

        return recommendations.OrderByDescending(r => r.ExpectedImprovement).ToList();
    }

    #region Private Helper Methods

    private static Task<(double Completeness, double Coherence)> AnalyzeChunkQualityAsync(
        DocumentChunk chunk, CancellationToken cancellationToken)
    {
        // Analyze chunk completeness and coherence
        var completeness = AnalyzeChunkCompleteness(chunk);
        var coherence = AnalyzeChunkCoherence(chunk);

        return Task.FromResult((completeness, coherence));
    }

    private static double AnalyzeChunkCompleteness(DocumentChunk chunk)
    {
        var content = chunk.Content.Trim();
        if (string.IsNullOrEmpty(content)) return 0;

        var score = 0.5; // Base score

        // Check for complete sentences
        if (content.EndsWith('.') || content.EndsWith('!') || content.EndsWith('?'))
            score += 0.2;

        // Check for proper capitalization
        if (char.IsUpper(content[0]))
            score += 0.1;

        // Check for reasonable length
        if (content.Length > 50 && content.Length < 2000)
            score += 0.2;

        return Math.Min(1.0, score);
    }

    private static double AnalyzeChunkCoherence(DocumentChunk chunk)
    {
        var content = chunk.Content;
        if (string.IsNullOrEmpty(content)) return 0;

        var sentences = content.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (sentences.Length < 2) return 0.8; // Single sentence is coherent

        // Simple coherence analysis based on sentence connectivity
        var coherenceScore = 0.5;

        // Check for transition words
        var transitionWords = new[] { "however", "therefore", "additionally", "furthermore", "moreover", "consequently" };
        var transitionCount = transitionWords.Count(word =>
            content.Contains(word, StringComparison.OrdinalIgnoreCase));

        coherenceScore += Math.Min(0.3, transitionCount * 0.1);

        // Check for consistent terminology
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var uniqueWords = words.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var repetitionRatio = 1.0 - ((double)uniqueWords / words.Length);

        if (repetitionRatio > 0.1 && repetitionRatio < 0.5)
            coherenceScore += 0.2;

        return Math.Min(1.0, coherenceScore);
    }

    private static double CalculateContentConsistency(List<DocumentChunk> chunks)
    {
        if (chunks.Count < 2) return 1.0;

        // Analyze consistency in writing style, terminology, and format
        var avgLengths = chunks.Select(c => (double)c.Content.Length);
        var lengthVariance = CalculateVariance(avgLengths);
        var lengthConsistency = Math.Max(0, 1.0 - (lengthVariance / 100000)); // Normalize variance

        return Math.Min(1.0, lengthConsistency);
    }

    private static double CalculateBoundaryQuality(List<DocumentChunk> chunks)
    {
        if (chunks.Count < 2) return 1.0;

        var boundaryScores = new List<double>();

        for (int i = 0; i < chunks.Count - 1; i++)
        {
            var currentChunk = chunks[i];
            var nextChunk = chunks[i + 1];

            var boundaryScore = AnalyzeBoundaryQuality(currentChunk, nextChunk);
            boundaryScores.Add(boundaryScore);
        }

        return boundaryScores.Count != 0 ? boundaryScores.Average() : 0.5;
    }

    private static double AnalyzeBoundaryQuality(DocumentChunk current, DocumentChunk next)
    {
        var score = 0.5; // Base score

        // Check if current chunk ends with sentence boundary
        var currentContent = current.Content.Trim();
        if (currentContent.EndsWith('.') || currentContent.EndsWith('!') || currentContent.EndsWith('?'))
            score += 0.25;

        // Check if next chunk starts with capital letter
        var nextContent = next.Content.Trim();
        if (nextContent.Length > 0 && char.IsUpper(nextContent[0]))
            score += 0.25;

        return Math.Min(1.0, score);
    }

    private static double CalculateSizeDistribution(List<DocumentChunk> chunks)
    {
        if (chunks.Count == 0) return 0;

        var sizes = chunks.Select(c => (double)c.Content.Length);
        var avgSize = sizes.Average();
        var variance = CalculateVariance(sizes);
        var coefficient = variance > 0 ? Math.Sqrt(variance) / avgSize : 0;

        // Lower coefficient of variation indicates better size distribution
        return Math.Max(0, 1.0 - coefficient);
    }

    private static double CalculateOverlapEffectiveness(List<DocumentChunk> chunks)
    {
        if (chunks.Count < 2) return 1.0;

        var overlapScores = new List<double>();

        for (int i = 0; i < chunks.Count - 1; i++)
        {
            var current = chunks[i];
            var next = chunks[i + 1];

            // Analyze overlap quality between adjacent chunks
            var overlapScore = AnalyzeOverlapQuality(current, next);
            overlapScores.Add(overlapScore);
        }

        return overlapScores.Count != 0 ? overlapScores.Average() : 0.5;
    }

    private static double AnalyzeOverlapQuality(DocumentChunk current, DocumentChunk next)
    {
        // Simple word-based overlap analysis
        var currentWords = current.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var nextWords = next.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var overlap = currentWords.Intersect(nextWords, StringComparer.OrdinalIgnoreCase).Count();
        var union = currentWords.Union(nextWords, StringComparer.OrdinalIgnoreCase).Count();

        return union > 0 ? (double)overlap / union : 0;
    }

    private static double CalculateVariance(IEnumerable<double> values)
    {
        var valueList = values.ToList();
        if (valueList.Count == 0) return 0;

        var mean = valueList.Average();
        var variance = valueList.Sum(v => Math.Pow(v - mean, 2)) / valueList.Count;
        return variance;
    }

    #endregion

    #region Information Density Metrics

    private static double CalculateInformationDensity(List<DocumentChunk> chunks)
    {
        if (chunks.Count == 0) return 0.0;

        // Simple heuristic: ratio of meaningful words to total words
        var densities = chunks.Select(chunk =>
        {
            var words = chunk.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var meaningfulWords = words.Where(w => w.Length > 3 && !IsStopWord(w)).Count();
            return words.Length > 0 ? (double)meaningfulWords / words.Length : 0.0;
        });

        return densities.Average();
    }

    private static double CalculateKeywordRichness(List<DocumentChunk> chunks)
    {
        if (chunks.Count == 0) return 0.0;

        // Measure density of technical/important keywords
        var keywordPatterns = new[] { "api", "data", "system", "method", "process", "result", "analysis" };
        var totalWords = chunks.Sum(c => c.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        var keywordCount = chunks.Sum(c => keywordPatterns.Count(kw =>
            c.Content.Contains(kw, StringComparison.OrdinalIgnoreCase)));

        return totalWords > 0 ? Math.Min(1.0, (double)keywordCount / totalWords * 10) : 0.0;
    }

    private static double CalculateFactualContentRatio(List<DocumentChunk> chunks)
    {
        if (chunks.Count == 0) return 0.0;

        // Simple heuristic: chunks with numbers, specific terms, etc.
        var factualChunks = chunks.Count(chunk =>
            System.Text.RegularExpressions.Regex.IsMatch(chunk.Content, @"\d+") ||
            chunk.Content.Contains("percent") ||
            chunk.Content.Contains("result") ||
            chunk.Content.Contains("data"));

        return (double)factualChunks / chunks.Count;
    }

    private static double CalculateRedundancyLevel(List<DocumentChunk> chunks)
    {
        if (chunks.Count < 2) return 0.0;

        // Measure similarity between consecutive chunks
        var similarities = new List<double>();
        for (int i = 0; i < chunks.Count - 1; i++)
        {
            var similarity = CalculateSimilarity(chunks[i].Content, chunks[i + 1].Content);
            similarities.Add(similarity);
        }

        return similarities.Average();
    }

    #endregion

    #region Structural Coherence Metrics

    private static double CalculateStructurePreservation(List<DocumentChunk> chunks)
    {
        if (chunks.Count == 0) return 0.0;

        // Check for structure markers (headers, lists, etc.)
        var structuredChunks = chunks.Count(chunk =>
            chunk.Content.Contains('#') ||
            chunk.Content.Contains("- ") ||
            chunk.Content.Contains("1.") ||
            chunk.Content.Contains('•'));

        return (double)structuredChunks / chunks.Count;
    }

    private static double CalculateContextContinuity(List<DocumentChunk> chunks)
    {
        if (chunks.Count < 2) return 1.0;

        // Measure logical flow between consecutive chunks
        var continuityScores = new List<double>();
        for (int i = 0; i < chunks.Count - 1; i++)
        {
            var score = MeasureContextualContinuity(chunks[i], chunks[i + 1]);
            continuityScores.Add(score);
        }

        return continuityScores.Average();
    }

    private static double CalculateReferenceIntegrity(List<DocumentChunk> chunks)
    {
        if (chunks.Count == 0) return 0.0;

        // Check for proper handling of references, citations, etc.
        var chunksWithReferences = chunks.Count(chunk =>
            chunk.Content.Contains("see") ||
            chunk.Content.Contains("refer") ||
            chunk.Content.Contains("above") ||
            chunk.Content.Contains("below"));

        return (double)chunksWithReferences / chunks.Count;
    }

    private static double CalculateMetadataRichness(List<DocumentChunk> chunks)
    {
        if (chunks.Count == 0) return 0.0;

        // Measure richness of chunk metadata
        var richChunks = chunks.Count(chunk =>
            chunk.Metadata != null &&
            !string.IsNullOrEmpty(chunk.Metadata.FileName) &&
            chunk.Metadata.FileSize > 0);

        return (double)richChunks / chunks.Count;
    }

    #endregion

    #region Helper Methods

    private static bool IsStopWord(string word)
    {
        var stopWords = new HashSet<string> { "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by", "is", "are", "was", "were", "be", "been", "have", "has", "had", "do", "does", "did" };
        return stopWords.Contains(word.ToLowerInvariant());
    }

    private static double CalculateSimilarity(string text1, string text2)
    {
        var words1 = text1.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.ToLowerInvariant()).ToHashSet();
        var words2 = text2.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.ToLowerInvariant()).ToHashSet();

        if (words1.Count == 0 && words2.Count == 0) return 1.0;
        if (words1.Count == 0 || words2.Count == 0) return 0.0;

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return union > 0 ? (double)intersection / union : 0.0;
    }

    private static double MeasureContextualContinuity(DocumentChunk chunk1, DocumentChunk chunk2)
    {
        // Simple heuristic based on content similarity and flow
        var similarity = CalculateSimilarity(chunk1.Content, chunk2.Content);

        // Bonus for logical connectors
        var hasConnectors = chunk2.Content.ToLowerInvariant().Any(c =>
            chunk2.Content.Contains("however") ||
            chunk2.Content.Contains("therefore") ||
            chunk2.Content.Contains("furthermore") ||
            chunk2.Content.Contains("moreover"));

        return Math.Min(1.0, similarity + (hasConnectors ? 0.2 : 0.0));
    }

    #endregion
}
