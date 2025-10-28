using FileFlux.Domain;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileFlux.Infrastructure.Quality;

/// <summary>
/// Internal benchmarking suite that uses the same quality analysis logic as external APIs.
/// Ensures consistency and trustworthiness between internal testing and external functionality.
/// </summary>
internal class FileFluxBenchmarkSuite
{
    private readonly ChunkQualityEngine _qualityEngine;
    private readonly IDocumentProcessor _documentProcessor;
    private readonly ILogger<FileFluxBenchmarkSuite>? _logger;

    public FileFluxBenchmarkSuite(
        ChunkQualityEngine qualityEngine,
        IDocumentProcessor documentProcessor,
        ILogger<FileFluxBenchmarkSuite>? logger = null)
    {
        _qualityEngine = qualityEngine;
        _documentProcessor = documentProcessor;
        _logger = logger;
    }

    /// <summary>
    /// Runs comprehensive benchmark tests using the same logic as external API.
    /// Validates consistency between internal benchmarking and external quality analysis.
    /// </summary>
    public async Task<BenchmarkResult> RunComprehensiveBenchmarkAsync(
        string filePath,
        string[] chunkingStrategies,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Starting comprehensive benchmark for: {FilePath}", filePath);
        var stopwatch = Stopwatch.StartNew();

        var benchmarkResult = new BenchmarkResult
        {
            FilePath = filePath,
            StartTime = DateTime.UtcNow,
            TestedStrategies = chunkingStrategies.ToList()
        };

        try
        {
            // Test each chunking strategy
            foreach (var strategy in chunkingStrategies)
            {
                _logger?.LogDebug("Benchmarking strategy: {Strategy}", strategy);

                var strategyResult = await BenchmarkStrategyAsync(filePath, strategy, cancellationToken).ConfigureAwait(false);
                benchmarkResult.StrategyResults.Add(strategyResult);
            }

            // Validate consistency between internal and external APIs
            var consistencyResult = await ValidateApiConsistencyAsync(filePath, cancellationToken).ConfigureAwait(false);
            benchmarkResult.ConsistencyValidation = consistencyResult;

            // Generate overall recommendations
            benchmarkResult.OverallRecommendations = GenerateOverallRecommendations(benchmarkResult.StrategyResults);

            stopwatch.Stop();
            benchmarkResult.TotalDuration = stopwatch.Elapsed;
            benchmarkResult.Success = true;

            _logger?.LogInformation("Benchmark completed successfully in {Duration:F2}s", stopwatch.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            benchmarkResult.Success = false;
            benchmarkResult.Error = ex.Message;
            benchmarkResult.TotalDuration = stopwatch.Elapsed;

            _logger?.LogError(ex, "Benchmark failed for file: {FilePath}", filePath);
        }

        return benchmarkResult;
    }

    /// <summary>
    /// Benchmarks a specific chunking strategy using internal quality engine.
    /// </summary>
    private async Task<StrategyBenchmarkResult> BenchmarkStrategyAsync(
        string filePath,
        string strategy,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new StrategyBenchmarkResult { Strategy = strategy };

        try
        {
            var options = new ChunkingOptions { Strategy = strategy };

            // Process document using standard pipeline
            var rawContent = await _documentProcessor.ExtractAsync(filePath, cancellationToken).ConfigureAwait(false);
            var parsedContent = await _documentProcessor.ParseAsync(rawContent, cancellationToken: cancellationToken).ConfigureAwait(false);
            var chunks = await _documentProcessor.ChunkAsync(parsedContent, options, cancellationToken).ConfigureAwait(false);

            // Calculate quality metrics using internal engine (same as external API)
            var qualityMetrics = await _qualityEngine.CalculateQualityMetricsAsync(chunks, cancellationToken).ConfigureAwait(false);

            // Generate QA benchmark using internal engine
            var questions = await _qualityEngine.GenerateQuestionsAsync(parsedContent, 10, cancellationToken).ConfigureAwait(false);
            var qaValidation = await _qualityEngine.ValidateAnswerabilityAsync(questions, chunks, cancellationToken).ConfigureAwait(false);

            result.ChunkCount = chunks.Length;
            result.AverageChunkSize = chunks.Length > 0 ? chunks.Average(c => c.Content.Length) : 0;
            result.QualityMetrics = qualityMetrics;
            result.QAValidation = qaValidation;
            result.OverallScore = CalculateOverallScore(qualityMetrics, qaValidation);
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _logger?.LogError(ex, "Strategy benchmark failed for {Strategy}", strategy);
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;
        return result;
    }

    /// <summary>
    /// Validates that internal benchmarking logic produces the same results as external API.
    /// Critical for ensuring trustworthiness and consistency.
    /// </summary>
    private async Task<ConsistencyValidationResult> ValidateApiConsistencyAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        _logger?.LogDebug("Validating API consistency for: {FilePath}", filePath);

        var validation = new ConsistencyValidationResult();

        try
        {
            // Test with external API (DocumentQualityAnalyzer)
            var externalAnalyzer = new DocumentQualityAnalyzer(_qualityEngine, _documentProcessor);
            var externalReport = await externalAnalyzer.AnalyzeQualityAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);

            // Test with internal benchmarking (same logic, direct calls)
            var options = new ChunkingOptions { Strategy = "Intelligent" };
            var rawContent = await _documentProcessor.ExtractAsync(filePath, cancellationToken).ConfigureAwait(false);
            var parsedContent = await _documentProcessor.ParseAsync(rawContent, cancellationToken: cancellationToken).ConfigureAwait(false);
            var chunks = await _documentProcessor.ChunkAsync(parsedContent, options, cancellationToken).ConfigureAwait(false);
            var internalMetrics = await _qualityEngine.CalculateQualityMetricsAsync(chunks, cancellationToken).ConfigureAwait(false);

            // Compare results
            validation.ScoreDifference = Math.Abs(externalReport.OverallQualityScore -
                CalculateOverallScore(internalMetrics, new QAValidationResult()));

            validation.MetricsConsistent = CompareQualityMetrics(externalReport.ChunkingQuality, internalMetrics);

            validation.IsConsistent = validation.ScoreDifference < 0.01 && validation.MetricsConsistent;
            validation.ValidationDetails = new Dictionary<string, object>
            {
                { "external_score", externalReport.OverallQualityScore },
                { "internal_score", CalculateOverallScore(internalMetrics, new QAValidationResult()) },
                { "score_difference", validation.ScoreDifference },
                { "metrics_consistent", validation.MetricsConsistent }
            };

            _logger?.LogDebug("Consistency validation - Consistent: {IsConsistent}, Score difference: {ScoreDifference:F4}",
                validation.IsConsistent, validation.ScoreDifference);
        }
        catch (Exception ex)
        {
            validation.IsConsistent = false;
            validation.ValidationError = ex.Message;
            _logger?.LogError(ex, "Consistency validation failed");
        }

        return validation;
    }

