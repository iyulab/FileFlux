using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileFlux;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using FileFlux.RealWorldBenchmark.Metrics;
using FileFlux.RealWorldBenchmark.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FileFlux.RealWorldBenchmark.Benchmarks;

/// <summary>
/// Comprehensive RAG Quality Benchmark for evaluating chunking strategies
/// </summary>
public class RAGQualityBenchmark
{
    private readonly IServiceProvider _serviceProvider;
    private readonly OpenAiEmbeddingService _embeddingService;
    private readonly string _testDataPath;
    
    public RAGQualityBenchmark(IServiceProvider serviceProvider, string apiKey, string testDataPath)
    {
        _serviceProvider = serviceProvider;
        _embeddingService = new OpenAiEmbeddingService(apiKey);
        _testDataPath = testDataPath;
    }
    
    /// <summary>
    /// Run comprehensive quality benchmark for all strategies
    /// </summary>
    public async Task<BenchmarkReport> RunComprehensiveBenchmarkAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("\n🚀 Starting Comprehensive RAG Quality Benchmark\n");
        Console.WriteLine(new string('=', 80));
        
        var report = new BenchmarkReport
        {
            StartTime = DateTime.UtcNow,
            TestDataPath = _testDataPath
        };
        
        // Test strategies
        var strategies = new[]
        {
            "Smart",       // New smart chunking with 70% completeness target
            "Intelligent", // Original intelligent strategy
            "Semantic",    // Semantic boundary strategy
            "Paragraph",   // Paragraph-based strategy
            "FixedSize"    // Fixed size baseline
        };
        
        // Test documents
        var testDocuments = GetTestDocuments();
        
        foreach (var strategy in strategies)
        {
            Console.WriteLine($"\n📊 Testing Strategy: {strategy}");
            Console.WriteLine(new string('-', 40));
            
            var strategyResult = await TestStrategyAsync(
                strategy,
                testDocuments,
                cancellationToken);
            
            report.StrategyResults.Add(strategyResult);
            
            // Display results
            DisplayStrategyResults(strategyResult);
        }
        
        report.EndTime = DateTime.UtcNow;
        report.TotalDuration = report.EndTime - report.StartTime;
        
        // Generate final report
        GenerateFinalReport(report);
        
