# Document Quality Analysis

> **Measure, Evaluate, Optimize** - Ensure your RAG system gets the best possible chunks

## Overview

FileFlux's quality analysis system helps you:
- **Measure** chunking quality with objective metrics
- **Evaluate** different chunking strategies
- **Optimize** RAG system performance
- **Generate** Q&A benchmarks for testing

All quality features work **with or without** AI services, providing basic statistical analysis as a fallback.

---

## IDocumentQualityAnalyzer

### Interface Overview

```csharp
public interface IDocumentQualityAnalyzer
{
    // Full document quality analysis
    Task<DocumentQualityReport> AnalyzeQualityAsync(
        string filePath,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);

    // Evaluate existing chunks
    Task<ChunkingQualityMetrics> EvaluateChunksAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default);

    // Generate Q&A benchmark
    Task<QABenchmark> GenerateQABenchmarkAsync(
        string filePath,
        int questionCount = 20,
        CancellationToken cancellationToken = default);

    // Compare strategies
    Task<QualityBenchmarkResult> BenchmarkChunkingAsync(
        string filePath,
        string[] strategies,
        CancellationToken cancellationToken = default);
}
```

### Service Registration

```csharp
// IDocumentQualityAnalyzer is registered automatically with AddFileFlux()
services.AddFileFlux();

// Get analyzer from DI
var analyzer = serviceProvider.GetRequiredService<IDocumentQualityAnalyzer>();
```

---

## Feature 1: Full Quality Analysis

### Purpose
Comprehensive quality assessment of document processing for RAG optimization.

### Usage

```csharp
var analyzer = serviceProvider.GetRequiredService<IDocumentQualityAnalyzer>();

// Analyze with default options
var report = await analyzer.AnalyzeQualityAsync("document.pdf");

// Or specify custom chunking options
var options = new ChunkingOptions
{
    Strategy = ChunkingStrategies.Intelligent,
    MaxChunkSize = 512,
    OverlapSize = 64
};
var report = await analyzer.AnalyzeQualityAsync("document.pdf", options);
```

### Report Structure

```csharp
public class DocumentQualityReport
{
    // Overall quality score (0.0-1.0)
    double OverallQualityScore;

    // Chunking quality metrics
    ChunkingQualityMetrics ChunkingQuality;

    // Individual chunk metrics
    List<ChunkMetric> ChunkMetrics;

    // Processing statistics
    ProcessingStatistics Statistics;

    // Recommendations for improvement
    List<QualityRecommendation> Recommendations;
}
```

### Example: Interpreting Results

```csharp
var report = await analyzer.AnalyzeQualityAsync("technical-doc.pdf");

Console.WriteLine($"Overall Quality: {report.OverallQualityScore:P2}");
// Output: "Overall Quality: 78.50%"

Console.WriteLine($"Completeness: {report.ChunkingQuality.AverageCompleteness:P2}");
// Output: "Completeness: 82.30%"

Console.WriteLine($"Consistency: {report.ChunkingQuality.ContentConsistency:P2}");
// Output: "Consistency: 75.60%"

Console.WriteLine($"Boundary Quality: {report.ChunkingQuality.BoundaryQuality:P2}");
// Output: "Boundary Quality: 77.40%"

// Check individual chunks
foreach (var chunkMetric in report.ChunkMetrics)
{
    if (chunkMetric.QualityScore < 0.7)
    {
        Console.WriteLine($"Low quality chunk {chunkMetric.ChunkIndex}: {chunkMetric.Issues}");
    }
}

// Review recommendations
foreach (var rec in report.Recommendations)
{
    Console.WriteLine($"[{rec.Priority}] {rec.Type}: {rec.Description}");
    if (rec.SuggestedParameters.Any())
    {
        Console.WriteLine($"  Suggested: {string.Join(", ", rec.SuggestedParameters)}");
    }
}
```

---

## Feature 2: Chunk Evaluation

### Purpose
Evaluate quality of pre-generated chunks (useful for A/B testing different approaches).

### Usage

