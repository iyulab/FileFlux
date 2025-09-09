using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FileFlux.Domain;

namespace FileFlux.RealWorldBenchmark.Metrics;

/// <summary>
/// Advanced RAG quality analyzer with comprehensive metrics
/// </summary>
public class RAGQualityAnalyzer
{
    private readonly Dictionary<string, double> _weights;
    
    public RAGQualityAnalyzer()
    {
        // Default weights for different quality aspects
        _weights = new Dictionary<string, double>
        {
            ["semantic_completeness"] = 0.25,
            ["context_preservation"] = 0.20,
            ["information_density"] = 0.15,
            ["structural_integrity"] = 0.15,
            ["retrieval_readiness"] = 0.15,
            ["boundary_quality"] = 0.10
        };
    }
    
    /// <summary>
    /// Perform comprehensive quality analysis on chunks
    /// </summary>
    public RAGQualityReport AnalyzeChunks(List<DocumentChunk> chunks, string originalContent = null)
    {
        var report = new RAGQualityReport
        {
            TotalChunks = chunks.Count,
            Timestamp = DateTime.UtcNow
        };
        
        // 1. Semantic Completeness Analysis
        report.SemanticCompleteness = AnalyzeSemanticCompleteness(chunks);
        
        // 2. Context Preservation Analysis
        report.ContextPreservation = AnalyzeContextPreservation(chunks);
        
        // 3. Information Density Analysis
        report.InformationDensity = AnalyzeInformationDensity(chunks);
        
        // 4. Structural Integrity Analysis
        report.StructuralIntegrity = AnalyzeStructuralIntegrity(chunks);
        
        // 5. Retrieval Readiness Analysis
        report.RetrievalReadiness = AnalyzeRetrievalReadiness(chunks);
        
        // 6. Boundary Quality Analysis
        report.BoundaryQuality = AnalyzeBoundaryQuality(chunks);
        
        // 7. Coverage Analysis (if original content provided)
        if (!string.IsNullOrEmpty(originalContent))
        {
            report.ContentCoverage = AnalyzeContentCoverage(chunks, originalContent);
        }
        
        // Calculate composite score
        report.CompositeScore = CalculateCompositeScore(report);
        
        // Generate recommendations
        report.Recommendations = GenerateRecommendations(report);
        
        return report;
    }
    
    private SemanticCompletenessMetrics AnalyzeSemanticCompleteness(List<DocumentChunk> chunks)
    {
        var metrics = new SemanticCompletenessMetrics();
        
        var completeSentences = 0;
        var completeThoughts = 0;
        var orphanedFragments = 0;
        var sentenceBoundaryScores = new List<double>();
        
        foreach (var chunk in chunks)
        {
            var content = chunk.Content.Trim();
            
            // Check for complete sentences
            var sentences = SplitIntoSentences(content);
            var completeCount = sentences.Count(IsCompleteSentence);
            completeSentences += completeCount;
            
            // Check for complete thoughts (paragraphs or sections)
            if (IsCompleteThought(content))
                completeThoughts++;
            
            // Check for orphaned fragments
            if (IsOrphanedFragment(content))
                orphanedFragments++;
            
            // Calculate sentence boundary score
            var boundaryScore = CalculateSentenceBoundaryScore(content);
            sentenceBoundaryScores.Add(boundaryScore);
        }
        
        metrics.CompleteSentenceRatio = chunks.Count > 0 
            ? (double)completeSentences / chunks.Count : 0;
        metrics.CompleteThoughtRatio = chunks.Count > 0 
            ? (double)completeThoughts / chunks.Count : 0;
        metrics.OrphanedFragmentRatio = chunks.Count > 0 
            ? (double)orphanedFragments / chunks.Count : 0;
        metrics.AverageBoundaryScore = sentenceBoundaryScores.Any() 
            ? sentenceBoundaryScores.Average() : 0;
        
        metrics.OverallScore = CalculateSemanticScore(metrics);
        
        return metrics;
    }
    
