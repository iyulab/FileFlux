using System;
using System.Collections.Generic;
using System.Linq;
using FileFlux.Domain;

namespace FileFlux.RealWorldBenchmark.Metrics;

/// <summary>
/// RAG Quality Metrics for evaluating chunking strategies
/// </summary>
public class RAGQualityMetrics
{
    /// <summary>
    /// Evaluate RAG retrieval quality for a set of chunks
    /// </summary>
    public static RAGEvaluationResult EvaluateRetrieval(
        List<(string Query, List<string> ExpectedChunks)> testCases,
        List<(string ChunkContent, float[] Embedding)> indexedChunks,
        Func<string, float[]> embedQuery)
    {
        var results = new List<QueryResult>();
        
        foreach (var testCase in testCases)
        {
            var queryEmbedding = embedQuery(testCase.Query);
            var retrievedChunks = RetrieveChunks(queryEmbedding, indexedChunks, topK: 5);
            
            var result = EvaluateQueryResult(
                testCase.Query,
                testCase.ExpectedChunks,
                retrievedChunks.Select(r => r.ChunkContent).ToList()
            );
            
            results.Add(result);
        }
        
        return new RAGEvaluationResult
        {
            QueryResults = results,
            AveragePrecision = results.Average(r => r.Precision),
            AverageRecall = results.Average(r => r.Recall),
            AverageF1Score = results.Average(r => r.F1Score),
            MeanReciprocalRank = CalculateMRR(results),
            TotalQueries = testCases.Count,
            SuccessfulQueries = results.Count(r => r.Recall > 0)
        };
    }
    
    /// <summary>
    /// Evaluate chunk completeness (문장 완성도)
    /// </summary>
    public static ChunkCompletenessResult EvaluateChunkCompleteness(List<DocumentChunk> chunks)
    {
        var results = new List<ChunkCompleteness>();
        
        foreach (var chunk in chunks)
        {
            var completeness = CalculateCompleteness(chunk.Content);
            results.Add(new ChunkCompleteness
            {
                ChunkId = chunk.Id,
                ChunkIndex = chunk.ChunkIndex,
                Content = chunk.Content,
                CompletenessScore = completeness.Score,
                IncompleteSentences = completeness.IncompleteSentences,
                TotalSentences = completeness.TotalSentences,
                HasSentenceBreaks = completeness.HasSentenceBreaks
            });
        }
        
        return new ChunkCompletenessResult
        {
            ChunkResults = results,
            AverageCompleteness = results.Average(r => r.CompletenessScore),
            MinCompleteness = results.Min(r => r.CompletenessScore),
            MaxCompleteness = results.Max(r => r.CompletenessScore),
            ChunksWithBreaks = results.Count(r => r.HasSentenceBreaks),
            TotalChunks = chunks.Count,
            ChunksAbove70Percent = results.Count(r => r.CompletenessScore >= 0.7),
            ChunksAbove90Percent = results.Count(r => r.CompletenessScore >= 0.9)
        };
    }
    
    /// <summary>
    /// Evaluate overlap functionality
    /// </summary>
    public static OverlapAnalysisResult AnalyzeOverlap(List<DocumentChunk> chunks, int expectedOverlapSize)
    {
        var overlapResults = new List<OverlapInfo>();
        
        for (int i = 0; i < chunks.Count - 1; i++)
        {
            var currentChunk = chunks[i];
            var nextChunk = chunks[i + 1];
            
            var overlap = CalculateOverlap(currentChunk.Content, nextChunk.Content);
            overlapResults.Add(new OverlapInfo
            {
                ChunkIndex1 = i,
                ChunkIndex2 = i + 1,
                OverlapCharacters = overlap.Characters,
                OverlapTokens = overlap.Tokens,
                OverlapPercentage = overlap.Percentage,
                HasOverlap = overlap.Characters > 0
            });
        }
        
        return new OverlapAnalysisResult
        {
            OverlapInfos = overlapResults,
            AverageOverlapSize = overlapResults.Average(o => o.OverlapCharacters),
            ExpectedOverlapSize = expectedOverlapSize,
            ChunksWithOverlap = overlapResults.Count(o => o.HasOverlap),
            TotalChunkPairs = overlapResults.Count,
            OverlapEffectiveness = CalculateOverlapEffectiveness(overlapResults, expectedOverlapSize)
        };
    }
    
