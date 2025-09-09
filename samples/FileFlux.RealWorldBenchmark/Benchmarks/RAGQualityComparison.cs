using FileFlux;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Strategies;
using FileFlux.RealWorldBenchmark.Services;
using System.Text.Json;

namespace FileFlux.RealWorldBenchmark.Benchmarks;

/// <summary>
/// RAG 품질 비교 벤치마크 - SmartChunking vs 기존 전략들
/// </summary>
public class RAGQualityComparison
{
    private readonly IDocumentProcessor _processor;
    private readonly OpenAiEmbeddingService _embeddingService;
    private readonly List<string> _testStrategies;
    private readonly Dictionary<string, List<TestResult>> _results;

    public RAGQualityComparison(IDocumentProcessor processor, OpenAiEmbeddingService embeddingService)
    {
        _processor = processor;
        _embeddingService = embeddingService;
        _testStrategies = new[] { "Smart", "Intelligent", "Semantic", "FixedSize" }.ToList();
        _results = new Dictionary<string, List<TestResult>>();
    }

    /// <summary>
    /// 청킹 전략별 RAG 품질 비교 실행
    /// </summary>
    public async Task<ComparisonReport> RunComparisonAsync(string testDocument, List<string> testQueries, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("🚀 RAG Quality Comparison Started");
        Console.WriteLine("=================================");
        
        var report = new ComparisonReport
        {
            TestDocument = Path.GetFileName(testDocument),
            TestQueries = testQueries,
            StrategyResults = new Dictionary<string, StrategyResult>(),
            CompletedAt = DateTime.UtcNow
        };

        // 각 전략별로 테스트 실행
        foreach (var strategy in _testStrategies)
        {
            Console.WriteLine($"\n🔍 Testing Strategy: {strategy}");
            Console.WriteLine($"{"".PadLeft(40, '-')}");
            
            try
            {
                var strategyResult = await TestStrategyAsync(testDocument, testQueries, strategy, cancellationToken);
                report.StrategyResults[strategy] = strategyResult;
                
                Console.WriteLine($"✅ {strategy} Complete - Avg Quality: {strategyResult.AverageQualityScore:F2}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ {strategy} Failed: {ex.Message}");
                report.StrategyResults[strategy] = new StrategyResult
                {
                    StrategyName = strategy,
                    Error = ex.Message,
                    TestResults = new List<QueryTestResult>()
                };
            }
        }

        // 결과 분석
        AnalyzeResults(report);
        
        Console.WriteLine("\n📊 Comparison Complete!");
        return report;
    }

    /// <summary>
    /// 특정 전략으로 테스트 실행
    /// </summary>
    private async Task<StrategyResult> TestStrategyAsync(string testDocument, List<string> testQueries, string strategy, CancellationToken cancellationToken)
    {
        // 1. 문서 처리
        var options = new ChunkingOptions
        {
            Strategy = strategy,
            MaxChunkSize = 512,
            OverlapSize = 64
        };

        var chunkList = new List<DocumentChunk>();
        await foreach (var chunk in _processor.ProcessAsync(testDocument, options, cancellationToken))
        {
            chunkList.Add(chunk);
        }
        
        Console.WriteLine($"   Chunks created: {chunkList.Count}");

        // 2. 임베딩 생성
        var embeddedChunks = new List<EmbeddedChunk>();
        foreach (var chunk in chunkList)
        {
            var embedding = await _embeddingService.GenerateEmbeddingAsync(chunk.Content, cancellationToken);
            embeddedChunks.Add(new EmbeddedChunk
            {
                Chunk = chunk,
                Embedding = embedding
            });
        }

        // 3. 각 쿼리별 검색 테스트
        var queryResults = new List<QueryTestResult>();
        
        for (int i = 0; i < testQueries.Count; i++)
        {
            var query = testQueries[i];
            Console.WriteLine($"   Query {i + 1}/{testQueries.Count}: {TruncateString(query, 50)}");
            
            var queryResult = await TestQueryAsync(query, embeddedChunks, strategy, cancellationToken);
            queryResults.Add(queryResult);
        }

        // 4. 전략별 메트릭 계산
        var strategyResult = new StrategyResult
        {
            StrategyName = strategy,
            TotalChunks = chunkList.Count,
            AverageChunkSize = chunkList.Average(c => c.Content.Length),
            TestResults = queryResults,
            AverageQualityScore = queryResults.Average(r => r.QualityScore),
            AverageRetrievalScore = queryResults.Average(r => r.RetrievalScore),
            AverageRelevanceScore = queryResults.Average(r => r.RelevanceScore)
        };

        // 5. 청크 품질 메트릭 추가
        await CalculateChunkQualityMetrics(strategyResult, chunkList);

        return strategyResult;
    }