```csharp
// Process document with different methods
var chunks1 = await processor.ProcessAsync("doc.pdf", options1);
var chunks2 = await processor.ProcessAsync("doc.pdf", options2);

// Compare quality
var metrics1 = await analyzer.EvaluateChunksAsync(chunks1);
var metrics2 = await analyzer.EvaluateChunksAsync(chunks2);

if (metrics1.OverallScore > metrics2.OverallScore)
{
    Console.WriteLine("Options1 produced better chunks");
}
```

### ChunkingQualityMetrics Structure

```csharp
public class ChunkingQualityMetrics
{
    // Average chunk completeness (0.0-1.0)
    double AverageCompleteness;

    // Content consistency across chunks (0.0-1.0)
    double ContentConsistency;

    // Boundary detection quality (0.0-1.0)
    double BoundaryQuality;

    // Overall quality score
    double OverallScore;

    // Statistical measures
    double AverageChunkSize;
    double ChunkSizeStdDev;
    int TotalChunks;

    // Detailed metrics per chunk
    List<ChunkQualityScore> ChunkScores;
}
```

### Quality Dimensions Explained

| Metric | What It Measures | Target Range |
|--------|------------------|--------------|
| **AverageCompleteness** | How complete each chunk is as a standalone unit | > 0.75 |
| **ContentConsistency** | Similarity and coherence between chunks | > 0.70 |
| **BoundaryQuality** | How well chunk boundaries align with semantic units | > 0.80 |
| **OverallScore** | Weighted average of all metrics | > 0.75 |

### Improving Scores

**Low Completeness (< 0.7):**
- Increase `MaxChunkSize`
- Use `Intelligent` or `Semantic` strategy
- Adjust `OverlapSize` for more context

**Low Consistency (< 0.65):**
- Enable metadata enrichment
- Use `Smart` strategy for structured documents
- Check document quality (malformed PDFs, etc.)

**Low Boundary Quality (< 0.75):**
- Switch to structure-aware strategy (`Smart`, `Intelligent`)
- For Markdown: Use `Paragraph` strategy
- Enable statistical boundary detection

---

## Feature 3: Q&A Benchmark Generation

### Purpose
Generate question-answer pairs to test RAG retrieval quality.

**Requires:** `ITextCompletionService` to be registered (AI-powered feature)

### Usage

```csharp
// Generate 20 Q&A pairs (default)
var benchmark = await analyzer.GenerateQABenchmarkAsync("document.pdf");

// Or specify custom count
var benchmark = await analyzer.GenerateQABenchmarkAsync(
    "document.pdf",
    questionCount: 50);
```

### QABenchmark Structure

```csharp
public class QABenchmark
{
    // Generated Q&A pairs
    List<QAPair> Questions;

    // Benchmark metadata
    string DocumentPath;
    int TotalQuestions;
    DateTime GeneratedAt;

    // Quality metrics
    double AnswerabilityScore;        // How well questions can be answered
    Dictionary<string, int> QuestionTypes;  // Distribution by type

    // Chunk coverage
    double ChunkCoveragePercentage;   // % of chunks with questions
}

public class QAPair
{
    string Question;
    string Answer;
    string QuestionType;              // Factual, Conceptual, Analytical, etc.
    List<int> RelevantChunkIds;       // Chunks needed to answer
    double DifficultyScore;           // 0.0 (easy) to 1.0 (hard)
}
```

### Using Q&A Benchmarks

