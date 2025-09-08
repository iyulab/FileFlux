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
        IEmbeddingService embeddingService,
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

        // Generate embeddings for both segments
        var embeddings = await embeddingService.GenerateBatchEmbeddingsAsync(
            new[] { segment1, segment2 },
            EmbeddingPurpose.Analysis,
            cancellationToken);

        var embeddingArray = embeddings.ToArray();
        if (embeddingArray.Length != 2)
        {
            throw new InvalidOperationException("Failed to generate embeddings for both segments");
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

    public async Task<IEnumerable<BoundaryPoint>> DetectBoundariesAsync(
        IList<string> segments,
        IEmbeddingService embeddingService,
        CancellationToken cancellationToken = default)
    {
        if (segments == null || segments.Count < 2)
        {
            return Enumerable.Empty<BoundaryPoint>();
        }

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

    private BoundaryType DetermineBoundaryType(string segment1, string segment2, double similarity)
    {
        // Very low similarity indicates major topic change
        if (similarity < 0.3)
        {
            return BoundaryType.TopicChange;
        }

        // Check for structural indicators
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
}