    /// <summary>
    /// 쿼리별 검색 테스트
    /// </summary>
    private async Task<QueryTestResult> TestQueryAsync(string query, List<EmbeddedChunk> embeddedChunks, string strategy, CancellationToken cancellationToken)
    {
        // 쿼리 임베딩 생성
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        
        // 코사인 유사도 계산
        var similarities = embeddedChunks.Select(ec => new ChunkSimilarity
        {
            Chunk = ec.Chunk,
            Similarity = CosineSimilarity(queryEmbedding, ec.Embedding)
        }).OrderByDescending(cs => cs.Similarity).ToList();

        // Top-K 청크 선택 (K=3)
        var topChunks = similarities.Take(3).ToList();
        
        // 품질 메트릭 계산
        var qualityScore = CalculateQualityScore(topChunks);
        var retrievalScore = CalculateRetrievalScore(topChunks, query);
        var relevanceScore = CalculateRelevanceScore(topChunks, query);

        return new QueryTestResult
        {
            Query = query,
            Strategy = strategy,
            TopChunks = topChunks,
            QualityScore = qualityScore,
            RetrievalScore = retrievalScore,
            RelevanceScore = relevanceScore,
            AvgSimilarity = topChunks.Average(tc => tc.Similarity),
            AvgChunkLength = topChunks.Average(tc => tc.Chunk.Content.Length)
        };
    }

    /// <summary>
    /// 청크 품질 메트릭 계산
    /// </summary>
    private async Task CalculateChunkQualityMetrics(StrategyResult result, List<DocumentChunk> chunks)
    {
        // 청크 완성도 (문장 경계 보존)
        var completeness = chunks.Average(c => CalculateCompletenessScore(c.Content));
        
        // 문장 무결성 (중간에 끊어진 문장이 없는지)
        var integrity = chunks.Average(c => CalculateSentenceIntegrity(c.Content));
        
        // 정보 밀도 (단위 길이당 정보량)
        var density = chunks.Average(c => CalculateInformationDensity(c.Content));

        result.ChunkCompleteness = completeness;
        result.SentenceIntegrity = integrity;
        result.InformationDensity = density;
        
        await Task.CompletedTask; // 비동기 호환
    }

    /// <summary>
    /// 문장 완성도 계산
    /// </summary>
    private double CalculateCompletenessScore(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0.0;

        var sentences = content.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        if (sentences.Length == 0) return 0.0;

        var completeSentences = 0;
        foreach (var sentence in sentences)
        {
            var trimmed = sentence.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                // 완전한 문장: "..." 로 끝나지 않고, 문자나 숫자로 끝나지 않음
                if (!trimmed.EndsWith("...") && !char.IsLetterOrDigit(trimmed.LastOrDefault()))
                {
                    completeSentences++;
                }
            }
        }

