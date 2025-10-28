using System.Text.RegularExpressions;
using FileFlux.Domain;

namespace FileFlux.Infrastructure.Services;

/// <summary>
/// Analyzes the semantic coherence of chunks using embeddings.
/// </summary>
public class ChunkCoherenceAnalyzer : IChunkCoherenceAnalyzer
{
    private readonly ISemanticBoundaryDetector _boundaryDetector;

    public ChunkCoherenceAnalyzer(ISemanticBoundaryDetector? boundaryDetector = null)
    {
        _boundaryDetector = boundaryDetector ?? new SemanticBoundaryDetector();
    }

    public async Task<CoherenceAnalysisResult> AnalyzeCoherenceAsync(
        DocumentChunk chunk,
        IEmbeddingService embeddingService,
        CancellationToken cancellationToken = default)
    {
        if (chunk == null || string.IsNullOrWhiteSpace(chunk.Content))
        {
            return new CoherenceAnalysisResult
            {
                CoherenceScore = 0,
                Level = CohesionLevel.VeryLow,
                Issues = new List<CoherenceIssue>
                {
                    new() { Type = CoherenceIssueType.MissingContext, Description = "Empty or null chunk", Severity = IssueSeverity.High }
                }
            };
        }

        // Split chunk into sentences for analysis
        var sentences = SplitIntoSentences(chunk.Content);

        if (sentences.Count < 2)
        {
            // Single sentence or very short chunk
            return new CoherenceAnalysisResult
            {
                CoherenceScore = 0.8, // Assume reasonable coherence for single units
                IntraSentenceSimilarity = 1.0,
                SimilarityVariance = 0,
                Level = CohesionLevel.High,
                AnalyzedChunk = chunk
            };
        }

        // Generate embeddings for all sentences
        var embeddings = await embeddingService.GenerateBatchEmbeddingsAsync(
            sentences,
            EmbeddingPurpose.Analysis,
            cancellationToken);

        var embeddingArray = embeddings.ToArray();

        // Calculate pairwise similarities
        var similarities = CalculatePairwiseSimilarities(embeddingArray, embeddingService);

        // Calculate coherence metrics
        var avgSimilarity = similarities.Average();
        var variance = CalculateVariance(similarities, avgSimilarity);

        // Detect coherence issues
        var issues = DetectCoherenceIssues(sentences, similarities, avgSimilarity);

        // Calculate overall coherence score
        var coherenceScore = CalculateCoherenceScore(avgSimilarity, variance, issues);

        // Determine cohesion level
        var level = DetermineCohesionLevel(coherenceScore);

        // Generate suggestions
        var suggestions = GenerateSuggestions(issues, level, avgSimilarity);

        return new CoherenceAnalysisResult
        {
            CoherenceScore = coherenceScore,
            IntraSentenceSimilarity = avgSimilarity,
            SimilarityVariance = variance,
            Level = level,
            Issues = issues,
            Suggestions = suggestions,
            AnalyzedChunk = chunk
        };
    }

    public async Task<IEnumerable<CoherenceAnalysisResult>> AnalyzeBatchCoherenceAsync(
        IEnumerable<DocumentChunk> chunks,
        IEmbeddingService embeddingService,
        CancellationToken cancellationToken = default)
    {
        var results = new List<CoherenceAnalysisResult>();

        foreach (var chunk in chunks)
        {
            var result = await AnalyzeCoherenceAsync(chunk, embeddingService, cancellationToken);
            results.Add(result);
        }

        // Add comparative analysis
        if (results.Count > 1)
        {
            var avgCoherence = results.Average(r => r.CoherenceScore);
            var bestChunk = results.OrderByDescending(r => r.CoherenceScore).First();
            var worstChunk = results.OrderBy(r => r.CoherenceScore).First();

            foreach (var result in results)
            {
                if (result.CoherenceScore < avgCoherence * 0.8)
                {
                    result.Suggestions.Add($"This chunk's coherence ({result.CoherenceScore:F2}) is below average ({avgCoherence:F2}). Consider restructuring.");
                }
            }
        }

        return results;
    }

