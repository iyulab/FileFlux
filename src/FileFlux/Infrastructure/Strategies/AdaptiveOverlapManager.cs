using FileFlux;
using FileFlux.Domain;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// Phase 10: Adaptive Overlap Manager - Context Preservation Enhancement
/// Goal: Improve from 37-52% to 75%+ 
/// </summary>
public class AdaptiveOverlapManager
{
    private static readonly Regex SentenceEndRegex = new(@"[.!?]+(?:\s|$)", RegexOptions.Compiled);
    private static readonly Regex ParagraphRegex = new(@"\n\s*\n+", RegexOptions.Compiled);
    private static readonly Regex HeaderRegex = new(@"^#{1,6}\s+.+$", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Calculate optimal overlap size between chunks
    /// Dynamically determines overlap based on document structure and content complexity
    /// </summary>
    public int CalculateOptimalOverlap(string previousChunk, string currentChunk, ChunkingOptions options)
    {
        if (string.IsNullOrWhiteSpace(previousChunk) || string.IsNullOrWhiteSpace(currentChunk))
            return options.OverlapSize;

        // Base overlap size
        var baseOverlap = options.OverlapSize > 0 ? options.OverlapSize : 100;

        // 1. Context complexity adjustment
        var complexityFactor = CalculateContextComplexity(previousChunk, currentChunk);

        // 2. Structural boundary detection adjustment
        var structuralFactor = DetectStructuralBoundaryType(previousChunk, currentChunk);

        // 3. Semantic continuity adjustment
        var semanticFactor = CalculateSemanticContinuity(previousChunk, currentChunk);

        // Calculate adaptive overlap
        var adaptiveOverlap = (int)(baseOverlap * complexityFactor * structuralFactor * semanticFactor);

        // Apply min/max constraints
        var minOverlap = Math.Max(50, baseOverlap / 2);
        var maxOverlap = Math.Min(options.MaxChunkSize / 4, baseOverlap * 3);

        return Math.Max(minOverlap, Math.Min(maxOverlap, adaptiveOverlap));
    }

    /// <summary>
    /// Create context-preserving overlap text
    /// Recognizes sentence/paragraph boundaries for natural overlap
    /// </summary>
    public string CreateContextPreservingOverlap(string previousChunk, int overlapSize)
    {
        if (string.IsNullOrWhiteSpace(previousChunk) || overlapSize <= 0)
            return string.Empty;

        // Split into sentences
        var sentences = ExtractSentences(previousChunk);
        if (sentences.Count == 0)
            return string.Empty;

        // Find last paragraph start
        var lastParagraphStart = FindLastParagraphStart(previousChunk);
        var relevantText = lastParagraphStart >= 0
            ? previousChunk.Substring(lastParagraphStart)
            : previousChunk;

        // Extract meaningful last portion
        var overlapText = ExtractMeaningfulOverlap(relevantText, overlapSize, sentences);

        // Include header if present
        var lastHeader = ExtractLastHeader(previousChunk);
        if (!string.IsNullOrEmpty(lastHeader))
        {
            overlapText = lastHeader + "\n" + overlapText;
        }

        return overlapText.Trim();
    }

    /// <summary>
    /// Calculate context complexity
    /// </summary>
    private double CalculateContextComplexity(string previousChunk, string currentChunk)
    {
        var prevComplexity = CalculateTextComplexity(previousChunk);
        var currComplexity = CalculateTextComplexity(currentChunk);

        // Higher complexity requires more overlap
        var avgComplexity = (prevComplexity + currComplexity) / 2;

        return avgComplexity switch
        {
            < 0.3 => 0.8,  // Simple text: less overlap
            < 0.6 => 1.0,  // Medium complexity: base overlap
            < 0.8 => 1.3,  // Complex text: more overlap
            _ => 1.5       // Very complex: maximum overlap
        };
    }

    /// <summary>
    /// Measure text complexity
    /// </summary>
    private double CalculateTextComplexity(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0.0;

        var sentences = ExtractSentences(text);
        if (sentences.Count == 0)
            return 0.0;

        // Average sentence length
        var avgSentenceLength = sentences.Average(s => s.Split(' ').Length);

        // Technical term density
        var technicalTermDensity = CountTechnicalTerms(text) / (double)sentences.Count;

        // Structural element density
        var structuralDensity = CountStructuralElements(text) / (double)sentences.Count;

        // Complexity score (0.0 ~ 1.0)
        var complexity = (avgSentenceLength / 30.0) * 0.4 +  // Complex if > 30 words
                        (technicalTermDensity / 5.0) * 0.3 +   // Complex if > 5 terms per sentence
                        (structuralDensity / 2.0) * 0.3;       // Complex if > 2 structural elements

        return Math.Min(1.0, complexity);
    }

    /// <summary>
    /// Detect structural boundary type
    /// </summary>
    private double DetectStructuralBoundaryType(string previousChunk, string currentChunk)
    {
        var prevEndsWithHeader = HeaderRegex.IsMatch(previousChunk.TrimEnd());
        var currStartsWithHeader = HeaderRegex.IsMatch(currentChunk.TrimStart());

        // Header boundary: reduce overlap
        if (prevEndsWithHeader || currStartsWithHeader)
            return 0.6;

        var prevEndsWithList = IsListEnding(previousChunk);
        var currStartsWithList = IsListBeginning(currentChunk);

        // List continuation: increase overlap
        if (prevEndsWithList && currStartsWithList)
            return 1.4;

        var prevEndsWithTable = IsTableEnding(previousChunk);
        var currStartsWithTable = IsTableBeginning(currentChunk);

        // Table boundary: minimize overlap
        if (prevEndsWithTable || currStartsWithTable)
            return 0.5;

        // Regular text: base overlap
        return 1.0;
    }

    /// <summary>
    /// Calculate semantic continuity
    /// </summary>
    private double CalculateSemanticContinuity(string previousChunk, string currentChunk)
    {
        // Extract last and first sentences
        var prevSentences = ExtractSentences(previousChunk);
        var currSentences = ExtractSentences(currentChunk);

        if (prevSentences.Count == 0 || currSentences.Count == 0)
            return 1.0;

        var lastSentence = prevSentences.Last();
        var firstSentence = currSentences.First();

        // Common keyword ratio
        var commonKeywords = CalculateCommonKeywordRatio(lastSentence, firstSentence);

        // Pronoun/reference detection
        var hasReference = DetectReference(firstSentence);

        // High continuity: increase overlap
        if (commonKeywords > 0.3 || hasReference)
            return 1.3;

        // Low continuity: decrease overlap
        if (commonKeywords < 0.1)
            return 0.8;

        return 1.0;
    }

    /// <summary>
    /// Extract meaningful overlap
    /// </summary>
    private string ExtractMeaningfulOverlap(string text, int targetSize, List<string> sentences)
    {
        if (sentences.Count == 0)
            return text.Length > targetSize ? text.Substring(text.Length - targetSize) : text;

        var overlapSentences = new List<string>();
        var currentSize = 0;

        // Add sentences from the end
        for (int i = sentences.Count - 1; i >= 0; i--)
        {
            var sentence = sentences[i];
            var sentenceSize = sentence.Length;

            if (currentSize + sentenceSize > targetSize * 1.5) // Allow 50% overshoot
                break;

            overlapSentences.Insert(0, sentence);
            currentSize += sentenceSize;

            if (currentSize >= targetSize)
                break;
        }

        // Include at least one sentence
        if (overlapSentences.Count == 0 && sentences.Count > 0)
        {
            overlapSentences.Add(sentences.Last());
        }

        return string.Join(" ", overlapSentences);
    }

    /// <summary>
    /// Find last paragraph start position
    /// </summary>
    private int FindLastParagraphStart(string text)
    {
        var matches = ParagraphRegex.Matches(text);
        if (matches.Count == 0)
            return -1;

        var lastMatch = matches[matches.Count - 1];
        return lastMatch.Index + lastMatch.Length;
    }

    /// <summary>
    /// Extract last header
    /// </summary>
    private string ExtractLastHeader(string text)
    {
        var matches = HeaderRegex.Matches(text);
        if (matches.Count == 0)
            return string.Empty;

        return matches[matches.Count - 1].Value.Trim();
    }

    /// <summary>
    /// Extract sentences
    /// </summary>
    private List<string> ExtractSentences(string text)
    {
        return SentenceEndRegex.Split(text)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Where(s => s.Length > 10)
            .ToList();
    }

    /// <summary>
    /// Count technical terms
    /// </summary>
    private int CountTechnicalTerms(string text)
    {
        var patterns = new[]
        {
            @"\b[A-Z]{2,}\b",  // Abbreviations
            @"\b\w+\(\)",      // Function calls
            @"\b\w+\.\w+",     // Namespaces
            @"\b(?:class|function|method|interface|enum)\b"  // Programming keywords
        };

        return patterns.Sum(pattern => Regex.Matches(text, pattern, RegexOptions.IgnoreCase).Count);
    }

    /// <summary>
    /// Count structural elements
    /// </summary>
    private int CountStructuralElements(string text)
    {
        var count = 0;
        count += HeaderRegex.Matches(text).Count;
        count += Regex.Matches(text, @"^\s*[-*+]\s+", RegexOptions.Multiline).Count;
        count += Regex.Matches(text, @"^\s*\d+\.\s+", RegexOptions.Multiline).Count;
        count += text.Split('\n').Count(line => line.Contains('|') && line.Count(c => c == '|') >= 2);
        return count;
    }

    /// <summary>
    /// Detect list ending
    /// </summary>
    private bool IsListEnding(string text)
    {
        var lines = text.Split('\n');
        var lastLines = lines.Skip(Math.Max(0, lines.Length - 3));
        return lastLines.Any(line => line != null && Regex.IsMatch(line.Trim(), @"^[-*+]\s+|^\d+\.\s+"));
    }

    /// <summary>
    /// Detect list beginning
    /// </summary>
    private bool IsListBeginning(string text)
    {
        var lines = text.Split('\n').Take(3);
        return lines.Any(line => line != null && Regex.IsMatch(line.Trim(), @"^[-*+]\s+|^\d+\.\s+"));
    }

    /// <summary>
    /// Detect table ending
    /// </summary>
    private bool IsTableEnding(string text)
    {
        var lines = text.Split('\n');
        var lastLines = lines.Skip(Math.Max(0, lines.Length - 3));
        return lastLines.Any(line => line != null && line.Contains('|') && line.Count(c => c == '|') >= 2);
    }

    /// <summary>
    /// Detect table beginning
    /// </summary>
    private bool IsTableBeginning(string text)
    {
        var lines = text.Split('\n').Take(3);
        return lines.Any(line => line.Contains('|') && line.Count(c => c == '|') >= 2);
    }

    /// <summary>
    /// Calculate common keyword ratio
    /// </summary>
    private double CalculateCommonKeywordRatio(string text1, string text2)
    {
        var words1 = ExtractKeywords(text1);
        var words2 = ExtractKeywords(text2);

        if (words1.Count == 0 || words2.Count == 0)
            return 0.0;

        var common = words1.Intersect(words2, StringComparer.OrdinalIgnoreCase).Count();
        var total = words1.Union(words2, StringComparer.OrdinalIgnoreCase).Count();

        return total > 0 ? (double)common / total : 0.0;
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
    /// Detect reference expressions
    /// </summary>
    private bool DetectReference(string text)
    {
        var referencePatterns = new[]
        {
            @"\b(this|that|these|those|it|they|them|their)\b",
            @"\b(above|below|following|previous|aforementioned)\b",
            @"\b(as mentioned|as described|as shown|as discussed)\b"
        };

        return referencePatterns.Any(pattern =>
            Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase));
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
}