    private ContextPreservationMetrics AnalyzeContextPreservation(List<DocumentChunk> chunks)
    {
        var metrics = new ContextPreservationMetrics();
        
        if (chunks.Count < 2)
        {
            metrics.OverallScore = 1.0; // Single chunk preserves all context
            return metrics;
        }
        
        var overlapScores = new List<double>();
        var continuityScores = new List<double>();
        var referencePreservation = new List<double>();
        
        for (int i = 1; i < chunks.Count; i++)
        {
            var prev = chunks[i - 1];
            var curr = chunks[i];
            
            // Analyze overlap quality
            var overlapScore = CalculateOverlapQuality(prev.Content, curr.Content);
            overlapScores.Add(overlapScore);
            
            // Analyze continuity
            var continuityScore = CalculateContinuityScore(prev.Content, curr.Content);
            continuityScores.Add(continuityScore);
            
            // Analyze reference preservation
            var refScore = CalculateReferencePreservation(prev.Content, curr.Content);
            referencePreservation.Add(refScore);
        }
        
        metrics.AverageOverlapScore = overlapScores.Any() ? overlapScores.Average() : 0;
        metrics.ContinuityScore = continuityScores.Any() ? continuityScores.Average() : 0;
        metrics.ReferencePreservationScore = referencePreservation.Any() ? referencePreservation.Average() : 0;
        
        // Check for context windows
        metrics.ContextWindowCoverage = CalculateContextWindowCoverage(chunks);
        
        metrics.OverallScore = (metrics.AverageOverlapScore * 0.3 +
                                metrics.ContinuityScore * 0.3 +
                                metrics.ReferencePreservationScore * 0.2 +
                                metrics.ContextWindowCoverage * 0.2);
        
        return metrics;
    }
    
    private InformationDensityMetrics AnalyzeInformationDensity(List<DocumentChunk> chunks)
    {
        var metrics = new InformationDensityMetrics();
        
        var densityScores = new List<double>();
        var redundancyScores = new List<double>();
        var uniqueTermRatios = new List<double>();
        
        var allTerms = new HashSet<string>();
        
        foreach (var chunk in chunks)
        {
            var content = chunk.Content;
            
            // Calculate token density
            var density = CalculateTokenDensity(content);
            densityScores.Add(density);
            
            // Calculate redundancy
            var redundancy = CalculateRedundancy(content);
            redundancyScores.Add(redundancy);
            
            // Calculate unique term ratio
            var terms = ExtractTerms(content);
            var uniqueInChunk = terms.Distinct().Count();
            var uniqueRatio = terms.Count > 0 ? (double)uniqueInChunk / terms.Count : 0;
            uniqueTermRatios.Add(uniqueRatio);
            
            allTerms.UnionWith(terms);
        }
        
        metrics.AverageTokenDensity = densityScores.Any() ? densityScores.Average() : 0;
        metrics.RedundancyScore = redundancyScores.Any() ? redundancyScores.Average() : 0;
        metrics.UniqueTermRatio = uniqueTermRatios.Any() ? uniqueTermRatios.Average() : 0;
        metrics.InformationEntropy = CalculateEntropy(chunks);
        
        metrics.OverallScore = (metrics.AverageTokenDensity * 0.3 +
                               (1 - metrics.RedundancyScore) * 0.3 +
                               metrics.UniqueTermRatio * 0.2 +
                               metrics.InformationEntropy * 0.2);
        
        return metrics;
    }
    