```csharp
var benchmark = await analyzer.GenerateQABenchmarkAsync("technical-manual.pdf");

Console.WriteLine($"Generated {benchmark.TotalQuestions} questions");
Console.WriteLine($"Answerability: {benchmark.AnswerabilityScore:P2}");
Console.WriteLine($"Chunk Coverage: {benchmark.ChunkCoveragePercentage:P2}");

// Test your RAG system
foreach (var qa in benchmark.Questions)
{
    Console.WriteLine($"\nQuestion ({qa.QuestionType}): {qa.Question}");
    Console.WriteLine($"Expected Answer: {qa.Answer}");
    Console.WriteLine($"Relevant Chunks: {string.Join(", ", qa.RelevantChunkIds)}");

    // 1. Query your RAG system
    var ragAnswer = await YourRagSystem.QueryAsync(qa.Question);

    // 2. Compare with expected answer
    var similarity = CalculateSimilarity(ragAnswer, qa.Answer);

    // 3. Verify correct chunks were retrieved
    var retrievedChunks = await YourRagSystem.GetRetrievedChunksAsync(qa.Question);
    var correctRetrieval = retrievedChunks.Intersect(qa.RelevantChunkIds).Count();

    Console.WriteLine($"Retrieval Accuracy: {correctRetrieval}/{qa.RelevantChunkIds.Count}");
}
```

### Question Types Generated

| Type | Description | Example |
|------|-------------|---------|
| **Factual** | Direct information recall | "What is the maximum timeout value?" |
| **Conceptual** | Understanding of concepts | "How does the caching mechanism work?" |
| **Analytical** | Analysis and comparison | "What are the trade-offs between Strategy A and B?" |
| **Procedural** | Step-by-step processes | "What steps are needed to configure the system?" |
| **Comparative** | Comparisons | "How does this approach differ from alternatives?" |

---

## Feature 4: Strategy Benchmarking

### Purpose
Compare multiple chunking strategies to find the optimal one for your document type.

### Usage

```csharp
// Define strategies to test
var strategies = new[]
{
    ChunkingStrategies.Intelligent,
    ChunkingStrategies.Semantic,
    ChunkingStrategies.Smart,
    ChunkingStrategies.Paragraph
};

// Run benchmark
var benchmark = await analyzer.BenchmarkChunkingAsync(
    "document.pdf",
    strategies);

// Review results
Console.WriteLine($"Recommended Strategy: {benchmark.RecommendedStrategy}");

foreach (var result in benchmark.Results.OrderByDescending(r => r.QualityScore))
{
    Console.WriteLine($"\n{result.Strategy}:");
    Console.WriteLine($"  Quality: {result.QualityScore:P2}");
    Console.WriteLine($"  Processing Time: {result.ProcessingTime.TotalSeconds:F2}s");
    Console.WriteLine($"  Chunk Count: {result.ChunkCount}");
    Console.WriteLine($"  Avg Chunk Size: {result.AverageChunkSize:F0} chars");
}
```

### BenchmarkResult Structure

```csharp
public class QualityBenchmarkResult
{
    // Recommended strategy based on quality and performance
    string RecommendedStrategy;

    // Individual strategy results
    List<StrategyResult> Results;

    // Comparative analysis
    Dictionary<string, double> QualityComparison;
    Dictionary<string, TimeSpan> PerformanceComparison;
}

public class StrategyResult
{
    string Strategy;
    double QualityScore;
    TimeSpan ProcessingTime;
    int ChunkCount;
    double AverageChunkSize;
    ChunkingQualityMetrics DetailedMetrics;
}
```

### Interpreting Benchmark Results

```csharp
var benchmark = await analyzer.BenchmarkChunkingAsync("api-docs.md", strategies);

// Find best quality
var bestQuality = benchmark.Results
    .OrderByDescending(r => r.QualityScore)
    .First();
Console.WriteLine($"Best Quality: {bestQuality.Strategy} ({bestQuality.QualityScore:P2})");

// Find fastest
var fastest = benchmark.Results
    .OrderBy(r => r.ProcessingTime)
    .First();
Console.WriteLine($"Fastest: {fastest.Strategy} ({fastest.ProcessingTime.TotalMilliseconds:F0}ms)");

// Find balanced (quality/speed ratio)
var balanced = benchmark.Results
    .OrderByDescending(r => r.QualityScore / r.ProcessingTime.TotalSeconds)
    .First();
Console.WriteLine($"Best Balanced: {balanced.Strategy}");

// Decision logic
if (bestQuality.QualityScore - balanced.QualityScore < 0.05)
{
    Console.WriteLine($"Recommendation: Use {balanced.Strategy} (minimal quality loss, much faster)");
}
else
{
    Console.WriteLine($"Recommendation: Use {bestQuality.Strategy} (quality difference significant)");
}
```