    /// <summary>
    /// Calculate semantic coherence of chunks
    /// </summary>
    public static SemanticCoherenceResult EvaluateSemanticCoherence(
        List<DocumentChunk> chunks,
        Func<string, float[]> embedText)
    {
        var coherenceScores = new List<double>();
        
        for (int i = 0; i < chunks.Count - 1; i++)
        {
            var embedding1 = embedText(chunks[i].Content);
            var embedding2 = embedText(chunks[i + 1].Content);
            
            var similarity = CosineSimilarity(embedding1, embedding2);
            coherenceScores.Add(similarity);
        }
        
        return new SemanticCoherenceResult
        {
            AverageCoherence = coherenceScores.Any() ? coherenceScores.Average() : 0,
            MinCoherence = coherenceScores.Any() ? coherenceScores.Min() : 0,
            MaxCoherence = coherenceScores.Any() ? coherenceScores.Max() : 0,
            CoherenceScores = coherenceScores,
            TotalChunks = chunks.Count
        };
    }
    
    private static List<(string ChunkContent, double Similarity)> RetrieveChunks(
        float[] queryEmbedding,
        List<(string ChunkContent, float[] Embedding)> indexedChunks,
        int topK)
    {
        var similarities = new List<(string ChunkContent, double Similarity)>();
        
        foreach (var chunk in indexedChunks)
        {
            var similarity = CosineSimilarity(queryEmbedding, chunk.Embedding);
            similarities.Add((chunk.ChunkContent, similarity));
        }
        
        return similarities
            .OrderByDescending(s => s.Similarity)
            .Take(topK)
            .ToList();
    }
    
    private static QueryResult EvaluateQueryResult(
        string query,
        List<string> expectedChunks,
        List<string> retrievedChunks)
    {
        var relevantRetrieved = 0;
        
        foreach (var retrieved in retrievedChunks)
        {
            if (expectedChunks.Any(expected => 
                retrieved.Contains(expected, StringComparison.OrdinalIgnoreCase) ||
                expected.Contains(retrieved, StringComparison.OrdinalIgnoreCase)))
            {
                relevantRetrieved++;
            }
        }
        
        var precision = retrievedChunks.Count > 0 ? 
            (double)relevantRetrieved / retrievedChunks.Count : 0;
        
        var recall = expectedChunks.Count > 0 ? 
            (double)relevantRetrieved / expectedChunks.Count : 0;
        
        var f1Score = (precision + recall) > 0 ? 
            2 * (precision * recall) / (precision + recall) : 0;
        
        return new QueryResult
        {
            Query = query,
            Precision = precision,
            Recall = recall,
            F1Score = f1Score,
            RelevantRetrieved = relevantRetrieved,
            TotalRetrieved = retrievedChunks.Count,
            TotalExpected = expectedChunks.Count
        };
    }
    
    private static double CalculateMRR(List<QueryResult> results)
    {
        if (!results.Any()) return 0;
        
        var reciprocalRanks = results
            .Where(r => r.RelevantRetrieved > 0)
            .Select(r => 1.0 / (results.IndexOf(r) + 1));
        
        return reciprocalRanks.Any() ? reciprocalRanks.Average() : 0;
    }
    