    private StructuralIntegrityMetrics AnalyzeStructuralIntegrity(List<DocumentChunk> chunks)
    {
        var metrics = new StructuralIntegrityMetrics();
        
        var preservedHeaders = 0;
        var preservedLists = 0;
        var preservedCodeBlocks = 0;
        var preservedTables = 0;
        var brokenStructures = 0;
        
        foreach (var chunk in chunks)
        {
            var content = chunk.Content;
            
            // Check for preserved structures
            if (HasCompleteHeader(content)) preservedHeaders++;
            if (HasCompleteList(content)) preservedLists++;
            if (HasCompleteCodeBlock(content)) preservedCodeBlocks++;
            if (HasCompleteTable(content)) preservedTables++;
            
            // Check for broken structures
            if (HasBrokenStructure(content)) brokenStructures++;
        }
        
        var totalStructures = preservedHeaders + preservedLists + preservedCodeBlocks + preservedTables;
        
        metrics.HeaderPreservation = chunks.Count > 0 ? (double)preservedHeaders / chunks.Count : 0;
        metrics.ListPreservation = chunks.Count > 0 ? (double)preservedLists / chunks.Count : 0;
        metrics.CodeBlockPreservation = chunks.Count > 0 ? (double)preservedCodeBlocks / chunks.Count : 0;
        metrics.TablePreservation = chunks.Count > 0 ? (double)preservedTables / chunks.Count : 0;
        metrics.BrokenStructureRatio = chunks.Count > 0 ? (double)brokenStructures / chunks.Count : 0;
        
        metrics.OverallScore = totalStructures > 0 
            ? (double)(totalStructures - brokenStructures) / totalStructures 
            : 1.0 - metrics.BrokenStructureRatio;
        
        return metrics;
    }
    
    private RetrievalReadinessMetrics AnalyzeRetrievalReadiness(List<DocumentChunk> chunks)
    {
        var metrics = new RetrievalReadinessMetrics();
        
        var selfContainedCount = 0;
        var keywordRichCount = 0;
        var summaryQualityScores = new List<double>();
        var queryMatchScores = new List<double>();
        
        foreach (var chunk in chunks)
        {
            var content = chunk.Content;
            
            // Check if chunk is self-contained
            if (IsSelfContained(content))
                selfContainedCount++;
            
            // Check keyword richness
            if (IsKeywordRich(content))
                keywordRichCount++;
            
            // Evaluate summary quality
            var summaryScore = EvaluateSummaryQuality(content);
            summaryQualityScores.Add(summaryScore);
            
            // Evaluate query match potential
            var queryScore = EvaluateQueryMatchPotential(content);
            queryMatchScores.Add(queryScore);
        }
        
        metrics.SelfContainedRatio = chunks.Count > 0 
            ? (double)selfContainedCount / chunks.Count : 0;
        metrics.KeywordRichness = chunks.Count > 0 
            ? (double)keywordRichCount / chunks.Count : 0;
        metrics.AverageSummaryQuality = summaryQualityScores.Any() 
            ? summaryQualityScores.Average() : 0;
        metrics.QueryMatchPotential = queryMatchScores.Any() 
            ? queryMatchScores.Average() : 0;
        
        metrics.OverallScore = (metrics.SelfContainedRatio * 0.3 +
                               metrics.KeywordRichness * 0.2 +
                               metrics.AverageSummaryQuality * 0.25 +
                               metrics.QueryMatchPotential * 0.25);
        
        return metrics;
    }
    
    private BoundaryQualityMetrics AnalyzeBoundaryQuality(List<DocumentChunk> chunks)
    {
        var metrics = new BoundaryQualityMetrics();
        
        if (chunks.Count < 2)
        {
            metrics.OverallScore = 1.0;
            return metrics;
        }
        
        var cleanStartCount = 0;
        var cleanEndCount = 0;
        var transitionScores = new List<double>();
        
        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            
            // Check clean starts
            if (HasCleanStart(chunk.Content))
                cleanStartCount++;
            
            // Check clean ends
            if (HasCleanEnd(chunk.Content))
                cleanEndCount++;
            
            // Check transition quality
            if (i < chunks.Count - 1)
            {
                var transitionScore = EvaluateTransition(chunk.Content, chunks[i + 1].Content);
                transitionScores.Add(transitionScore);
            }
        }
        
        metrics.CleanStartRatio = chunks.Count > 0 
            ? (double)cleanStartCount / chunks.Count : 0;
        metrics.CleanEndRatio = chunks.Count > 0 
            ? (double)cleanEndCount / chunks.Count : 0;
        metrics.TransitionQuality = transitionScores.Any() 
            ? transitionScores.Average() : 0;
        
        metrics.OverallScore = (metrics.CleanStartRatio * 0.35 +
                               metrics.CleanEndRatio * 0.35 +
                               metrics.TransitionQuality * 0.30);
        
