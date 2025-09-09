using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using FileFlux;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FileFlux.Infrastructure.Services;

namespace FileFlux.RealWorldBenchmark.Benchmarks;

/// <summary>
/// Advanced performance benchmarking using BenchmarkDotNet
/// </summary>
[MemoryDiagnoser]
[ThreadingDiagnoser]
[SimpleJob(RunStrategy.Monitoring, iterationCount: 10)]
public class PerformanceBenchmarker
{
    private IDocumentProcessor _processor;
    private List<string> _testFiles;
    private readonly Consumer _consumer = new Consumer();
    
    [Params("Intelligent", "Semantic", "Paragraph", "FixedSize")]
    public string Strategy { get; set; }
    
    [Params(256, 512, 1024)]
    public int ChunkSize { get; set; }
    
    [Params(32, 64, 128)]
    public int OverlapSize { get; set; }
    
    [GlobalSetup]
    public void Setup()
    {
        _processor = CreateProcessor();
        _testFiles = DiscoverTestFiles();
    }
    
    [Benchmark(Baseline = true)]
    public async Task ProcessSingleFile()
    {
        if (!_testFiles.Any()) return;
        
        var options = new ChunkingOptions
        {
            Strategy = Strategy,
            MaxChunkSize = ChunkSize,
            OverlapSize = OverlapSize
        };
        
        await foreach (var chunk in _processor.ProcessAsync(_testFiles.First(), options))
        {
            _consumer.Consume(chunk);
        }
    }
    
    [Benchmark]
    public async Task ProcessMultipleFilesSequential()
    {
        if (_testFiles.Count < 3) return;
        
        var options = new ChunkingOptions
        {
            Strategy = Strategy,
            MaxChunkSize = ChunkSize,
            OverlapSize = OverlapSize
        };
        
        foreach (var file in _testFiles.Take(3))
        {
            await foreach (var chunk in _processor.ProcessAsync(file, options))
            {
                _consumer.Consume(chunk);
            }
        }
    }
    
    [Benchmark]
    public async Task ProcessWithMemoryEfficiency()
    {
        if (!_testFiles.Any()) return;
        
        var memoryProcessor = new FileFlux.Infrastructure.Optimization.MemoryEfficientProcessor(
            _processor, 
            new FileFlux.Infrastructure.Caching.LruMemoryCache(100));
        
        var options = new ChunkingOptions
        {
            Strategy = Strategy,
            MaxChunkSize = ChunkSize,
            OverlapSize = OverlapSize
        };
        
        await foreach (var chunk in memoryProcessor.ProcessStreamAsync(_testFiles.First(), options))
        {
            _consumer.Consume(chunk);
        }
    }
    
    [Benchmark]
    public async Task ProcessWithParallelism()
    {
        if (_testFiles.Count < 3) return;
        
        var parallelProcessor = new FileFlux.Infrastructure.Optimization.ParallelBatchProcessor(_processor);
        
        var options = new ChunkingOptions
        {
            Strategy = Strategy,
            MaxChunkSize = ChunkSize,
            OverlapSize = OverlapSize
        };
        
        var result = await parallelProcessor.ProcessBatchAsync(_testFiles.Take(3), options);
        _consumer.Consume(result);
    }
    
    [Benchmark]
    public async Task ProcessLargeFile()
    {
        var largeFile = _testFiles.OrderByDescending(f => new System.IO.FileInfo(f).Length).FirstOrDefault();
        if (largeFile == null) return;
        
        var options = new ChunkingOptions
        {
            Strategy = Strategy,
            MaxChunkSize = ChunkSize,
            OverlapSize = OverlapSize
        };
        
        await foreach (var chunk in _processor.ProcessAsync(largeFile, options))
        {
            _consumer.Consume(chunk);
        }
    }
    
    [Benchmark]
    public async Task ProcessWithCaching()
    {
        if (!_testFiles.Any()) return;
        
        var cache = new FileFlux.Infrastructure.Caching.LruMemoryCache(100);
        var cachedProcessor = new FileFlux.Infrastructure.Optimization.MemoryEfficientProcessor(_processor, cache);
        
        var options = new ChunkingOptions
        {
            Strategy = Strategy,
            MaxChunkSize = ChunkSize,
            OverlapSize = OverlapSize
        };
        
        // First pass (cache miss)
        await foreach (var chunk in cachedProcessor.ProcessStreamAsync(_testFiles.First(), options))
        {
            _consumer.Consume(chunk);
        }
        
        // Second pass (cache hit)
        await foreach (var chunk in cachedProcessor.ProcessStreamAsync(_testFiles.First(), options))
        {
            _consumer.Consume(chunk);
        }
    }
    
