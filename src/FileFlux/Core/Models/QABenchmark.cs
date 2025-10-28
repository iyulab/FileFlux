using FileFlux.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FileFlux;

/// <summary>
/// QA benchmark dataset for measuring RAG system performance.
/// Supports merging with existing QA datasets for iterative improvement.
/// </summary>
public class QABenchmark
{
    /// <summary>
    /// Document identifier for correlation with processing results
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// Document source path or identifier
    /// </summary>
    public string DocumentPath { get; set; } = string.Empty;

    /// <summary>
    /// Generated questions and their expected answers
    /// </summary>
    public List<GeneratedQuestion> Questions { get; set; } = new();

    /// <summary>
    /// QA validation results and quality metrics
    /// </summary>
    public QAValidationResult ValidationResult { get; set; } = new();

    /// <summary>
    /// Overall answerability score (0.0-1.0)
    /// Indicates how well chunks can answer the generated questions
    /// </summary>
    public double AnswerabilityScore { get; set; }

    /// <summary>
    /// Generation timestamp for tracking and versioning
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Processing options used during QA generation
    /// </summary>
    public ChunkingOptions? ProcessingOptions { get; set; }

    /// <summary>
    /// Merges two QA benchmark datasets, combining questions and updating metrics.
    /// Essential for iterative QA dataset improvement.
    /// </summary>
    /// <param name="existing">Existing QA benchmark dataset</param>
    /// <param name="newQA">New QA benchmark to merge</param>
    /// <returns>Merged QA benchmark with combined questions and updated metrics</returns>
    public static QABenchmark Merge(QABenchmark existing, QABenchmark newQA)
    {
        if (existing == null) return newQA;
        if (newQA == null) return existing;

        var merged = new QABenchmark
        {
            DocumentId = existing.DocumentId,
            DocumentPath = existing.DocumentPath,
            GeneratedAt = DateTime.UtcNow,
            ProcessingOptions = newQA.ProcessingOptions ?? existing.ProcessingOptions
        };

        // Combine questions, avoiding duplicates based on similarity
        var allQuestions = new List<GeneratedQuestion>(existing.Questions);

        foreach (var newQuestion in newQA.Questions)
        {
            if (!IsDuplicateQuestion(allQuestions, newQuestion))
            {
                allQuestions.Add(newQuestion);
            }
        }

        merged.Questions = allQuestions;

        // Recalculate combined metrics
        merged.AnswerabilityScore = CalculateCombinedAnswerabilityScore(
            existing.AnswerabilityScore, existing.Questions.Count,
            newQA.AnswerabilityScore, newQA.Questions.Count);

        // Merge validation results
        merged.ValidationResult = MergeValidationResults(existing.ValidationResult, newQA.ValidationResult);

        return merged;
    }

    /// <summary>
    /// Checks if a question is similar to existing questions to avoid duplicates
    /// </summary>
    private static bool IsDuplicateQuestion(List<GeneratedQuestion> existingQuestions, GeneratedQuestion newQuestion)
    {
        return existingQuestions.Any(q =>
            CalculateQuestionSimilarity(q.Question, newQuestion.Question) > 0.8);
    }

    /// <summary>
    /// Calculates similarity between two questions (simplified implementation)
    /// </summary>
    private static double CalculateQuestionSimilarity(string question1, string question2)
    {
        // Simplified similarity based on word overlap
        var words1 = question1.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var words2 = question2.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return union == 0 ? 0 : (double)intersection / union;
    }

    /// <summary>
    /// Calculates weighted average of answerability scores
    /// </summary>
    private static double CalculateCombinedAnswerabilityScore(
        double existingScore, int existingCount,
        double newScore, int newCount)
    {
        if (existingCount == 0) return newScore;
        if (newCount == 0) return existingScore;

        var totalWeight = existingCount + newCount;
        return (existingScore * existingCount + newScore * newCount) / totalWeight;
    }

    /// <summary>
    /// Merges validation results from two QA benchmarks
    /// </summary>
    private static QAValidationResult MergeValidationResults(
        QAValidationResult existing, QAValidationResult newResult)
    {
        return new QAValidationResult
        {
            TotalQuestions = existing.TotalQuestions + newResult.TotalQuestions,
            AnswerableQuestions = existing.AnswerableQuestions + newResult.AnswerableQuestions,
            HighQualityAnswers = existing.HighQualityAnswers + newResult.HighQualityAnswers,
            AverageConfidence = (existing.AverageConfidence + newResult.AverageConfidence) / 2,
            ValidationMetrics = MergeDictionaries(existing.ValidationMetrics, newResult.ValidationMetrics)
        };
    }