        return metrics;
    }
    
    private ContentCoverageMetrics AnalyzeContentCoverage(List<DocumentChunk> chunks, string originalContent)
    {
        var metrics = new ContentCoverageMetrics();
        
        // Combine all chunk content
        var combinedContent = string.Join(" ", chunks.Select(c => c.Content));
        
        // Calculate coverage ratio
        metrics.CoverageRatio = (double)combinedContent.Length / originalContent.Length;
        
        // Check for missing sections
        var originalSentences = SplitIntoSentences(originalContent);
        var chunkSentences = SplitIntoSentences(combinedContent);
        
        metrics.MissingSectionRatio = 1.0 - ((double)chunkSentences.Count / originalSentences.Count);
        
        // Check for duplication
        var duplicateCount = chunks.Count(c => 
            chunks.Count(other => other != c && other.Content == c.Content) > 0);
        metrics.DuplicationRatio = chunks.Count > 0 
            ? (double)duplicateCount / chunks.Count : 0;
        
        metrics.OverallScore = Math.Min(1.0, metrics.CoverageRatio) * 
                              (1 - metrics.MissingSectionRatio) * 
                              (1 - metrics.DuplicationRatio);
        
        return metrics;
    }
    
    // Helper methods
    private List<string> SplitIntoSentences(string text)
    {
        var pattern = @"(?<=[.!?])\s+(?=[A-Z])";
        return Regex.Split(text, pattern)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }
    
    private bool IsCompleteSentence(string sentence)
    {
        sentence = sentence.Trim();
        return sentence.Length > 10 && 
               (sentence.EndsWith('.') || sentence.EndsWith('!') || sentence.EndsWith('?'));
    }
    
    private bool IsCompleteThought(string content)
    {
        var sentences = SplitIntoSentences(content);
        return sentences.Count >= 2 && 
               sentences.All(IsCompleteSentence) &&
               content.Length > 100;
    }
    
    private bool IsOrphanedFragment(string content)
    {
        content = content.Trim();
        return content.Length < 50 || 
               (!content.Contains('.') && !content.Contains('!') && !content.Contains('?'));
    }
    
    private double CalculateSentenceBoundaryScore(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0;
        
        var score = 0.0;
        content = content.Trim();
        
        // Check start
        if (char.IsUpper(content[0])) score += 0.5;
        
        // Check end
        if (content.EndsWith('.') || content.EndsWith('!') || content.EndsWith('?')) 
            score += 0.5;
        
        return score;
    }
    
    private double CalculateSemanticScore(SemanticCompletenessMetrics metrics)
    {
        return metrics.CompleteSentenceRatio * 0.3 +
               metrics.CompleteThoughtRatio * 0.3 +
               (1 - metrics.OrphanedFragmentRatio) * 0.2 +
               metrics.AverageBoundaryScore * 0.2;
    }
    
    private double CalculateOverlapQuality(string prev, string curr)
    {
        var maxOverlap = Math.Min(prev.Length, Math.Min(curr.Length, 256));
        
        for (int overlapSize = maxOverlap; overlapSize > 20; overlapSize--)
        {
            var prevTail = prev.Substring(Math.Max(0, prev.Length - overlapSize));
            if (curr.StartsWith(prevTail))
            {
                // Score based on overlap size and quality
                var sizeScore = (double)overlapSize / maxOverlap;
                var qualityScore = prevTail.Contains('.') || prevTail.Contains(' ') ? 1.0 : 0.5;
                return sizeScore * qualityScore;
            }
        }
        
        return 0;
    }
    
    private double CalculateContinuityScore(string prev, string curr)
    {
        // Check for logical continuity indicators
        var continuityIndicators = new[] { "however", "therefore", "moreover", "furthermore", 
                                          "additionally", "consequently", "thus", "hence" };
        
        var currLower = curr.ToLower();
        var hasContinuityWord = continuityIndicators.Any(indicator => 
            currLower.StartsWith(indicator) || currLower.Contains(" " + indicator));
        
        return hasContinuityWord ? 1.0 : 0.5;
    }
    
    private double CalculateReferencePreservation(string prev, string curr)
    {
        // Check for pronouns and references
        var referencePatterns = new[] { @"\bhe\b", @"\bshe\b", @"\bit\b", @"\bthey\b", 
                                       @"\bthis\b", @"\bthat\b", @"\bthese\b", @"\bthose\b" };
        
        var hasReference = referencePatterns.Any(pattern => 
            Regex.IsMatch(curr.Substring(0, Math.Min(100, curr.Length)), pattern, RegexOptions.IgnoreCase));
        
        return hasReference ? 0.5 : 1.0; // Lower score if references without context
    }
    
    private double CalculateContextWindowCoverage(List<DocumentChunk> chunks)
    {
        // Evaluate how well chunks provide context for each other
        var coverageScores = new List<double>();
        
        for (int i = 0; i < chunks.Count; i++)
        {
            var contextWindow = GetContextWindow(chunks, i, 2); // ±2 chunks
            var score = EvaluateContextWindow(chunks[i].Content, contextWindow);
            coverageScores.Add(score);
        }
        
        return coverageScores.Any() ? coverageScores.Average() : 0;
    }
    
    private List<string> GetContextWindow(List<DocumentChunk> chunks, int index, int windowSize)
    {
        var window = new List<string>();
        
        for (int i = Math.Max(0, index - windowSize); 
             i <= Math.Min(chunks.Count - 1, index + windowSize); i++)
        {
            if (i != index)
                window.Add(chunks[i].Content);
        }
        
        return window;
    }
    
    private double EvaluateContextWindow(string chunk, List<string> window)
    {
        if (!window.Any()) return 1.0;
        
        var chunkTerms = ExtractTerms(chunk).Distinct().ToList();
        var windowTerms = window.SelectMany(ExtractTerms).Distinct().ToList();
        
        var overlap = chunkTerms.Intersect(windowTerms).Count();
        return chunkTerms.Count > 0 ? (double)overlap / chunkTerms.Count : 0;
    }
    
    private double CalculateTokenDensity(string content)
    {
        var words = content.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var meaningfulWords = words.Count(w => w.Length > 3 && !IsStopWord(w));
        
        return words.Length > 0 ? (double)meaningfulWords / words.Length : 0;
    }
    
    private double CalculateRedundancy(string content)
    {
        var sentences = SplitIntoSentences(content);
        if (sentences.Count < 2) return 0;
        
        var redundancy = 0.0;
        for (int i = 0; i < sentences.Count - 1; i++)
        {
            for (int j = i + 1; j < sentences.Count; j++)
            {
                var similarity = CalculateSimilarity(sentences[i], sentences[j]);
                if (similarity > 0.7) redundancy += similarity;
            }
        }
        
        return redundancy / (sentences.Count * (sentences.Count - 1) / 2);
    }
    
    private double CalculateSimilarity(string s1, string s2)
    {
        var terms1 = ExtractTerms(s1).Distinct().ToList();
        var terms2 = ExtractTerms(s2).Distinct().ToList();
        
        if (!terms1.Any() || !terms2.Any()) return 0;
        
        var intersection = terms1.Intersect(terms2).Count();
        var union = terms1.Union(terms2).Count();
        
        return (double)intersection / union;
    }
    
    private List<string> ExtractTerms(string text)
    {
        return text.ToLower()
            .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':', '"', '\'', '(', ')' }, 
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !IsStopWord(w))
            .ToList();
    }
    
    private bool IsStopWord(string word)
    {
        var stopWords = new HashSet<string> 
        { 
            "the", "is", "at", "which", "on", "and", "a", "an", "as", "are", 
            "was", "were", "been", "be", "have", "has", "had", "do", "does", 
            "did", "will", "would", "could", "should", "may", "might", "must",
            "can", "could", "to", "of", "in", "for", "with", "by", "from", "about"
        };
        
        return stopWords.Contains(word.ToLower());
    }
    
    private double CalculateEntropy(List<DocumentChunk> chunks)
    {
        var allTerms = chunks.SelectMany(c => ExtractTerms(c.Content)).ToList();
        if (!allTerms.Any()) return 0;
        
        var termFrequencies = allTerms.GroupBy(t => t)
            .ToDictionary(g => g.Key, g => (double)g.Count() / allTerms.Count);
        
        var entropy = 0.0;
        foreach (var freq in termFrequencies.Values)
        {
            if (freq > 0)
                entropy -= freq * Math.Log(freq, 2);
        }
        
        return entropy / Math.Log(termFrequencies.Count, 2); // Normalize
    }
    
    private bool HasCompleteHeader(string content)
    {
        return Regex.IsMatch(content, @"^#{1,6}\s+.+$", RegexOptions.Multiline) ||
               Regex.IsMatch(content, @"^.+\n[=-]+$", RegexOptions.Multiline);
    }
    
    private bool HasCompleteList(string content)
    {
        var listPattern = @"^[\*\-\+]\s+.+$";
        var numberedPattern = @"^\d+\.\s+.+$";
        
        var listItems = Regex.Matches(content, listPattern, RegexOptions.Multiline).Count +
                       Regex.Matches(content, numberedPattern, RegexOptions.Multiline).Count;
        
        return listItems >= 2; // At least 2 items for a complete list
    }
    
    private bool HasCompleteCodeBlock(string content)
    {
        return content.Contains("```") && 
               Regex.IsMatch(content, @"```[\s\S]*?```");
    }
    
    private bool HasCompleteTable(string content)
    {
        return content.Contains("|") && 
               Regex.IsMatch(content, @"\|.+\|.*\n\|[-:\s|]+\|");
    }
    
    private bool HasBrokenStructure(string content)
    {
        // Check for incomplete structures
        var hasOrphanedCodeStart = content.Contains("```") && 
                                   !Regex.IsMatch(content, @"```[\s\S]*?```");
        var hasOrphanedListItem = Regex.IsMatch(content, @"^[\*\-\+]\s+.+$", RegexOptions.Multiline) &&
                                  Regex.Matches(content, @"^[\*\-\+]\s+.+$", RegexOptions.Multiline).Count == 1;
        
        return hasOrphanedCodeStart || hasOrphanedListItem;
    }
    
    private bool IsSelfContained(string content)
    {
        // Check if chunk can stand alone
        var hasCompleteThought = IsCompleteThought(content);
        var hasNoOrphanedReferences = !Regex.IsMatch(content, @"^(he|she|it|they|this|that|these|those)\b", 
                                                     RegexOptions.IgnoreCase);
        
        return hasCompleteThought && hasNoOrphanedReferences;
    }
    
    private bool IsKeywordRich(string content)
    {
        var terms = ExtractTerms(content);
        var uniqueTerms = terms.Distinct().Count();
        
        // Keyword rich if unique terms > 20% of total terms and > 10 unique terms
        return uniqueTerms > 10 && 
               terms.Count > 0 && 
               (double)uniqueTerms / terms.Count > 0.2;
    }
    
    private double EvaluateSummaryQuality(string content)
    {
        // Evaluate how well the chunk summarizes its content
        var sentences = SplitIntoSentences(content);
        if (!sentences.Any()) return 0;
        
        var firstSentence = sentences.First();
        var restContent = string.Join(" ", sentences.Skip(1));
        
        // Check if first sentence acts as summary
        var summaryScore = 0.0;
        
        if (firstSentence.Length > 20 && firstSentence.Length < 200)
            summaryScore += 0.3;
        
        if (IsCompleteSentence(firstSentence))
            summaryScore += 0.3;
        
        // Check if key terms from rest appear in first sentence
        if (restContent.Length > 0)
        {
            var firstTerms = ExtractTerms(firstSentence).Distinct();
            var restTerms = ExtractTerms(restContent).Distinct();
            var overlap = firstTerms.Intersect(restTerms).Count();
            
            if (restTerms.Any())
                summaryScore += 0.4 * ((double)overlap / restTerms.Count());
        }
        else
        {
            summaryScore += 0.4; // Single sentence chunks are self-summarizing
        }
        
        return summaryScore;
    }
    
    private double EvaluateQueryMatchPotential(string content)
    {
        // Evaluate how well the chunk might match user queries
        var score = 0.0;
        
        // Has question words (likely to match question queries)
        if (Regex.IsMatch(content, @"\b(what|when|where|who|why|how)\b", RegexOptions.IgnoreCase))
            score += 0.2;
        
        // Has definition patterns
        if (Regex.IsMatch(content, @"\bis\b.*\b(defined as|refers to|means)\b", RegexOptions.IgnoreCase))
            score += 0.2;
        
        // Has descriptive content
        var terms = ExtractTerms(content);
        if (terms.Count > 20)
            score += 0.2;
        
        // Has proper nouns (likely entity queries)
        if (Regex.Matches(content, @"\b[A-Z][a-z]+\b").Count > 2)
            score += 0.2;
        
        // Has numbers/data (likely factual queries)
        if (Regex.Matches(content, @"\b\d+\b").Count > 2)
            score += 0.2;
        
        return Math.Min(1.0, score);
    }
    
    private bool HasCleanStart(string content)
    {
        content = content.TrimStart();
        if (string.IsNullOrEmpty(content)) return false;
        
        // Check for clean starts
        return char.IsUpper(content[0]) || 
               content.StartsWith("#") || 
               Regex.IsMatch(content, @"^\d+\.");
    }
    
    private bool HasCleanEnd(string content)
    {
        content = content.TrimEnd();
        if (string.IsNullOrEmpty(content)) return false;
        
        // Check for clean ends
        return content.EndsWith('.') || 
               content.EndsWith('!') || 
               content.EndsWith('?') ||
               content.EndsWith("```");
    }
    
    private double EvaluateTransition(string current, string next)
    {
        // Evaluate transition quality between chunks
        var score = 0.0;
        
        // Check overlap
        var overlapQuality = CalculateOverlapQuality(current, next);
        score += overlapQuality * 0.4;
        
        // Check continuity
        var continuity = CalculateContinuityScore(current, next);
        score += continuity * 0.3;
        
        // Check boundary cleanliness
        if (HasCleanEnd(current)) score += 0.15;
        if (HasCleanStart(next)) score += 0.15;
        
        return score;
    }
    
    private double CalculateCompositeScore(RAGQualityReport report)
    {
        var scores = new Dictionary<string, double>
        {
            ["semantic_completeness"] = report.SemanticCompleteness?.OverallScore ?? 0,
            ["context_preservation"] = report.ContextPreservation?.OverallScore ?? 0,
            ["information_density"] = report.InformationDensity?.OverallScore ?? 0,
            ["structural_integrity"] = report.StructuralIntegrity?.OverallScore ?? 0,
            ["retrieval_readiness"] = report.RetrievalReadiness?.OverallScore ?? 0,
            ["boundary_quality"] = report.BoundaryQuality?.OverallScore ?? 0
        };
        
        var weightedSum = 0.0;
        var totalWeight = 0.0;
        
        foreach (var kvp in scores)
        {
            if (_weights.ContainsKey(kvp.Key))
            {
                weightedSum += kvp.Value * _weights[kvp.Key];
                totalWeight += _weights[kvp.Key];
            }
        }
        
        return totalWeight > 0 ? weightedSum / totalWeight : 0;
    }
    
    private List<string> GenerateRecommendations(RAGQualityReport report)
    {
        var recommendations = new List<string>();
        
        // Semantic completeness recommendations
        if (report.SemanticCompleteness?.OrphanedFragmentRatio > 0.2)
            recommendations.Add("High ratio of orphaned fragments detected. Consider increasing chunk size or improving boundary detection.");
        
        if (report.SemanticCompleteness?.CompleteSentenceRatio < 0.7)
            recommendations.Add("Many chunks lack complete sentences. Adjust chunking strategy to preserve sentence boundaries.");
        
        // Context preservation recommendations
        if (report.ContextPreservation?.AverageOverlapScore < 0.3)
            recommendations.Add("Low overlap between chunks. Increase overlap size to improve context preservation.");
        
        if (report.ContextPreservation?.ReferencePreservationScore < 0.5)
            recommendations.Add("Poor reference preservation. Consider using semantic-aware chunking strategies.");
        
        // Information density recommendations
        if (report.InformationDensity?.RedundancyScore > 0.3)
            recommendations.Add("High redundancy detected. Optimize chunking to reduce duplicate information.");
        
        if (report.InformationDensity?.AverageTokenDensity < 0.5)
            recommendations.Add("Low information density. Consider filtering or preprocessing to remove filler content.");
        
        // Structural integrity recommendations
        if (report.StructuralIntegrity?.BrokenStructureRatio > 0.1)
            recommendations.Add("Broken structures detected. Use structure-aware chunking for documents with lists, tables, or code blocks.");
        
        // Retrieval readiness recommendations
        if (report.RetrievalReadiness?.SelfContainedRatio < 0.6)
            recommendations.Add("Many chunks are not self-contained. Adjust strategy to create more independent chunks.");
        
        if (report.RetrievalReadiness?.KeywordRichness < 0.5)
            recommendations.Add("Low keyword richness. Consider preprocessing to enhance searchable terms.");
        
        // Boundary quality recommendations
        if (report.BoundaryQuality?.TransitionQuality < 0.5)
            recommendations.Add("Poor transitions between chunks. Improve boundary detection algorithms.");
        
        // Overall recommendations
        if (report.CompositeScore < 0.6)
            recommendations.Add("Overall quality below threshold. Consider using 'Intelligent' chunking strategy with appropriate parameters.");
        
        if (!recommendations.Any())
            recommendations.Add("Quality metrics are satisfactory. Current configuration is well-optimized for RAG.");
        
        return recommendations;
    }
}