    public async Task<IEnumerable<ChunkBoundary>> SuggestBoundariesAsync(
        string content,
        IEmbeddingService embeddingService,
        ChunkingOptions options,
        CancellationToken cancellationToken = default)
    {
        var boundaries = new List<ChunkBoundary>();

        // Split content into paragraphs or sentences
        var segments = SplitIntoSegments(content, options.MaxChunkSize);

        // Detect semantic boundaries
        var boundaryPoints = await _boundaryDetector.DetectBoundariesAsync(
            segments,
            embeddingService,
            cancellationToken);

        // Convert boundary points to chunk boundaries
        int currentPosition = 0;
        int lastBoundaryIndex = -1;

        foreach (var point in boundaryPoints.OrderBy(p => p.SegmentIndex))
        {
            // Calculate positions
            int startPos = 0;
            for (int i = 0; i <= lastBoundaryIndex; i++)
            {
                startPos += segments[i].Length + 1; // +1 for separator
            }

            int endPos = startPos;
            for (int i = lastBoundaryIndex + 1; i <= point.SegmentIndex; i++)
            {
                endPos += segments[i].Length + 1;
            }

            // Create chunk boundary
            boundaries.Add(new ChunkBoundary
            {
                StartPosition = startPos,
                EndPosition = Math.Min(endPos, content.Length),
                CoherenceScore = 1.0 - point.Similarity, // Invert similarity for coherence
                Reason = $"{point.Type} boundary (confidence: {point.Confidence:F2})",
                ContentPreview = GetPreview(content, startPos, endPos)
            });

            lastBoundaryIndex = point.SegmentIndex;
        }

        // Add final boundary if needed
        if (lastBoundaryIndex < segments.Count - 1)
        {
            int startPos = 0;
            for (int i = 0; i <= lastBoundaryIndex; i++)
            {
                startPos += segments[i].Length + 1;
            }

            boundaries.Add(new ChunkBoundary
            {
                StartPosition = startPos,
                EndPosition = content.Length,
                CoherenceScore = 0.8, // Default for final chunk
                Reason = "End of content",
                ContentPreview = GetPreview(content, startPos, content.Length)
            });
        }

        return OptimizeBoundaries(boundaries, options);
    }

    private List<string> SplitIntoSentences(string text)
    {
        // Simple sentence splitting (can be improved with NLP libraries)
        var sentencePattern = @"(?<=[.!?])\s+(?=[A-Z])";
        var sentences = Regex.Split(text, sentencePattern)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        // If no sentences found, split by newlines
        if (sentences.Count == 0)
        {
            sentences = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }

        return sentences;
    }

    private List<string> SplitIntoSegments(string content, int targetSize)
    {
        var segments = new List<string>();
        var paragraphs = content.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length <= targetSize)
            {
                segments.Add(paragraph);
            }
            else
            {
                // Split large paragraphs into sentences
                var sentences = SplitIntoSentences(paragraph);
                var currentSegment = "";

                foreach (var sentence in sentences)
                {
                    if ((currentSegment + " " + sentence).Length <= targetSize)
                    {
                        currentSegment = string.IsNullOrEmpty(currentSegment)
                            ? sentence
                            : currentSegment + " " + sentence;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(currentSegment))
                        {
                            segments.Add(currentSegment);
                        }
                        currentSegment = sentence;
                    }
                }

