using FileFlux;
using FileFlux.Domain;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// Phase 10: Boundary Quality Manager - Boundary Quality Enhancement
/// Goal: Improve from 14-77% to 80%+
/// </summary>
public class BoundaryQualityManager
{
    private static readonly Regex SentenceEndRegex = new(@"[.!?]+(?:\s|$)", RegexOptions.Compiled);
    private static readonly Regex ParagraphRegex = new(@"\n\s*\n+", RegexOptions.Compiled);
    private static readonly Regex HeaderRegex = new(@"^#{1,6}\s+.+$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ListItemRegex = new(@"^\s*[-*+]\s+|^\s*\d+\.\s+", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Evaluate and improve boundary quality
    /// </summary>
    public BoundaryQualityResult EvaluateAndImproveBoundary(string fullText, int proposedSplitPosition, ChunkingOptions options)
    {
        if (string.IsNullOrWhiteSpace(fullText) || proposedSplitPosition <= 0 || proposedSplitPosition >= fullText.Length)
        {
            return new BoundaryQualityResult
            {
                OriginalPosition = proposedSplitPosition,
                ImprovedPosition = proposedSplitPosition,
                QualityScore = 0.0,
                Reason = "Invalid split position"
            };
        }

        // Evaluate current position quality
        var currentQuality = EvaluateBoundaryQuality(fullText, proposedSplitPosition);

        // Find better boundary
        var improvedPosition = FindBetterBoundary(fullText, proposedSplitPosition, options);
        var improvedQuality = EvaluateBoundaryQuality(fullText, improvedPosition);

        return new BoundaryQualityResult
        {
            OriginalPosition = proposedSplitPosition,
            ImprovedPosition = improvedPosition,
            OriginalQualityScore = currentQuality.Score,
            QualityScore = improvedQuality.Score,
            Reason = improvedQuality.Reason,
            BoundaryType = improvedQuality.BoundaryType
        };
    }

    /// <summary>
    /// Evaluate boundary quality
    /// </summary>
    private BoundaryEvaluation EvaluateBoundaryQuality(string text, int position)
    {
        var scores = new List<(double score, string reason, BoundaryType type)>();

        // 1. Sentence boundary quality
        var sentenceScore = EvaluateSentenceBoundary(text, position);
        scores.Add(sentenceScore);

        // 2. Paragraph boundary quality
        var paragraphScore = EvaluateParagraphBoundary(text, position);
        scores.Add(paragraphScore);

        // 3. Structural boundary quality
        var structuralScore = EvaluateStructuralBoundary(text, position);
        scores.Add(structuralScore);

        // 4. Semantic coherence quality
        var semanticScore = EvaluateSemanticCoherence(text, position);
        scores.Add(semanticScore);

        // Select best score
        var best = scores.OrderByDescending(s => s.score).First();

        return new BoundaryEvaluation
        {
            Score = best.score,
            Reason = best.reason,
            BoundaryType = best.type
        };
    }

    /// <summary>
    /// Evaluate sentence boundary
    /// </summary>
    private (double score, string reason, BoundaryType type) EvaluateSentenceBoundary(string text, int position)
    {
        // Extract context around position
        var contextStart = Math.Max(0, position - 100);
        var contextEnd = Math.Min(text.Length, position + 100);
        var context = text.Substring(contextStart, contextEnd - contextStart);
        var relativePosition = position - contextStart;

        // Find sentence ends
        var sentenceEnds = SentenceEndRegex.Matches(context);
        if (sentenceEnds.Count == 0)
            return (0.3, "No sentence boundary found", BoundaryType.None);

        // Find closest sentence end
        var closestEnd = sentenceEnds
            .Cast<Match>()
            .Select(m => new { Match = m, Distance = Math.Abs(m.Index + m.Length - relativePosition) })
            .OrderBy(x => x.Distance)
            .First();

        // Calculate distance-based score
        var distance = closestEnd.Distance;
        var score = distance switch
        {
            0 => 1.0,      // Exactly at sentence end
            < 5 => 0.9,    // Very close
            < 10 => 0.7,   // Close
            < 20 => 0.5,   // Medium
            _ => 0.3       // Far
        };

        return (score, $"Sentence boundary at distance {distance}", BoundaryType.Sentence);
    }

    /// <summary>
    /// Evaluate paragraph boundary
    /// </summary>
    private (double score, string reason, BoundaryType type) EvaluateParagraphBoundary(string text, int position)
    {
        // Find paragraph separators
        var paragraphs = ParagraphRegex.Matches(text);
        if (paragraphs.Count == 0)
            return (0.2, "No paragraph boundary found", BoundaryType.None);

        // Find closest paragraph boundary
        var closestParagraph = paragraphs
            .Cast<Match>()
            .Select(m => new { Match = m, Distance = Math.Abs(m.Index - position) })
            .OrderBy(x => x.Distance)
            .First();

        var distance = closestParagraph.Distance;
        var score = distance switch
        {
            0 => 1.0,      // Exactly at paragraph boundary
            < 10 => 0.8,   // Very close
            < 30 => 0.6,   // Close
            < 50 => 0.4,   // Medium
            _ => 0.2       // Far
        };

        return (score, $"Paragraph boundary at distance {distance}", BoundaryType.Paragraph);
    }

    /// <summary>
    /// Evaluate structural boundary
    /// </summary>
    private (double score, string reason, BoundaryType type) EvaluateStructuralBoundary(string text, int position)
    {
        var scores = new List<(double score, string reason, BoundaryType type)>();

        // Header boundaries
        var headers = HeaderRegex.Matches(text);
        if (headers.Count > 0)
        {
            var closestHeader = headers
                .Cast<Match>()
                .Select(m => new { Match = m, Distance = Math.Abs(m.Index - position) })
                .OrderBy(x => x.Distance)
                .First();

            var headerScore = closestHeader.Distance switch
            {
                0 => 1.0,
                < 5 => 0.9,
                < 20 => 0.7,
                _ => 0.3
            };

            scores.Add((headerScore, $"Header boundary at distance {closestHeader.Distance}", BoundaryType.Header));
        }

        // List boundaries
        var listItems = ListItemRegex.Matches(text);
        if (listItems.Count > 0)
        {
            // Detect list start/end
            var listBoundaries = FindListBoundaries(text, listItems);
            var closestList = listBoundaries
                .Select(b => new { Position = b, Distance = Math.Abs(b - position) })
                .OrderBy(x => x.Distance)
                .FirstOrDefault();

            if (closestList != null)
            {
                var listScore = closestList.Distance switch
                {
                    0 => 0.9,
                    < 10 => 0.7,
                    < 30 => 0.5,
                    _ => 0.2
                };

                scores.Add((listScore, $"List boundary at distance {closestList.Distance}", BoundaryType.List));
            }
        }

        // Table boundaries
        if (text.Contains('|'))
        {
            var tableBoundaries = FindTableBoundaries(text);
            var closestTable = tableBoundaries
                .Select(b => new { Position = b, Distance = Math.Abs(b - position) })
                .OrderBy(x => x.Distance)
                .FirstOrDefault();

            if (closestTable != null)
            {
                var tableScore = closestTable.Distance switch
                {
                    0 => 0.95,
                    < 5 => 0.8,
                    < 20 => 0.6,
                    _ => 0.3
                };

                scores.Add((tableScore, $"Table boundary at distance {closestTable.Distance}", BoundaryType.Table));
            }
        }

        if (scores.Count == 0)
            return (0.3, "No structural boundary found", BoundaryType.None);

        return scores.OrderByDescending(s => s.score).First();
    }

    /// <summary>
    /// Evaluate semantic coherence
    /// </summary>
    private (double score, string reason, BoundaryType type) EvaluateSemanticCoherence(string text, int position)
    {
        // Text before and after split
        var beforeText = text.Substring(Math.Max(0, position - 200), Math.Min(200, position));
        var afterText = text.Substring(position, Math.Min(200, text.Length - position));

        // Keyword continuity
        var beforeKeywords = ExtractKeywords(beforeText);
        var afterKeywords = ExtractKeywords(afterText);

        var commonKeywords = beforeKeywords.Intersect(afterKeywords, StringComparer.OrdinalIgnoreCase).Count();
        var totalKeywords = beforeKeywords.Union(afterKeywords, StringComparer.OrdinalIgnoreCase).Count();

        var continuity = totalKeywords > 0 ? (double)commonKeywords / totalKeywords : 0.0;

        // Too high continuity indicates poor split point
        var score = continuity switch
        {
            > 0.7 => 0.3,  // Too high continuity - bad split point
            > 0.5 => 0.5,  // High continuity
            > 0.3 => 0.7,  // Moderate continuity
            > 0.1 => 0.9,  // Good split point
            _ => 1.0       // Completely independent - optimal split
        };

        return (score, $"Semantic continuity: {continuity:F2}", BoundaryType.Semantic);
    }

    /// <summary>
    /// Find better boundary
    /// </summary>
    private int FindBetterBoundary(string text, int currentPosition, ChunkingOptions options)
    {
        var searchRadius = Math.Min(200, options.MaxChunkSize / 10);
        var candidatePositions = new List<(int position, double score)>();

        // Include current position
        var currentEval = EvaluateBoundaryQuality(text, currentPosition);
        candidatePositions.Add((currentPosition, currentEval.Score));

        // Search surrounding area
        for (int offset = -searchRadius; offset <= searchRadius; offset += 10)
        {
            if (offset == 0) continue;

            var candidatePosition = currentPosition + offset;
            if (candidatePosition < 0 || candidatePosition >= text.Length)
                continue;

            var eval = EvaluateBoundaryQuality(text, candidatePosition);
            candidatePositions.Add((candidatePosition, eval.Score));

            // Early exit if perfect boundary found
            if (eval.Score >= 0.95)
            {
                return candidatePosition;
            }
        }

        // Fine-grained search around high scores
        var topCandidates = candidatePositions
            .OrderByDescending(c => c.score)
            .Take(3)
            .ToList();

        foreach (var candidate in topCandidates)
        {
            for (int microOffset = -5; microOffset <= 5; microOffset++)
            {
                if (microOffset == 0) continue;

                var microPosition = candidate.position + microOffset;
                if (microPosition < 0 || microPosition >= text.Length)
                    continue;

                var microEval = EvaluateBoundaryQuality(text, microPosition);
                candidatePositions.Add((microPosition, microEval.Score));
            }
        }

        // Return highest scoring position
        return candidatePositions
            .OrderByDescending(c => c.score)
            .First()
            .position;
    }

    /// <summary>
    /// Find list boundaries
    /// </summary>
    private List<int> FindListBoundaries(string text, MatchCollection listItems)
    {
        var boundaries = new List<int>();
        var lines = text.Split('\n');
        var linePositions = CalculateLinePositions(text);

        bool inList = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var isListItem = ListItemRegex.IsMatch(lines[i]);

            if (!inList && isListItem)
            {
                // List start
                boundaries.Add(linePositions[i]);
                inList = true;
            }
            else if (inList && !isListItem && !string.IsNullOrWhiteSpace(lines[i]))
            {
                // List end
                boundaries.Add(linePositions[i]);
                inList = false;
            }
        }

        return boundaries;
    }