    private static bool CompareQualityMetrics(ChunkingQualityMetrics external, ChunkingQualityMetrics internalMetrics)
    {
        const double tolerance = 0.01;

        return Math.Abs(external.AverageCompleteness - internalMetrics.AverageCompleteness) < tolerance &&
               Math.Abs(external.ContentConsistency - internalMetrics.ContentConsistency) < tolerance &&
               Math.Abs(external.BoundaryQuality - internalMetrics.BoundaryQuality) < tolerance &&
               Math.Abs(external.SizeDistribution - internalMetrics.SizeDistribution) < tolerance &&
               Math.Abs(external.OverlapEffectiveness - internalMetrics.OverlapEffectiveness) < tolerance;
    }

    private static double CalculateOverallScore(ChunkingQualityMetrics qualityMetrics, QAValidationResult qaValidation)
    {
        const double chunkingWeight = 0.8;
        const double answerabilityWeight = 0.2;

        var chunkingScore = (qualityMetrics.AverageCompleteness +
                            qualityMetrics.ContentConsistency +
                            qualityMetrics.BoundaryQuality +
                            qualityMetrics.SizeDistribution +
                            qualityMetrics.OverlapEffectiveness) / 5.0;

        var answerabilityScore = qaValidation.AnswerabilityRatio;

        return chunkingScore * chunkingWeight + answerabilityScore * answerabilityWeight;
    }

    private static List<string> GenerateOverallRecommendations(List<StrategyBenchmarkResult> strategyResults)
    {
        var recommendations = new List<string>();

        if (!strategyResults.Any(r => r.Success))
        {
            recommendations.Add("All chunking strategies failed. Check document format and processing pipeline.");
            return recommendations;
        }

        var bestStrategy = strategyResults.Where(r => r.Success).OrderByDescending(r => r.OverallScore).First();
        var worstStrategy = strategyResults.Where(r => r.Success).OrderBy(r => r.OverallScore).First();

        recommendations.Add($"Best performing strategy: {bestStrategy.Strategy} (Score: {bestStrategy.OverallScore:F3})");

        if (bestStrategy.OverallScore - worstStrategy.OverallScore > 0.1)
        {
            recommendations.Add($"Consider avoiding {worstStrategy.Strategy} strategy for this document type");
        }

        if (strategyResults.All(r => r.Success && r.QAValidation?.AnswerabilityRatio < 0.8))
        {
            recommendations.Add("Low answerability scores across all strategies - consider increasing chunk overlap or using semantic chunking");
        }

        var avgChunkSize = strategyResults.Where(r => r.Success).Average(r => r.AverageChunkSize);
        if (avgChunkSize > 2000)
        {
            recommendations.Add("Large average chunk size detected - consider reducing MaxChunkSize parameter");
        }
        else if (avgChunkSize < 200)
        {
            recommendations.Add("Small average chunk size detected - consider increasing MaxChunkSize parameter");
        }

        return recommendations;
    }
}

/// <summary>
/// Benchmark results for the entire test suite
/// </summary>
public class BenchmarkResult
{
    public string FilePath { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<string> TestedStrategies { get; set; } = new();
    public List<StrategyBenchmarkResult> StrategyResults { get; set; } = new();
    public ConsistencyValidationResult? ConsistencyValidation { get; set; }
    public List<string> OverallRecommendations { get; set; } = new();
}

/// <summary>
/// Benchmark results for a specific chunking strategy
/// </summary>
public class StrategyBenchmarkResult
{
    public string Strategy { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int ChunkCount { get; set; }
    public double AverageChunkSize { get; set; }
    public ChunkingQualityMetrics? QualityMetrics { get; set; }
    public QAValidationResult? QAValidation { get; set; }
    public double OverallScore { get; set; }
}

/// <summary>
/// Results of consistency validation between internal and external APIs
/// </summary>
public class ConsistencyValidationResult
{
    public bool IsConsistent { get; set; }
    public double ScoreDifference { get; set; }
    public bool MetricsConsistent { get; set; }
    public string? ValidationError { get; set; }
    public Dictionary<string, object> ValidationDetails { get; set; } = new();
}