    private IDocumentProcessor CreateProcessor()
    {
        var textService = new MockTextCompletionService();
        var readerFactory = new FileFlux.Infrastructure.Factories.DocumentReaderFactory();
        var parserFactory = new FileFlux.Infrastructure.Factories.DocumentParserFactory(textService);
        var strategyFactory = new FileFlux.Infrastructure.Factories.ChunkingStrategyFactory();
        
        return new FileFlux.Infrastructure.DocumentProcessor(
            readerFactory, parserFactory, strategyFactory);
    }
    
    private List<string> DiscoverTestFiles()
    {
        var testPath = @"D:\data\FileFlux\test";
        var files = new List<string>();
        
        if (System.IO.Directory.Exists(testPath))
        {
            files.AddRange(System.IO.Directory.GetFiles(testPath, "*.pdf", System.IO.SearchOption.AllDirectories));
            files.AddRange(System.IO.Directory.GetFiles(testPath, "*.docx", System.IO.SearchOption.AllDirectories));
            files.AddRange(System.IO.Directory.GetFiles(testPath, "*.md", System.IO.SearchOption.AllDirectories));
        }
        
        return files.Take(10).ToList(); // Limit for benchmarking
    }
}

/// <summary>
/// Detailed performance profiler for granular analysis
/// </summary>
public class DetailedPerformanceProfiler
{
    private readonly IDocumentProcessor _processor;
    private readonly Dictionary<string, List<TimeSpan>> _timings;
    private readonly Dictionary<string, List<long>> _memoryUsage;
    
    public DetailedPerformanceProfiler(IDocumentProcessor processor)
    {
        _processor = processor;
        _timings = new Dictionary<string, List<TimeSpan>>();
        _memoryUsage = new Dictionary<string, List<long>>();
    }
    
    public async Task<PerformanceProfile> ProfileAsync(
        string filePath, 
        ChunkingOptions options,
        int iterations = 5)
    {
        var profile = new PerformanceProfile
        {
            FilePath = filePath,
            FileSize = new System.IO.FileInfo(filePath).Length,
            Strategy = options.Strategy,
            ChunkSize = options.MaxChunkSize,
            IterationCount = iterations
        };
        
        // Warmup
        await WarmupAsync(filePath, options);
        
        // Profile each stage
        for (int i = 0; i < iterations; i++)
        {
            await ProfileIterationAsync(filePath, options, profile);
        }
        
        // Calculate statistics
        profile.Statistics = CalculateStatistics(profile);
        
        return profile;
    }
    
    private async Task WarmupAsync(string filePath, ChunkingOptions options)
    {
        await foreach (var _ in _processor.ProcessAsync(filePath, options))
        {
            // Warmup iteration
        }
    }
    
    private async Task ProfileIterationAsync(
        string filePath, 
        ChunkingOptions options,
        PerformanceProfile profile)
    {
        var iteration = new PerformanceIteration();
        
        // Force garbage collection for accurate memory measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var memoryBefore = GC.GetTotalMemory(true);
        var totalStopwatch = Stopwatch.StartNew();
        
        // Stage 1: Reading
        var readStopwatch = Stopwatch.StartNew();
        var rawContent = await _processor.ExtractAsync(filePath);
        readStopwatch.Stop();
        iteration.ReadTime = readStopwatch.Elapsed;
        iteration.MemoryAfterRead = GC.GetTotalMemory(false) - memoryBefore;
        
        // Stage 2: Parsing
        var parseStopwatch = Stopwatch.StartNew();
        var parsedContent = await _processor.ParseAsync(rawContent);
        parseStopwatch.Stop();
        iteration.ParseTime = parseStopwatch.Elapsed;
        iteration.MemoryAfterParse = GC.GetTotalMemory(false) - memoryBefore;
        
        // Stage 3: Chunking
        var chunkStopwatch = Stopwatch.StartNew();
        var chunks = await _processor.ChunkAsync(parsedContent, options);
        chunkStopwatch.Stop();
        iteration.ChunkTime = chunkStopwatch.Elapsed;
        iteration.MemoryAfterChunk = GC.GetTotalMemory(false) - memoryBefore;
        
        totalStopwatch.Stop();
        iteration.TotalTime = totalStopwatch.Elapsed;
        iteration.ChunkCount = chunks.Length;
        iteration.PeakMemory = GC.GetTotalMemory(false) - memoryBefore;
        
        // Calculate throughput
        iteration.ThroughputMBps = profile.FileSize / (1024.0 * 1024.0) / iteration.TotalTime.TotalSeconds;
        iteration.ChunksPerSecond = chunks.Length / iteration.TotalTime.TotalSeconds;
        
        profile.Iterations.Add(iteration);
    }
    
