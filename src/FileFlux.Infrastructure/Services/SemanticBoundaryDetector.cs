namespace FileFlux.Infrastructure.Services;

/// <summary>
/// Implementation of semantic boundary detection using embeddings.
/// </summary>
public class SemanticBoundaryDetector : ISemanticBoundaryDetector
{
    private double _similarityThreshold = 0.7;

    public double SimilarityThreshold
    {
        get => _similarityThreshold;
        set => _similarityThreshold = Math.Max(0, Math.Min(1, value));
    }

    public async Task<BoundaryDetectionResult> DetectBoundaryAsync(
        string segment1,
        string segment2,
        IEmbeddingService? embeddingService = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(segment1) || string.IsNullOrWhiteSpace(segment2))
        {
            return new BoundaryDetectionResult
            {
                IsBoundary = true,
                Similarity = 0,
                Confidence = 1.0,
                Type = BoundaryType.Section
            };
        }

        // If no embedding service available, fall back to text-based similarity
        if (embeddingService == null)
        {
            return await DetectBoundaryWithTextSimilarityAsync(segment1, segment2, cancellationToken);
        }

        try
        {
            // Generate embeddings for both segments
            var embeddings = await embeddingService.GenerateBatchEmbeddingsAsync(
                new[] { segment1, segment2 },
                EmbeddingPurpose.Analysis,
                cancellationToken);

            var embeddingArray = embeddings.ToArray();
            if (embeddingArray.Length != 2)
            {
                // Fall back to text-based similarity if embedding generation fails
                return await DetectBoundaryWithTextSimilarityAsync(segment1, segment2, cancellationToken);
            }

            // Calculate similarity
            var similarity = embeddingService.CalculateSimilarity(embeddingArray[0], embeddingArray[1]);

            // Determine boundary based on similarity threshold
            var isBoundary = similarity < _similarityThreshold;

            // Calculate confidence based on distance from threshold
            var confidence = Math.Abs(similarity - _similarityThreshold) / _similarityThreshold;
            confidence = Math.Min(1.0, confidence * 2); // Scale up for clarity

            // Determine boundary type based on similarity and content analysis
            var boundaryType = DetermineBoundaryType(segment1, segment2, similarity);

            return new BoundaryDetectionResult
            {
                IsBoundary = isBoundary,
                Similarity = similarity,
                Confidence = confidence,
                Type = boundaryType,
                Metadata = new Dictionary<string, object>
                {
                    ["Threshold"] = _similarityThreshold,
                    ["Segment1Length"] = segment1.Length,
                    ["Segment2Length"] = segment2.Length,
                    ["Method"] = "Embedding-based"
                }
            };
        }
        catch (Exception)
        {
            // Fall back to text-based similarity if any exception occurs
            return await DetectBoundaryWithTextSimilarityAsync(segment1, segment2, cancellationToken);
        }
    }

    public async Task<IEnumerable<BoundaryPoint>> DetectBoundariesAsync(
        IList<string> segments,
        IEmbeddingService? embeddingService = null,
        CancellationToken cancellationToken = default)
    {
        if (segments == null || segments.Count < 2)
        {
            return Enumerable.Empty<BoundaryPoint>();
        }

        // If no embedding service available, fall back to text-based analysis
        if (embeddingService == null)
        {
            return await DetectBoundariesWithTextSimilarityAsync(segments, cancellationToken);
        }

        try
        {
            var boundaries = new List<BoundaryPoint>();

            // Generate embeddings for all segments in batch
            var embeddings = await embeddingService.GenerateBatchEmbeddingsAsync(
                segments,
                EmbeddingPurpose.Analysis,
                cancellationToken);

            var embeddingArray = embeddings.ToArray();

            // Calculate similarities between consecutive segments
            for (int i = 0; i < embeddingArray.Length - 1; i++)
            {
                var similarity = embeddingService.CalculateSimilarity(
                    embeddingArray[i],
                    embeddingArray[i + 1]);

                if (similarity < _similarityThreshold)
                {
                    var confidence = Math.Min(1.0, Math.Abs(similarity - _similarityThreshold) * 2);
                    var boundaryType = DetermineBoundaryType(segments[i], segments[i + 1], similarity);

                    boundaries.Add(new BoundaryPoint
                    {
                        SegmentIndex = i,
                        Similarity = similarity,
                        Confidence = confidence,
                        Type = boundaryType
                    });
                }
            }

            // Post-process boundaries to merge nearby boundaries and adjust confidence
            return PostProcessBoundaries(boundaries, segments);
        }
        catch (Exception)
        {
            // Fall back to text-based analysis if embedding operations fail
            return await DetectBoundariesWithTextSimilarityAsync(segments, cancellationToken);
        }
    }

    private BoundaryType DetermineBoundaryType(string segment1, string segment2, double similarity)
    {
        // Check for structural indicators first (priority)
        if (ContainsHeading(segment2))
        {
            return BoundaryType.Section;
        }

        if (ContainsCodeBlock(segment1) || ContainsCodeBlock(segment2))
        {
            return BoundaryType.CodeBlock;
        }

        if (ContainsTable(segment1) || ContainsTable(segment2))
        {
            return BoundaryType.Table;
        }

        if (ContainsList(segment1) || ContainsList(segment2))
        {
            return BoundaryType.List;
        }

        // Very low similarity indicates major topic change (after structural checks)
        if (similarity < 0.3)
        {
            return BoundaryType.TopicChange;
        }

        // Check paragraph boundaries
        if (segment1.EndsWith(".") && char.IsUpper(segment2.FirstOrDefault()))
        {
            return similarity < 0.5 ? BoundaryType.Paragraph : BoundaryType.Sentence;
        }

        // Default based on similarity
        return similarity < 0.5 ? BoundaryType.TopicChange : BoundaryType.Paragraph;
    }

    private IEnumerable<BoundaryPoint> PostProcessBoundaries(
        List<BoundaryPoint> boundaries, 
        IList<string> segments)
    {
        if (boundaries.Count == 0)
        {
            return boundaries;
        }

        var processed = new List<BoundaryPoint>();
        BoundaryPoint? lastBoundary = null;

        foreach (var boundary in boundaries)
        {
            // Merge nearby boundaries (within 2 segments)
            if (lastBoundary != null && boundary.SegmentIndex - lastBoundary.SegmentIndex <= 2)
            {
                // Keep the stronger boundary
                if (boundary.Confidence > lastBoundary.Confidence)
                {
                    processed[processed.Count - 1] = boundary;
                    lastBoundary = boundary;
                }
            }
            else
            {
                processed.Add(boundary);
                lastBoundary = boundary;
            }
        }

        // Adjust confidence based on segment lengths
        foreach (var boundary in processed)
        {
            var segmentLength = segments[boundary.SegmentIndex].Length;
            var nextSegmentLength = boundary.SegmentIndex + 1 < segments.Count 
                ? segments[boundary.SegmentIndex + 1].Length 
                : 0;

            // Boost confidence for boundaries between segments of very different lengths
            var lengthRatio = Math.Min(segmentLength, nextSegmentLength) / 
                             (double)Math.Max(segmentLength, nextSegmentLength);
            
            if (lengthRatio < 0.3)
            {
                boundary.Confidence = Math.Min(1.0, boundary.Confidence * 1.2);
            }
        }

        return processed;
    }

    private bool ContainsHeading(string text)
    {
        return text.StartsWith("#") || 
               text.Contains("HEADING_START") ||
               System.Text.RegularExpressions.Regex.IsMatch(text, @"^(Chapter|Section|\d+\.)\s+", 
                   System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private bool ContainsCodeBlock(string text)
    {
        return text.Contains("```") || 
               text.Contains("CODE_BLOCK_START") ||
               text.Contains("CODE_START");
    }

    private bool ContainsTable(string text)
    {
        return text.Contains("TABLE_START") || 
               text.Contains("|") && text.Count(c => c == '|') > 2;
    }

    private bool ContainsList(string text)
    {
        return text.Contains("LIST_START") ||
               System.Text.RegularExpressions.Regex.IsMatch(text, @"^\s*[-*+•]\s+",
                   System.Text.RegularExpressions.RegexOptions.Multiline) ||
               System.Text.RegularExpressions.Regex.IsMatch(text, @"^\s*\d+\.\s+",
                   System.Text.RegularExpressions.RegexOptions.Multiline);
    }

    /// <summary>
    /// Fallback method for boundary detection using text-based similarity when embeddings are not available
    /// </summary>
    private Task<BoundaryDetectionResult> DetectBoundaryWithTextSimilarityAsync(
        string segment1,
        string segment2,
        CancellationToken cancellationToken = default)
    {
        // Calculate text-based similarity using word overlap
        var similarity = CalculateTextSimilarity(segment1, segment2);

        // Determine boundary based on similarity threshold (adjusted for text-based analysis)
        var adjustedThreshold = _similarityThreshold * 0.8; // Lower threshold for text-based
        var isBoundary = similarity < adjustedThreshold;

        // Calculate confidence
        var confidence = Math.Abs(similarity - adjustedThreshold) / adjustedThreshold;
        confidence = Math.Min(1.0, confidence * 1.5);

        // Determine boundary type
        var boundaryType = DetermineBoundaryType(segment1, segment2, similarity);

        var result = new BoundaryDetectionResult
        {
            IsBoundary = isBoundary,
            Similarity = similarity,
            Confidence = confidence,
            Type = boundaryType,
            Metadata = new Dictionary<string, object>
            {
                ["Threshold"] = adjustedThreshold,
                ["Segment1Length"] = segment1.Length,
                ["Segment2Length"] = segment2.Length,
                ["Method"] = "Text-based (fallback)"
            }
        };

        return Task.FromResult(result);
    }

    /// <summary>
    /// Fallback method for detecting multiple boundaries using text-based analysis
    /// </summary>
    private Task<IEnumerable<BoundaryPoint>> DetectBoundariesWithTextSimilarityAsync(
        IList<string> segments,
        CancellationToken cancellationToken = default)
    {
        var boundaries = new List<BoundaryPoint>();
        var adjustedThreshold = _similarityThreshold * 0.8; // Lower threshold for text-based

        // Calculate similarities between consecutive segments
        for (int i = 0; i < segments.Count - 1; i++)
        {
            var similarity = CalculateTextSimilarity(segments[i], segments[i + 1]);

            if (similarity < adjustedThreshold)
            {
                var confidence = Math.Min(1.0, Math.Abs(similarity - adjustedThreshold) * 1.5);
                var boundaryType = DetermineBoundaryType(segments[i], segments[i + 1], similarity);

                boundaries.Add(new BoundaryPoint
                {
                    SegmentIndex = i,
                    Similarity = similarity,
                    Confidence = confidence,
                    Type = boundaryType
                });
            }
        }

        // Post-process boundaries
        var processedBoundaries = PostProcessBoundaries(boundaries, segments);
        return Task.FromResult(processedBoundaries);
    }

    /// <summary>
    /// Calculate text-based similarity using word overlap (Jaccard similarity)
    /// </summary>
    private double CalculateTextSimilarity(string text1, string text2)
    {
        if (string.IsNullOrWhiteSpace(text1) && string.IsNullOrWhiteSpace(text2))
            return 1.0;

        if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            return 0.0;

        // Extract words and normalize
        var words1 = text1.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2) // Filter out very short words
            .ToHashSet();

        var words2 = text2.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .ToHashSet();

        if (!words1.Any() && !words2.Any())
            return 1.0;

        if (!words1.Any() || !words2.Any())
            return 0.0;

        // Calculate Jaccard similarity
        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return union > 0 ? (double)intersection / union : 0.0;
    }
}