---

## With vs Without AI Services

### Without ITextCompletionService (Basic Mode)

**Available Metrics:**
- ✅ Statistical analysis (chunk sizes, boundaries)
- ✅ Structural quality (headings, paragraphs)
- ✅ Basic completeness (text completeness)
- ✅ Size distribution metrics
- ❌ Semantic quality assessment
- ❌ Q&A benchmark generation
- ❌ AI-powered recommendations

```csharp
// No AI service registered
services.AddFileFlux(); // No ITextCompletionService

var analyzer = provider.GetRequiredService<IDocumentQualityAnalyzer>();
var report = await analyzer.AnalyzeQualityAsync("doc.pdf");

// report contains statistical metrics only
// Recommendations are rule-based
```

### With ITextCompletionService (AI-Enhanced Mode)

**Available Metrics:**
- ✅ All basic metrics
- ✅ Semantic quality assessment
- ✅ Content relevance scoring
- ✅ Q&A benchmark generation
- ✅ AI-powered recommendations
- ✅ Context preservation analysis

```csharp
// AI service registered
services.AddScoped<ITextCompletionService, MyAIService>();
services.AddFileFlux();

var analyzer = provider.GetRequiredService<IDocumentQualityAnalyzer>();
var report = await analyzer.AnalyzeQualityAsync("doc.pdf");

// report contains AI-enhanced metrics
// Can generate Q&A benchmarks
var qaBenchmark = await analyzer.GenerateQABenchmarkAsync("doc.pdf");
```

---

## Performance Considerations

### Analysis Cost

| Operation | Without AI | With AI | Typical Time |
|-----------|-----------|---------|--------------|
| AnalyzeQualityAsync | Low (statistical only) | High (LLM calls) | 0.5s vs 5-10s |
| EvaluateChunksAsync | Very Low | Medium | 0.1s vs 2-5s |
| GenerateQABenchmarkAsync | N/A (requires AI) | High | 10-30s |
| BenchmarkChunkingAsync | Medium | Very High | 2s vs 30-60s |

### Optimization Tips

1. **Cache Results**: Quality analysis for the same document rarely changes
2. **Limit Strategies**: Benchmark 2-3 strategies, not all 7
3. **Reduce Q&A Count**: 10-20 questions often sufficient for validation
4. **Parallel Benchmarking**: FileFlux supports concurrent strategy evaluation
5. **Sampling**: For large documents, analyze representative sections

```csharp
// Efficient benchmarking
var strategies = new[]
{
    ChunkingStrategies.Auto,  // Let FileFlux choose
    ChunkingStrategies.Intelligent,  // Your current choice
    ChunkingStrategies.Semantic  // Alternative
};

// Benchmark these 3 instead of all 7
var result = await analyzer.BenchmarkChunkingAsync("doc.pdf", strategies);
```

---

## Real-World Examples

### Example 1: Optimizing Technical Documentation

```csharp
// You have API documentation and want best chunking for RAG
var strategies = new[]
{
    ChunkingStrategies.Intelligent,  // Good for code + text
    ChunkingStrategies.Smart,        // Structure-aware
    ChunkingStrategies.Paragraph     // Simple sections
};

var benchmark = await analyzer.BenchmarkChunkingAsync(
    "api-reference.md",
    strategies);

// Intelligent wins for technical docs
// Quality: 0.82, Time: 2.3s, Chunks: 47
```

### Example 2: Validating RAG Retrieval