        return (double)completeSentences / sentences.Length;
    }

    /// <summary>
    /// 문장 무결성 계산
    /// </summary>
    private double CalculateSentenceIntegrity(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0.0;

        var trimmed = content.Trim();
        
        // 미완성 표시가 있으면 점수 하락
        if (trimmed.Contains("...") && !trimmed.Contains("etc."))
            return 0.5;
            
        // 문장이 중간에 끊겼는지 확인
        var lastChar = trimmed.LastOrDefault();
        if (char.IsLetterOrDigit(lastChar) || lastChar == ',')
            return 0.3;
            
        return 1.0;
    }

    /// <summary>
    /// 정보 밀도 계산
    /// </summary>
    private double CalculateInformationDensity(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0.0;

        var words = content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var uniqueWords = words.Where(w => w.Length > 3).Distinct().Count();
        var sentences = content.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries).Length;
        
        // 정보 밀도 = (고유 단어 수 + 문장 수) / 총 문자 수 * 1000
        return (uniqueWords + sentences) * 1000.0 / content.Length;
    }

    /// <summary>
    /// 코사인 유사도 계산
    /// </summary>
    private double CosineSimilarity(float[] vec1, float[] vec2)
    {
        if (vec1.Length != vec2.Length)
            return 0.0;

        var dotProduct = vec1.Zip(vec2, (a, b) => a * b).Sum();
        var norm1 = Math.Sqrt(vec1.Sum(a => a * a));
        var norm2 = Math.Sqrt(vec2.Sum(b => b * b));

        if (norm1 == 0 || norm2 == 0)
            return 0.0;

        return dotProduct / (norm1 * norm2);
    }

    /// <summary>
    /// 품질 점수 계산
    /// </summary>
    private double CalculateQualityScore(List<ChunkSimilarity> topChunks)
    {
        if (topChunks.Count == 0) return 0.0;

        // 청크 품질 점수 평균
        return topChunks.Average(tc => tc.Chunk.QualityScore);
    }

    /// <summary>
    /// 검색 점수 계산
    /// </summary>
    private double CalculateRetrievalScore(List<ChunkSimilarity> topChunks, string query)
    {
        if (topChunks.Count == 0) return 0.0;

        // 유사도 점수에 위치 가중치 적용 (상위 청크에 더 높은 가중치)
        var scores = new List<double>();
        for (int i = 0; i < topChunks.Count; i++)
        {
            var weight = 1.0 - (i * 0.2); // 1.0, 0.8, 0.6
            scores.Add(topChunks[i].Similarity * weight);
        }

        return scores.Average();
    }

    /// <summary>
    /// 관련성 점수 계산
    /// </summary>
    private double CalculateRelevanceScore(List<ChunkSimilarity> topChunks, string query)
    {
        if (topChunks.Count == 0) return 0.0;

        // 쿼리 키워드와 청크 내용의 일치도
        var queryWords = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var scores = new List<double>();

        foreach (var chunkSim in topChunks)
        {
            var chunkWords = chunkSim.Chunk.Content.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var matches = queryWords.Count(qw => chunkWords.Contains(qw));
            var relevance = queryWords.Length > 0 ? (double)matches / queryWords.Length : 0.0;
            scores.Add(relevance);
        }

        return scores.Average();
    }

    /// <summary>
    /// 결과 분석 및 순위 매기기
    /// </summary>
    private void AnalyzeResults(ComparisonReport report)
    {
        Console.WriteLine("\n📊 Analysis Results");
        Console.WriteLine("==================");

        // 전체 품질 점수로 순위 매기기
        var rankings = report.StrategyResults
            .Where(kvp => string.IsNullOrEmpty(kvp.Value.Error))
            .OrderByDescending(kvp => kvp.Value.AverageQualityScore)
            .ToList();

        Console.WriteLine("\n🏆 Overall Quality Ranking:");
        for (int i = 0; i < rankings.Count; i++)
        {
            var result = rankings[i].Value;
            var icon = i == 0 ? "🥇" : i == 1 ? "🥈" : i == 2 ? "🥉" : "📊";
            
            Console.WriteLine($"{icon} {i + 1}. {result.StrategyName}");
            Console.WriteLine($"   Quality: {result.AverageQualityScore:F3}");
            Console.WriteLine($"   Retrieval: {result.AverageRetrievalScore:F3}");
            Console.WriteLine($"   Completeness: {result.ChunkCompleteness:F3}");
            Console.WriteLine($"   Integrity: {result.SentenceIntegrity:F3}");
            Console.WriteLine();
        }

        // 세부 분석
        var bestStrategy = rankings.FirstOrDefault();
        if (bestStrategy.Value != null)
        {
            Console.WriteLine($"🎯 Best Strategy: {bestStrategy.Value.StrategyName}");
            Console.WriteLine($"   Chunks: {bestStrategy.Value.TotalChunks}");
            Console.WriteLine($"   Avg Size: {bestStrategy.Value.AverageChunkSize:F0} chars");
            Console.WriteLine($"   Quality: {bestStrategy.Value.AverageQualityScore:F3}");
        }
    }

    private string TruncateString(string input, int maxLength)
    {
        if (input.Length <= maxLength) return input;
        return input.Substring(0, maxLength - 3) + "...";
    }
}

// 지원 클래스들
public class EmbeddedChunk
{
    public DocumentChunk Chunk { get; set; } = null!;
    public float[] Embedding { get; set; } = null!;
}

public class ChunkSimilarity
{
    public DocumentChunk Chunk { get; set; } = null!;
    public double Similarity { get; set; }
}

public class QueryTestResult
{
    public string Query { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public List<ChunkSimilarity> TopChunks { get; set; } = new();
    public double QualityScore { get; set; }
    public double RetrievalScore { get; set; }
    public double RelevanceScore { get; set; }
    public double AvgSimilarity { get; set; }
    public double AvgChunkLength { get; set; }
}

public class StrategyResult
{
    public string StrategyName { get; set; } = string.Empty;
    public int TotalChunks { get; set; }
    public double AverageChunkSize { get; set; }
    public List<QueryTestResult> TestResults { get; set; } = new();
    public double AverageQualityScore { get; set; }
    public double AverageRetrievalScore { get; set; }
    public double AverageRelevanceScore { get; set; }
    public double ChunkCompleteness { get; set; }
    public double SentenceIntegrity { get; set; }
    public double InformationDensity { get; set; }
    public string? Error { get; set; }
}

public class ComparisonReport
{
    public string TestDocument { get; set; } = string.Empty;
    public List<string> TestQueries { get; set; } = new();
    public Dictionary<string, StrategyResult> StrategyResults { get; set; } = new();
    public DateTime CompletedAt { get; set; }
}

public class TestResult
{
    public string StrategyName { get; set; } = string.Empty;
    public double QualityScore { get; set; }
    public double CompletenessScore { get; set; }
    public double RetrievalAccuracy { get; set; }
    public int ChunkCount { get; set; }
    public double AvgChunkSize { get; set; }
}