                if (!string.IsNullOrEmpty(currentSegment))
                {
                    segments.Add(currentSegment);
                }
            }
        }

        return segments;
    }

    private List<double> CalculatePairwiseSimilarities(float[][] embeddings, IEmbeddingService embeddingService)
    {
        var similarities = new List<double>();

        for (int i = 0; i < embeddings.Length - 1; i++)
        {
            for (int j = i + 1; j < embeddings.Length; j++)
            {
                var similarity = embeddingService.CalculateSimilarity(embeddings[i], embeddings[j]);
                similarities.Add(similarity);
            }
        }

        return similarities;
    }

    private double CalculateVariance(List<double> values, double mean)
    {
        if (values.Count == 0) return 0;

        var sumOfSquares = values.Sum(v => Math.Pow(v - mean, 2));
        return Math.Sqrt(sumOfSquares / values.Count);
    }

    private List<CoherenceIssue> DetectCoherenceIssues(
        List<string> sentences,
        List<double> similarities,
        double avgSimilarity)
    {
        var issues = new List<CoherenceIssue>();

        // Check for very low similarities (topic shifts)
        int pairIndex = 0;
        for (int i = 0; i < sentences.Count - 1; i++)
        {
            for (int j = i + 1; j < sentences.Count; j++)
            {
                if (pairIndex < similarities.Count && similarities[pairIndex] < 0.3)
                {
                    issues.Add(new CoherenceIssue
                    {
                        Type = CoherenceIssueType.TopicShift,
                        Description = $"Potential topic shift between sentences {i + 1} and {j + 1}",
                        Position = i,
                        Severity = similarities[pairIndex] < 0.2 ? IssueSeverity.High : IssueSeverity.Medium
                    });
                }
                pairIndex++;
            }
        }

        // Check for incomplete thoughts (very short sentences at boundaries)
        if (sentences.First().Length < 20)
        {
            issues.Add(new CoherenceIssue
            {
                Type = CoherenceIssueType.IncompleteThought,
                Description = "Chunk starts with a very short sentence",
                Position = 0,
                Severity = IssueSeverity.Medium
            });
        }

        if (sentences.Last().Length < 20)
        {
            issues.Add(new CoherenceIssue
            {
                Type = CoherenceIssueType.IncompleteThought,
                Description = "Chunk ends with a very short sentence",
                Position = sentences.Count - 1,
                Severity = IssueSeverity.Medium
            });
        }

        // Check for broken references
        foreach (var sentence in sentences)
        {
            if (Regex.IsMatch(sentence, @"\b(this|that|these|those|it|they)\b", RegexOptions.IgnoreCase)
                && sentences.IndexOf(sentence) == 0)
            {
                issues.Add(new CoherenceIssue
                {
                    Type = CoherenceIssueType.BrokenReference,
                    Description = "First sentence contains pronouns that may reference missing context",
                    Position = 0,
                    Severity = IssueSeverity.Medium
                });
                break;
            }
        }

        return issues;
    }

    private double CalculateCoherenceScore(double avgSimilarity, double variance, List<CoherenceIssue> issues)
    {
        // Start with average similarity as base score
        double score = avgSimilarity;

        // Penalize high variance (inconsistent coherence)
        score *= (1.0 - variance * 0.5);

        // Penalize based on issues
        foreach (var issue in issues)
        {
            switch (issue.Severity)
            {
                case IssueSeverity.High:
                    score *= 0.8;
                    break;
                case IssueSeverity.Medium:
                    score *= 0.9;
                    break;
                case IssueSeverity.Low:
                    score *= 0.95;
                    break;
            }
        }

        return Math.Max(0, Math.Min(1, score));
    }

    private CohesionLevel DetermineCohesionLevel(double coherenceScore)
    {
        return coherenceScore switch
        {
            >= 0.8 => CohesionLevel.VeryHigh,
            >= 0.65 => CohesionLevel.High,
            >= 0.5 => CohesionLevel.Medium,
            >= 0.35 => CohesionLevel.Low,
            _ => CohesionLevel.VeryLow
        };
    }

    private List<string> GenerateSuggestions(List<CoherenceIssue> issues, CohesionLevel level, double avgSimilarity)
    {
        var suggestions = new List<string>();

        if (level <= CohesionLevel.Low)
        {
            suggestions.Add("Consider restructuring this chunk for better coherence");
        }

        if (avgSimilarity < 0.5)
        {
            suggestions.Add("The content appears to cover multiple topics. Consider splitting into separate chunks.");
        }

        var topicShifts = issues.Count(i => i.Type == CoherenceIssueType.TopicShift);
        if (topicShifts > 0)
        {
            suggestions.Add($"Found {topicShifts} potential topic shifts. Review chunk boundaries.");
        }

        var brokenRefs = issues.Any(i => i.Type == CoherenceIssueType.BrokenReference);
        if (brokenRefs)
        {
            suggestions.Add("Add context for pronouns and references at the beginning of the chunk.");
        }

        var incompleteThoughts = issues.Any(i => i.Type == CoherenceIssueType.IncompleteThought);
        if (incompleteThoughts)
        {
            suggestions.Add("Ensure complete thoughts at chunk boundaries.");
        }

        if (suggestions.Count == 0 && level >= CohesionLevel.High)
        {
            suggestions.Add("Chunk has good coherence. No major improvements needed.");
        }

        return suggestions;
    }

    private string GetPreview(string content, int start, int end)
    {
        var length = Math.Min(100, end - start);
        var preview = content.Substring(start, Math.Min(length, content.Length - start));

        if (preview.Length == 100)
        {
            preview = string.Concat(preview.AsSpan(0, preview.LastIndexOf(' ')), "...");
        }

        return preview.Replace("\n", " ").Trim();
    }

    private IEnumerable<ChunkBoundary> OptimizeBoundaries(List<ChunkBoundary> boundaries, ChunkingOptions options)
    {
        var optimized = new List<ChunkBoundary>();

        foreach (var boundary in boundaries)
        {
            var chunkSize = boundary.EndPosition - boundary.StartPosition;

            // Skip chunks that are too small
            if (chunkSize < options.MaxChunkSize * 0.3)
            {
                continue;
            }

            // Split chunks that are too large
            if (chunkSize > options.MaxChunkSize * 1.5)
            {
                var midPoint = boundary.StartPosition + chunkSize / 2;

                optimized.Add(new ChunkBoundary
                {
                    StartPosition = boundary.StartPosition,
                    EndPosition = midPoint,
                    CoherenceScore = boundary.CoherenceScore * 0.9,
                    Reason = boundary.Reason + " (split for size)",
                    ContentPreview = boundary.ContentPreview
                });

                optimized.Add(new ChunkBoundary
                {
                    StartPosition = midPoint,
                    EndPosition = boundary.EndPosition,
                    CoherenceScore = boundary.CoherenceScore * 0.9,
                    Reason = "Continuation (split for size)",
                    ContentPreview = "..."
                });
            }
            else
            {
                optimized.Add(boundary);
            }
        }

        return optimized;
    }
}