    private static (double Score, int IncompleteSentences, int TotalSentences, bool HasSentenceBreaks) 
        CalculateCompleteness(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return (0, 0, 0, false);
        
        // Split into sentences
        var sentences = content.Split(new[] { '.', '!', '?', '。' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
        
        if (sentences.Count == 0)
            return (0, 0, 0, false);
        
        var incompleteSentences = 0;
        var hasSentenceBreaks = false;
        
        // Check last character of content
        var lastChar = content.Trim().LastOrDefault();
        if (char.IsLetterOrDigit(lastChar) || lastChar == ',')
        {
            incompleteSentences++;
            hasSentenceBreaks = true;
        }
        
        // Check for ellipsis indicating incomplete content
        if (content.Contains("...") && !content.Contains("etc."))
        {
            incompleteSentences++;
            hasSentenceBreaks = true;
        }
        
        // Check first sentence (might be continuation from previous chunk)
        var firstSentence = sentences.FirstOrDefault() ?? "";
        if (firstSentence.Length > 0 && char.IsLower(firstSentence[0]))
        {
            incompleteSentences++;
            hasSentenceBreaks = true;
        }
        
        var completenessScore = sentences.Count > 0 ? 
            (double)(sentences.Count - incompleteSentences) / sentences.Count : 0;
        
        return (completenessScore, incompleteSentences, sentences.Count, hasSentenceBreaks);
    }
    
    private static (int Characters, int Tokens, double Percentage) CalculateOverlap(
        string chunk1, string chunk2)
    {
        if (string.IsNullOrEmpty(chunk1) || string.IsNullOrEmpty(chunk2))
            return (0, 0, 0);
        
        // Find longest common substring at the end of chunk1 and beginning of chunk2
        var maxOverlap = Math.Min(chunk1.Length, chunk2.Length);
        var overlapLength = 0;
        
        for (int len = 1; len <= maxOverlap; len++)
        {
            var endOfChunk1 = chunk1.Substring(chunk1.Length - len);
            var startOfChunk2 = chunk2.Substring(0, len);
            
            if (endOfChunk1.Equals(startOfChunk2, StringComparison.Ordinal))
            {
                overlapLength = len;
            }
        }
        
        // Estimate tokens (roughly 1 token per 4 characters)
        var overlapTokens = overlapLength / 4;
        var overlapPercentage = chunk2.Length > 0 ? (double)overlapLength / chunk2.Length : 0;
        
        return (overlapLength, overlapTokens, overlapPercentage);
    }
    
    private static double CalculateOverlapEffectiveness(
        List<OverlapInfo> overlapInfos, int expectedSize)
    {
        if (!overlapInfos.Any() || expectedSize <= 0)
            return 0;
        
        var effectiveOverlaps = overlapInfos
            .Count(o => Math.Abs(o.OverlapCharacters - expectedSize) <= expectedSize * 0.2);
        
        return (double)effectiveOverlaps / overlapInfos.Count;
    }
    
    private static double CosineSimilarity(float[] embedding1, float[] embedding2)
    {
        if (embedding1.Length != embedding2.Length)
            return 0;
        
        double dotProduct = 0;
        double magnitude1 = 0;
        double magnitude2 = 0;
        
        for (int i = 0; i < embedding1.Length; i++)
        {
            dotProduct += embedding1[i] * embedding2[i];
            magnitude1 += embedding1[i] * embedding1[i];
            magnitude2 += embedding2[i] * embedding2[i];
        }
        
        magnitude1 = Math.Sqrt(magnitude1);
        magnitude2 = Math.Sqrt(magnitude2);
        
        if (magnitude1 == 0 || magnitude2 == 0)
            return 0;
        
        return dotProduct / (magnitude1 * magnitude2);
    }
}

// Result classes
public class RAGEvaluationResult
{
    public List<QueryResult> QueryResults { get; set; } = new();
    public double AveragePrecision { get; set; }
    public double AverageRecall { get; set; }
    public double AverageF1Score { get; set; }
    public double MeanReciprocalRank { get; set; }
    public int TotalQueries { get; set; }
    public int SuccessfulQueries { get; set; }
}

public class QueryResult
{
    public string Query { get; set; } = "";
    public double Precision { get; set; }
    public double Recall { get; set; }
    public double F1Score { get; set; }
    public int RelevantRetrieved { get; set; }
    public int TotalRetrieved { get; set; }
    public int TotalExpected { get; set; }
}

public class ChunkCompletenessResult
{
    public List<ChunkCompleteness> ChunkResults { get; set; } = new();
    public double AverageCompleteness { get; set; }
    public double MinCompleteness { get; set; }
    public double MaxCompleteness { get; set; }
    public int ChunksWithBreaks { get; set; }
    public int TotalChunks { get; set; }
    public int ChunksAbove70Percent { get; set; }
    public int ChunksAbove90Percent { get; set; }
}

public class ChunkCompleteness
{
    public string ChunkId { get; set; } = "";
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = "";
    public double CompletenessScore { get; set; }
    public int IncompleteSentences { get; set; }
    public int TotalSentences { get; set; }
    public bool HasSentenceBreaks { get; set; }
}

public class OverlapAnalysisResult
{
    public List<OverlapInfo> OverlapInfos { get; set; } = new();
    public double AverageOverlapSize { get; set; }
    public int ExpectedOverlapSize { get; set; }
    public int ChunksWithOverlap { get; set; }
    public int TotalChunkPairs { get; set; }
    public double OverlapEffectiveness { get; set; }
}

public class OverlapInfo
{
    public int ChunkIndex1 { get; set; }
    public int ChunkIndex2 { get; set; }
    public int OverlapCharacters { get; set; }
    public int OverlapTokens { get; set; }
    public double OverlapPercentage { get; set; }
    public bool HasOverlap { get; set; }
}

public class SemanticCoherenceResult
{
    public double AverageCoherence { get; set; }
    public double MinCoherence { get; set; }
    public double MaxCoherence { get; set; }
    public List<double> CoherenceScores { get; set; } = new();
    public int TotalChunks { get; set; }
}