    /// <summary>
    /// Merges two dictionaries, averaging numeric values
    /// </summary>
    private static Dictionary<string, object> MergeDictionaries(
        Dictionary<string, object> dict1, Dictionary<string, object> dict2)
    {
        var merged = new Dictionary<string, object>(dict1);

        foreach (var kvp in dict2)
        {
            if (merged.ContainsKey(kvp.Key))
            {
                // Average numeric values, keep the latest for others
                if (kvp.Value is double d1 && merged[kvp.Key] is double d2)
                {
                    merged[kvp.Key] = (d1 + d2) / 2;
                }
                else
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }
            else
            {
                merged[kvp.Key] = kvp.Value;
            }
        }

        return merged;
    }
}

/// <summary>
/// Generated question with expected answer and metadata
/// </summary>
public class GeneratedQuestion
{
    /// <summary>
    /// Unique identifier for the question
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The generated question text
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Expected answer based on document content
    /// </summary>
    public string ExpectedAnswer { get; set; } = string.Empty;

    /// <summary>
    /// IDs of chunks that contain relevant information for answering
    /// </summary>
    public List<string> RelevantChunkIds { get; } = new();

    /// <summary>
    /// Question type classification for targeted evaluation
    /// </summary>
    public QuestionType Type { get; set; }

    /// <summary>
    /// Difficulty level of the question (0.0-1.0)
    /// Higher values indicate more complex questions
    /// </summary>
    public double DifficultyScore { get; set; }

    /// <summary>
    /// Confidence in the question quality (0.0-1.0)
    /// Based on LLM generation confidence and validation
    /// </summary>
    public double ConfidenceScore { get; set; }

    /// <summary>
    /// Additional metadata for question analysis
    /// </summary>
    public Dictionary<string, object> Metadata { get; } = new();
}

/// <summary>
/// Types of generated questions for comprehensive evaluation
/// </summary>
public enum QuestionType
{
    /// <summary>
    /// Questions about specific facts or data points
    /// </summary>
    Factual,

    /// <summary>
    /// Questions about concepts, definitions, or explanations
    /// </summary>
    Conceptual,

    /// <summary>
    /// Questions requiring analysis or reasoning across multiple facts
    /// </summary>
    Analytical,

    /// <summary>
    /// Questions about procedures or step-by-step processes
    /// </summary>
    Procedural,

    /// <summary>
    /// Questions comparing different concepts or items
    /// </summary>
    Comparative
}

/// <summary>
/// Results of QA validation against document chunks
/// </summary>
public class QAValidationResult
{
    /// <summary>
    /// Total number of questions validated
    /// </summary>
    public int TotalQuestions { get; set; }

    /// <summary>
    /// Number of questions that can be answered from the chunks
    /// </summary>
    public int AnswerableQuestions { get; set; }

    /// <summary>
    /// Number of questions with high-quality, complete answers
    /// </summary>
    public int HighQualityAnswers { get; set; }

    /// <summary>
    /// Average confidence score across all validations
    /// </summary>
    public double AverageConfidence { get; set; }

    /// <summary>
    /// Detailed validation metrics and analysis results
    /// </summary>
    public Dictionary<string, object> ValidationMetrics { get; set; } = new();

    /// <summary>
    /// Answerability ratio (0.0-1.0)
    /// </summary>
    public double AnswerabilityRatio => TotalQuestions == 0 ? 0 : (double)AnswerableQuestions / TotalQuestions;

    /// <summary>
    /// High quality ratio (0.0-1.0)
    /// </summary>
    public double HighQualityRatio => TotalQuestions == 0 ? 0 : (double)HighQualityAnswers / TotalQuestions;
}

/// <summary>
/// Comparative benchmark results for different chunking strategies
/// </summary>
public class QualityBenchmarkResult
{
    /// <summary>
    /// Document identifier
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// Quality reports for each tested strategy
    /// </summary>
    public List<DocumentQualityReport> QualityReports { get; set; } = new();

    /// <summary>
    /// Comparative analysis results
    /// </summary>
    public Dictionary<string, object> ComparisonMetrics { get; set; } = new();

    /// <summary>
    /// Recommended strategy based on benchmark results
    /// </summary>
    public string RecommendedStrategy { get; set; } = string.Empty;

    /// <summary>
    /// Benchmark execution timestamp
    /// </summary>
    public DateTime BenchmarkedAt { get; set; } = DateTime.UtcNow;
}