// Data models for quality metrics
public class RAGQualityReport
{
    public int TotalChunks { get; set; }
    public DateTime Timestamp { get; set; }
    public double CompositeScore { get; set; }
    
    public SemanticCompletenessMetrics SemanticCompleteness { get; set; }
    public ContextPreservationMetrics ContextPreservation { get; set; }
    public InformationDensityMetrics InformationDensity { get; set; }
    public StructuralIntegrityMetrics StructuralIntegrity { get; set; }
    public RetrievalReadinessMetrics RetrievalReadiness { get; set; }
    public BoundaryQualityMetrics BoundaryQuality { get; set; }
    public ContentCoverageMetrics ContentCoverage { get; set; }
    
    public List<string> Recommendations { get; set; } = new();
}

public class SemanticCompletenessMetrics
{
    public double CompleteSentenceRatio { get; set; }
    public double CompleteThoughtRatio { get; set; }
    public double OrphanedFragmentRatio { get; set; }
    public double AverageBoundaryScore { get; set; }
    public double OverallScore { get; set; }
}

public class ContextPreservationMetrics
{
    public double AverageOverlapScore { get; set; }
    public double ContinuityScore { get; set; }
    public double ReferencePreservationScore { get; set; }
    public double ContextWindowCoverage { get; set; }
    public double OverallScore { get; set; }
}