```csharp
// Generate Q&A benchmark
var qaBenchmark = await analyzer.GenerateQABenchmarkAsync(
    "user-manual.pdf",
    questionCount: 25);

// Test your RAG system
int correctRetrievals = 0;
foreach (var qa in qaBenchmark.Questions)
{
    var retrievedChunks = await ragSystem.RetrieveAsync(qa.Question);
    if (retrievedChunks.Any(c => qa.RelevantChunkIds.Contains(c.Id)))
    {
        correctRetrievals++;
    }
}

double accuracy = (double)correctRetrievals / qaBenchmark.TotalQuestions;
Console.WriteLine($"RAG Retrieval Accuracy: {accuracy:P2}");
// Target: > 80% for good RAG performance
```

### Example 3: Continuous Quality Monitoring

```csharp
// Monitor quality across document updates
public async Task<QualityTrend> MonitorQuality(string filePath)
{
    var report = await analyzer.AnalyzeQualityAsync(filePath);

    // Store in database
    await db.QualityReports.AddAsync(new
    {
        FilePath = filePath,
        Quality = report.OverallQualityScore,
        Timestamp = DateTime.UtcNow,
        Strategy = report.Statistics.StrategyUsed
    });

    // Alert if quality drops
    var previousReport = await db.QualityReports
        .Where(r => r.FilePath == filePath)
        .OrderByDescending(r => r.Timestamp)
        .Skip(1)
        .FirstOrDefaultAsync();

    if (previousReport != null &&
        report.OverallQualityScore < previousReport.Quality - 0.1)
    {
        await SendAlert($"Quality dropped from {previousReport.Quality:P2} to {report.OverallQualityScore:P2}");
    }

    return new QualityTrend { /* ... */ };
}
```

---

## Troubleshooting

### Low Quality Scores

**Problem:** Overall quality consistently below 0.70

**Diagnosis:**
```csharp
var report = await analyzer.AnalyzeQualityAsync("doc.pdf");

// Check which metric is low
if (report.ChunkingQuality.AverageCompleteness < 0.7)
    Console.WriteLine("Issue: Chunks are incomplete");
if (report.ChunkingQuality.ContentConsistency < 0.65)
    Console.WriteLine("Issue: Inconsistent content");
if (report.ChunkingQuality.BoundaryQuality < 0.75)
    Console.WriteLine("Issue: Poor boundary detection");
```

**Solutions:**
- Try different chunking strategy
- Adjust chunk size parameters
- Check document quality (scanned PDFs, etc.)
- Enable AI services for better analysis

---

### Q&A Generation Fails

**Problem:** `GenerateQABenchmarkAsync` throws or returns empty

**Causes:**
1. ITextCompletionService not registered
2. Document too short (< 500 characters)
3. AI service unavailable
4. Unsupported document format

**Solutions:**
```csharp
// 1. Verify AI service is registered
var textService = provider.GetService<ITextCompletionService>();
if (textService == null)
{
    Console.WriteLine("Error: ITextCompletionService not registered");
    // Register your AI service
}

// 2. Check service availability
if (!await textService.IsAvailableAsync())
{
    Console.WriteLine("Error: AI service unavailable");
    // Check API keys, network, etc.
}

// 3. Verify document is processable
var chunks = await processor.ProcessAsync("doc.pdf");
if (chunks.Count == 0)
{
    Console.WriteLine("Error: Document produced no chunks");
}
```

---

## Related Documentation

- [ITextCompletionService Integration](../integration/text-completion-service.md) - AI service setup
- [Chunking Strategies](../features/chunking-strategies.md) - Strategy details
- [Mock Implementations](../testing/mock-implementations.md) - Testing without AI

---

## Summary

**Quality Analysis helps you:**
- 📊 **Measure** objective quality metrics
- 🔍 **Compare** different approaches
- 🎯 **Optimize** for your specific use case
- ✅ **Validate** RAG system performance

**Getting Started:**
1. Register `IDocumentQualityAnalyzer` (automatic with AddFileFlux())
2. Run quality analysis on sample documents
3. Compare different strategies
4. Implement recommendations
5. Monitor quality over time

**Pro Tips:**
- Start with `Auto` strategy and benchmark others
- Use Q&A benchmarks to validate RAG retrieval
- Cache quality reports for identical documents
- Monitor quality trends in production
- Target > 0.75 overall quality for good RAG performance