    /// <summary>
    /// Find table boundaries
    /// </summary>
    private List<int> FindTableBoundaries(string text)
    {
        var boundaries = new List<int>();
        var lines = text.Split('\n');
        var linePositions = CalculateLinePositions(text);

        bool inTable = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var isTableRow = lines[i].Contains('|') && lines[i].Count(c => c == '|') >= 2;

            if (!inTable && isTableRow)
            {
                // Table start
                boundaries.Add(linePositions[i]);
                inTable = true;
            }
            else if (inTable && !isTableRow && !string.IsNullOrWhiteSpace(lines[i]))
            {
                // Table end
                boundaries.Add(linePositions[i]);
                inTable = false;
            }
        }

        return boundaries;
    }

    /// <summary>
    /// Calculate line positions
    /// </summary>
    private List<int> CalculateLinePositions(string text)
    {
        var positions = new List<int> { 0 };
        var currentPosition = 0;

        foreach (var line in text.Split('\n').Skip(1))
        {
            currentPosition += line.Length + 1; // +1 for newline
            positions.Add(currentPosition);
        }

        return positions;
    }

    /// <summary>
    /// Extract keywords
    /// </summary>
    private HashSet<string> ExtractKeywords(string text)
    {
        return text.ToLowerInvariant()
            .Split(new[] { ' ', '\n', '\r', '\t', '.', ',', ';', ':', '!', '?' },
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .Where(w => !IsStopWord(w))
            .ToHashSet();
    }

    /// <summary>
    /// Check if word is a stop word
    /// </summary>
    private bool IsStopWord(string word)
    {
        var stopWords = new HashSet<string>
        {
            "the", "and", "or", "but", "in", "on", "at", "to", "for",
            "of", "with", "by", "from", "as", "is", "was", "are", "were",
            "been", "being", "have", "has", "had", "do", "does", "did",
            "will", "would", "could", "should", "may", "might", "must",
            "can", "shall", "a", "an"
        };

        return stopWords.Contains(word.ToLowerInvariant());
    }

    /// <summary>
    /// Boundary evaluation result
    /// </summary>
    private class BoundaryEvaluation
    {
        public double Score { get; set; }
        public string Reason { get; set; } = string.Empty;
        public BoundaryType BoundaryType { get; set; }
    }
}

/// <summary>
/// Boundary quality result
/// </summary>
public class BoundaryQualityResult
{
    public int OriginalPosition { get; set; }
    public int ImprovedPosition { get; set; }
    public double OriginalQualityScore { get; set; }
    public double QualityScore { get; set; }
    public string Reason { get; set; } = string.Empty;
    public BoundaryType BoundaryType { get; set; }
}

/// <summary>
/// Boundary type
/// </summary>
public enum BoundaryType
{
    None,
    Sentence,
    Paragraph,
    Header,
    List,
    Table,
    Semantic
}