public class InformationDensityMetrics
{
    public double AverageTokenDensity { get; set; }
    public double RedundancyScore { get; set; }
    public double UniqueTermRatio { get; set; }
    public double InformationEntropy { get; set; }
    public double OverallScore { get; set; }
}

public class StructuralIntegrityMetrics
{
    public double HeaderPreservation { get; set; }
    public double ListPreservation { get; set; }
    public double CodeBlockPreservation { get; set; }
    public double TablePreservation { get; set; }
    public double BrokenStructureRatio { get; set; }
    public double OverallScore { get; set; }
}

public class RetrievalReadinessMetrics
{
    public double SelfContainedRatio { get; set; }
    public double KeywordRichness { get; set; }
    public double AverageSummaryQuality { get; set; }
    public double QueryMatchPotential { get; set; }
    public double OverallScore { get; set; }
}

public class BoundaryQualityMetrics
{
    public double CleanStartRatio { get; set; }
    public double CleanEndRatio { get; set; }
    public double TransitionQuality { get; set; }
    public double OverallScore { get; set; }
}

public class ContentCoverageMetrics
{
    public double CoverageRatio { get; set; }
    public double MissingSectionRatio { get; set; }
    public double DuplicationRatio { get; set; }
    public double OverallScore { get; set; }
}