    private PerformanceStatistics CalculateStatistics(PerformanceProfile profile)
    {
        var stats = new PerformanceStatistics();
        
        if (!profile.Iterations.Any()) return stats;
        
        // Time statistics
        stats.AverageReadTime = TimeSpan.FromMilliseconds(
            profile.Iterations.Average(i => i.ReadTime.TotalMilliseconds));
        stats.AverageParseTime = TimeSpan.FromMilliseconds(
            profile.Iterations.Average(i => i.ParseTime.TotalMilliseconds));
        stats.AverageChunkTime = TimeSpan.FromMilliseconds(
            profile.Iterations.Average(i => i.ChunkTime.TotalMilliseconds));
        stats.AverageTotalTime = TimeSpan.FromMilliseconds(
            profile.Iterations.Average(i => i.TotalTime.TotalMilliseconds));
        
        // Memory statistics
        stats.AverageMemoryUsage = (long)profile.Iterations.Average(i => i.PeakMemory);
        stats.MaxMemoryUsage = profile.Iterations.Max(i => i.PeakMemory);
        stats.MinMemoryUsage = profile.Iterations.Min(i => i.PeakMemory);
        
        // Throughput statistics
        stats.AverageThroughput = profile.Iterations.Average(i => i.ThroughputMBps);
        stats.MaxThroughput = profile.Iterations.Max(i => i.ThroughputMBps);
        stats.MinThroughput = profile.Iterations.Min(i => i.ThroughputMBps);
        
        // Chunk statistics
        stats.AverageChunksPerSecond = profile.Iterations.Average(i => i.ChunksPerSecond);
        stats.ConsistentChunkCount = profile.Iterations.All(i => 
            i.ChunkCount == profile.Iterations.First().ChunkCount);
        
        // Calculate standard deviation
        var mean = profile.Iterations.Average(i => i.TotalTime.TotalMilliseconds);
        var variance = profile.Iterations.Average(i => 
            Math.Pow(i.TotalTime.TotalMilliseconds - mean, 2));
        stats.StandardDeviation = Math.Sqrt(variance);
        
        // Calculate percentiles
        var sortedTimes = profile.Iterations
            .Select(i => i.TotalTime.TotalMilliseconds)
            .OrderBy(t => t)
            .ToList();
        
        stats.P50 = TimeSpan.FromMilliseconds(GetPercentile(sortedTimes, 50));
        stats.P95 = TimeSpan.FromMilliseconds(GetPercentile(sortedTimes, 95));
        stats.P99 = TimeSpan.FromMilliseconds(GetPercentile(sortedTimes, 99));
        
        return stats;
    }
    
    private double GetPercentile(List<double> sortedValues, int percentile)
    {
        if (!sortedValues.Any()) return 0;
        
        var index = (percentile / 100.0) * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        
        if (lower == upper) return sortedValues[lower];
        
        var weight = index - lower;
        return sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight;
    }
}

// Performance data models
public class PerformanceProfile
{
    public string FilePath { get; set; }
    public long FileSize { get; set; }
    public string Strategy { get; set; }
    public int ChunkSize { get; set; }
    public int IterationCount { get; set; }
    public List<PerformanceIteration> Iterations { get; set; } = new();
    public PerformanceStatistics Statistics { get; set; }
}

public class PerformanceIteration
{
    public TimeSpan ReadTime { get; set; }
    public TimeSpan ParseTime { get; set; }
    public TimeSpan ChunkTime { get; set; }
    public TimeSpan TotalTime { get; set; }
    
    public long MemoryAfterRead { get; set; }
    public long MemoryAfterParse { get; set; }
    public long MemoryAfterChunk { get; set; }
    public long PeakMemory { get; set; }
    
    public int ChunkCount { get; set; }
    public double ThroughputMBps { get; set; }
    public double ChunksPerSecond { get; set; }
}

public class PerformanceStatistics
{
    // Time statistics
    public TimeSpan AverageReadTime { get; set; }
    public TimeSpan AverageParseTime { get; set; }
    public TimeSpan AverageChunkTime { get; set; }
    public TimeSpan AverageTotalTime { get; set; }
    
    // Memory statistics
    public long AverageMemoryUsage { get; set; }
    public long MaxMemoryUsage { get; set; }
    public long MinMemoryUsage { get; set; }
    
    // Throughput statistics
    public double AverageThroughput { get; set; }
    public double MaxThroughput { get; set; }
    public double MinThroughput { get; set; }
    
    // Chunk statistics
    public double AverageChunksPerSecond { get; set; }
    public bool ConsistentChunkCount { get; set; }
    
    // Statistical measures
    public double StandardDeviation { get; set; }
    public TimeSpan P50 { get; set; }
    public TimeSpan P95 { get; set; }
    public TimeSpan P99 { get; set; }
}