        return report;
    }
    
    /// <summary>
    /// Test a specific chunking strategy
    /// </summary>
    private async Task<StrategyBenchmarkResult> TestStrategyAsync(
        string strategyName,
        List<TestDocument> testDocuments,
        CancellationToken cancellationToken)
    {
        var result = new StrategyBenchmarkResult
        {
            StrategyName = strategyName,
            StartTime = DateTime.UtcNow
        };
        
        var processor = _serviceProvider.GetRequiredService<IDocumentProcessor>();
        
        foreach (var testDoc in testDocuments)
        {
            Console.WriteLine($"  📄 Processing: {Path.GetFileName(testDoc.FilePath)}");
            
            var docResult = await TestDocumentAsync(
                processor,
                strategyName,
                testDoc,
                cancellationToken);
            
            result.DocumentResults.Add(docResult);
        }
        
        // Calculate aggregate metrics
        result.AverageCompleteness = result.DocumentResults.Average(d => d.CompletenessResult.AverageCompleteness);
        result.AverageF1Score = result.DocumentResults.Average(d => d.RAGResult?.AverageF1Score ?? 0);
        result.AverageRecall = result.DocumentResults.Average(d => d.RAGResult?.AverageRecall ?? 0);
        result.AveragePrecision = result.DocumentResults.Average(d => d.RAGResult?.AveragePrecision ?? 0);
        result.ChunksAbove70Percent = result.DocumentResults.Sum(d => d.CompletenessResult.ChunksAbove70Percent);
        result.TotalChunks = result.DocumentResults.Sum(d => d.CompletenessResult.TotalChunks);
        
        result.EndTime = DateTime.UtcNow;
        result.ProcessingTime = result.EndTime - result.StartTime;
        
        return result;
    }
    
    /// <summary>
    /// Test a single document with a strategy
    /// </summary>
    private async Task<DocumentBenchmarkResult> TestDocumentAsync(
        IDocumentProcessor processor,
        string strategyName,
        TestDocument testDoc,
        CancellationToken cancellationToken)
    {
        var result = new DocumentBenchmarkResult
        {
            DocumentName = Path.GetFileName(testDoc.FilePath),
            DocumentType = testDoc.DocumentType
        };
        
        var stopwatch = Stopwatch.StartNew();
        
        // Process document with strategy
        var options = new ChunkingOptions
        {
            Strategy = strategyName,
            MaxChunkSize = 512,
            OverlapSize = 64
            // PreserveStructure and IncludeMetadata options not available in current ChunkingOptions
        };
        
        var chunks = processor.ProcessAsync(testDoc.FilePath, options, cancellationToken);
        var chunksList = new List<DocumentChunk>();
        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            chunksList.Add(chunk);
        }
        
        stopwatch.Stop();
        result.ProcessingTime = stopwatch.Elapsed;
        result.ChunkCount = chunksList.Count;
        
        // Evaluate chunk completeness
        Console.WriteLine($"    ✅ Evaluating completeness for {chunksList.Count} chunks...");
        result.CompletenessResult = RAGQualityMetrics.EvaluateChunkCompleteness(chunksList);
        
        // Evaluate overlap functionality
        Console.WriteLine($"    🔄 Analyzing overlap with expected size {options.OverlapSize}...");
        result.OverlapResult = RAGQualityMetrics.AnalyzeOverlap(chunksList, options.OverlapSize);
        
        // Generate embeddings if API key is available
        if (_embeddingService != null && testDoc.TestQueries.Any())
        {
            Console.WriteLine($"    🧮 Generating embeddings for RAG evaluation...");
            
            try
            {
                // Generate embeddings for chunks
                var chunkTexts = chunksList.Select(c => c.Content).ToList();
                var embeddings = await _embeddingService.GenerateEmbeddingsAsync(chunkTexts, cancellationToken);
                
                var indexedChunks = chunkTexts.Zip(embeddings, (text, emb) => (text, emb)).ToList();
                
                // Prepare test cases
                var testCases = testDoc.TestQueries.Select(q => 
                    (q.Query, q.ExpectedContent)).ToList();
                
                // Evaluate RAG retrieval
                result.RAGResult = RAGQualityMetrics.EvaluateRetrieval(
                    testCases,
                    indexedChunks,
                    query => _embeddingService.GenerateEmbeddingAsync(query, cancellationToken).Result
                );
                
                // Evaluate semantic coherence
                Console.WriteLine($"    🔗 Evaluating semantic coherence...");
                result.CoherenceResult = RAGQualityMetrics.EvaluateSemanticCoherence(
                    chunksList,
                    text => _embeddingService.GenerateEmbeddingAsync(text, cancellationToken).Result
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ⚠️ Embedding generation failed: {ex.Message}");
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Get test documents
    /// </summary>
    private List<TestDocument> GetTestDocuments()
    {
        var documents = new List<TestDocument>();
        
        // Technical documentation test
        documents.Add(new TestDocument
        {
            FilePath = Path.Combine(_testDataPath, "technical_doc.md"),
            DocumentType = "Technical",
            TestQueries = new List<TestQuery>
            {
                new TestQuery
                {
                    Query = "How to configure authentication in the system?",
                    ExpectedContent = new List<string> 
                    { 
                        "authentication configuration",
                        "auth settings",
                        "security setup"
                    }
                },
                new TestQuery
                {
                    Query = "What are the API rate limits?",
                    ExpectedContent = new List<string>
                    {
                        "rate limiting",
                        "API throttling",
                        "request limits"
                    }
                }
            }
        });
        
        // Business document test
        documents.Add(new TestDocument
        {
            FilePath = Path.Combine(_testDataPath, "business_report.pdf"),
            DocumentType = "Business",
            TestQueries = new List<TestQuery>
            {
                new TestQuery
                {
                    Query = "What is the quarterly revenue growth?",
                    ExpectedContent = new List<string>
                    {
                        "quarterly revenue",
                        "growth rate",
                        "financial performance"
                    }
                },
                new TestQuery
                {
                    Query = "What are the key market trends?",
                    ExpectedContent = new List<string>
                    {
                        "market trends",
                        "industry analysis",
                        "market dynamics"
                    }
                }
            }
        });
        
        // Academic paper test
        documents.Add(new TestDocument
        {
            FilePath = Path.Combine(_testDataPath, "research_paper.pdf"),
            DocumentType = "Academic",
            TestQueries = new List<TestQuery>
            {
                new TestQuery
                {
                    Query = "What is the main hypothesis of the study?",
                    ExpectedContent = new List<string>
                    {
                        "hypothesis",
                        "research question",
                        "study objective"
                    }
                },
                new TestQuery
                {
                    Query = "What methodology was used?",
                    ExpectedContent = new List<string>
                    {
                        "methodology",
                        "research methods",
                        "experimental design"
                    }
                }
            }
        });
        
        // Filter to existing files
        return documents.Where(d => File.Exists(d.FilePath)).ToList();
    }
    
    /// <summary>
    /// Display strategy results
    /// </summary>
    private void DisplayStrategyResults(StrategyBenchmarkResult result)
    {
        Console.WriteLine($"\n  📈 Results for {result.StrategyName}:");
        Console.WriteLine($"    • Average Completeness: {result.AverageCompleteness:P1}");
        Console.WriteLine($"    • Chunks ≥70% Complete: {result.ChunksAbove70Percent}/{result.TotalChunks}");
        
        if (result.AverageF1Score > 0)
        {
            Console.WriteLine($"    • RAG F1 Score: {result.AverageF1Score:P1}");
            Console.WriteLine($"    • RAG Recall: {result.AverageRecall:P1}");
            Console.WriteLine($"    • RAG Precision: {result.AveragePrecision:P1}");
        }
        
        Console.WriteLine($"    • Processing Time: {result.ProcessingTime.TotalSeconds:F2}s");
    }
    
    /// <summary>
    /// Generate final benchmark report
    /// </summary>
    private void GenerateFinalReport(BenchmarkReport report)
    {
        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine("📊 FINAL BENCHMARK REPORT");
        Console.WriteLine(new string('=', 80));
        
        // Rank strategies by completeness
        var rankedByCompleteness = report.StrategyResults
            .OrderByDescending(s => s.AverageCompleteness)
            .ToList();
        
        Console.WriteLine("\n🏆 Strategy Rankings by Completeness:");
        for (int i = 0; i < rankedByCompleteness.Count; i++)
        {
            var strategy = rankedByCompleteness[i];
            var medal = i switch
            {
                0 => "🥇",
                1 => "🥈",
                2 => "🥉",
                _ => "  "
            };
            
            Console.WriteLine($"{medal} {i + 1}. {strategy.StrategyName}: {strategy.AverageCompleteness:P1}");
        }
        
        // Rank by RAG performance if available
        var withRAG = report.StrategyResults.Where(s => s.AverageF1Score > 0).ToList();
        if (withRAG.Any())
        {
            var rankedByRAG = withRAG.OrderByDescending(s => s.AverageF1Score).ToList();
            
            Console.WriteLine("\n🎯 Strategy Rankings by RAG F1 Score:");
            for (int i = 0; i < rankedByRAG.Count; i++)
            {
                var strategy = rankedByRAG[i];
                var medal = i switch
                {
                    0 => "🥇",
                    1 => "🥈", 
                    2 => "🥉",
                    _ => "  "
                };
                
                Console.WriteLine($"{medal} {i + 1}. {strategy.StrategyName}: {strategy.AverageF1Score:P1}");
            }
        }
        
        // Success criteria check
        Console.WriteLine("\n✅ Success Criteria Check:");
        
        var bestStrategy = rankedByCompleteness.First();
        var meetsCompletenessTarget = bestStrategy.AverageCompleteness >= 0.7;
        var meetsRAGTarget = bestStrategy.AverageRecall >= 0.85;
        
        Console.WriteLine($"  • 70% Completeness Target: {(meetsCompletenessTarget ? "✅ ACHIEVED" : "❌ NOT MET")} ({bestStrategy.StrategyName}: {bestStrategy.AverageCompleteness:P1})");
        
        if (withRAG.Any())
        {
            Console.WriteLine($"  • 85% Recall Target: {(meetsRAGTarget ? "✅ ACHIEVED" : "❌ NOT MET")} ({bestStrategy.StrategyName}: {bestStrategy.AverageRecall:P1})");
        }
        
        Console.WriteLine($"\n⏱️ Total Benchmark Duration: {report.TotalDuration.TotalSeconds:F2} seconds");
        Console.WriteLine($"📅 Completed: {report.EndTime:yyyy-MM-dd HH:mm:ss} UTC");
    }
}

// Benchmark result classes
public class BenchmarkReport
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public string TestDataPath { get; set; } = "";
    public List<StrategyBenchmarkResult> StrategyResults { get; set; } = new();
}

public class StrategyBenchmarkResult
{
    public string StrategyName { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public List<DocumentBenchmarkResult> DocumentResults { get; set; } = new();
    public double AverageCompleteness { get; set; }
    public double AverageF1Score { get; set; }
    public double AverageRecall { get; set; }
    public double AveragePrecision { get; set; }
    public int ChunksAbove70Percent { get; set; }
    public int TotalChunks { get; set; }
}

public class DocumentBenchmarkResult
{
    public string DocumentName { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public TimeSpan ProcessingTime { get; set; }
    public int ChunkCount { get; set; }
    public ChunkCompletenessResult CompletenessResult { get; set; } = new();
    public OverlapAnalysisResult OverlapResult { get; set; } = new();
    public RAGEvaluationResult? RAGResult { get; set; }
    public SemanticCoherenceResult? CoherenceResult { get; set; }
}

public class TestDocument
{
    public string FilePath { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public List<TestQuery> TestQueries { get; set; } = new();
}

public class TestQuery
{
    public string Query { get; set; } = "";
    public List<string> ExpectedContent { get; set; } = new();
}