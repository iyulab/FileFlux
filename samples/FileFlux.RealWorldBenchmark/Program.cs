using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FileFlux;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FileFlux.Infrastructure.Optimization;
using FileFlux.Infrastructure.Caching;
using FileFlux.Infrastructure.Services;
using Spectre.Console;
using ConsoleTables;
using DotNetEnv;

namespace FileFlux.RealWorldBenchmark;

class Program
{
    private static readonly string TestDataPath = @"D:\data\FileFlux\test";
    private static readonly Dictionary<string, List<TestFile>> TestFiles = new();

    static async Task Main(string[] args)
    {
        // Load environment variables from .env.local
        var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env.local");
        if (File.Exists(envPath))
        {
            Env.Load(envPath);
            AnsiConsole.MarkupLine("[dim]Loaded environment variables from .env.local[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Warning: .env.local not found. Using mock services.[/]");
        }

        AnsiConsole.Write(
            new FigletText("FileFlux Benchmark")
                .LeftJustified()
                .Color(Color.Blue));

        // Discover test files
        DiscoverTestFiles();
        
        // Show test file summary
        ShowTestFileSummary();

        // Run benchmarks
        await RunBenchmarks();
    }

    static void DiscoverTestFiles()
    {
        AnsiConsole.Status()
            .Start("Discovering test files...", ctx =>
            {
                // PDF files
                var pdfPath = Path.Combine(TestDataPath, "test-pdf");
                if (Directory.Exists(pdfPath))
                {
                    TestFiles["PDF"] = Directory.GetFiles(pdfPath, "*.pdf")
                        .Select(f => new TestFile(f))
                        .ToList();
                }

                // DOCX files
                var docxPath = Path.Combine(TestDataPath, "test-docx");
                if (Directory.Exists(docxPath))
                {
                    TestFiles["DOCX"] = Directory.GetFiles(docxPath, "*.docx")
                        .Select(f => new TestFile(f))
                        .ToList();
                }

                // Markdown files
                var mdPath = Path.Combine(TestDataPath, "test-md");
                if (Directory.Exists(mdPath))
                {
                    TestFiles["MD"] = Directory.GetFiles(mdPath, "*.md")
                        .Select(f => new TestFile(f))
                        .ToList();
                }

                // Excel files
                var xlsxPath = Path.Combine(TestDataPath, "test-xlsx");
                if (Directory.Exists(xlsxPath))
                {
                    TestFiles["XLSX"] = Directory.GetFiles(xlsxPath, "*.xlsx")
                        .Select(f => new TestFile(f))
                        .ToList();
                }

                // PowerPoint files
                var pptxPath = Path.Combine(TestDataPath, "test-pptx");
                if (Directory.Exists(pptxPath))
                {
                    TestFiles["PPTX"] = Directory.GetFiles(pptxPath, "*.pptx")
                        .Select(f => new TestFile(f))
                        .ToList();
                }
            });
    }

    static void ShowTestFileSummary()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold yellow]Test Files Summary[/]");
        
        var table = new Table();
        table.AddColumn("File Type");
        table.AddColumn("Count");
        table.AddColumn("Total Size");
        table.AddColumn("Files");

        foreach (var kvp in TestFiles)
        {
            var totalSize = kvp.Value.Sum(f => f.Size);
            var fileNames = string.Join(", ", kvp.Value.Select(f => f.Name));
            
            table.AddRow(
                kvp.Key,
                kvp.Value.Count.ToString(),
                FormatFileSize(totalSize),
                fileNames.Length > 50 ? fileNames.Substring(0, 47) + "..." : fileNames
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    static async Task RunBenchmarks()
    {
        // Create processor
        var processor = CreateProcessor();

        // Benchmark configurations
        var strategies = new[] { "Intelligent", "Semantic", "Paragraph", "FixedSize" };
        var chunkSizes = new[] { 256, 512, 1024 };

        var results = new List<BenchmarkResult>();

        await AnsiConsole.Progress()
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            })
            .StartAsync(async ctx =>
            {
                var totalTests = TestFiles.Sum(f => f.Value.Count) * strategies.Length * chunkSizes.Length;
                var task = ctx.AddTask("[green]Running benchmarks[/]", maxValue: totalTests);

                foreach (var fileType in TestFiles)
                {
                    foreach (var file in fileType.Value)
                    {
                        foreach (var strategy in strategies)
                        {
                            foreach (var chunkSize in chunkSizes)
                            {
                                task.Description = $"Processing {file.Name} with {strategy} @ {chunkSize} tokens";
                                
                                var result = await BenchmarkFile(
                                    processor, 
                                    file, 
                                    strategy, 
                                    chunkSize);
                                
                                results.Add(result);
                                task.Increment(1);
                            }
                        }
                    }
                }
            });

        // Display results
        DisplayResults(results);

        // Run special benchmarks
        await RunSpecialBenchmarks(processor);
    }

    static async Task<BenchmarkResult> BenchmarkFile(
        IDocumentProcessor processor,
        TestFile file,
        string strategy,
        int chunkSize)
    {
        var options = new ChunkingOptions
        {
            Strategy = strategy,
            MaxChunkSize = chunkSize,
            OverlapSize = Math.Min(128, chunkSize / 4)
        };

        var stopwatch = Stopwatch.StartNew();
        var chunks = new List<DocumentChunk>();
        var memoryBefore = GC.GetTotalMemory(true);

        try
        {
            await foreach (var chunk in processor.ProcessAsync(file.Path, options))
            {
                chunks.Add(chunk);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error processing {file.Name}: {ex.Message}[/]");
        }

        stopwatch.Stop();
        var memoryAfter = GC.GetTotalMemory(false);

        return new BenchmarkResult
        {
            FileName = file.Name,
            FileType = file.Extension,
            FileSize = file.Size,
            Strategy = strategy,
            ChunkSize = chunkSize,
            ChunkCount = chunks.Count,
            ProcessingTime = stopwatch.Elapsed,
            MemoryUsed = memoryAfter - memoryBefore,
            ThroughputMBps = file.Size / (1024.0 * 1024.0) / stopwatch.Elapsed.TotalSeconds,
            AvgChunkSize = chunks.Count > 0 ? chunks.Average(c => c.Content.Length) : 0
        };
    }

    static async Task RunSpecialBenchmarks(IDocumentProcessor processor)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold blue]Special Benchmarks[/]"));

        // 1. Memory Efficiency Test
        await TestMemoryEfficiency(processor);

        // 2. Parallel Processing Test
        await TestParallelProcessing();

        // 3. Cache Effectiveness Test
        await TestCacheEffectiveness(processor);

        // 4. RAG Quality Test
        await TestRAGQuality(processor);
    }

    static async Task TestMemoryEfficiency(IDocumentProcessor processor)
    {
        AnsiConsole.MarkupLine("\n[yellow]Memory Efficiency Test[/]");
        
        var largestFile = TestFiles.Values
            .SelectMany(f => f)
            .OrderByDescending(f => f.Size)
            .FirstOrDefault();

        if (largestFile != null)
        {
            var memoryProcessor = new MemoryEfficientProcessor(processor, new LruMemoryCache(100));
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var memoryBefore = GC.GetTotalMemory(true);
            var stopwatch = Stopwatch.StartNew();
            var chunkCount = 0;

            await foreach (var chunk in memoryProcessor.ProcessStreamAsync(
                largestFile.Path, 
                new ChunkingOptions { Strategy = "Intelligent", MaxChunkSize = 512 }))
            {
                chunkCount++;
            }

            stopwatch.Stop();
            var memoryPeak = GC.GetTotalMemory(false);
            var memoryRatio = (double)(memoryPeak - memoryBefore) / largestFile.Size;

            var table = new Table();
            table.AddColumn("Metric");
            table.AddColumn("Value");
            
            table.AddRow("File", largestFile.Name);
            table.AddRow("File Size", FormatFileSize(largestFile.Size));
            table.AddRow("Memory Used", FormatFileSize(memoryPeak - memoryBefore));
            table.AddRow("Memory Ratio", $"{memoryRatio:P1}");
            table.AddRow("Processing Time", $"{stopwatch.ElapsedMilliseconds} ms");
            table.AddRow("Chunks Generated", chunkCount.ToString());
            table.AddRow("Throughput", $"{largestFile.Size / (1024.0 * 1024.0) / stopwatch.Elapsed.TotalSeconds:F2} MB/s");

            AnsiConsole.Write(table);
        }
    }

    static async Task TestParallelProcessing()
    {
        AnsiConsole.MarkupLine("\n[yellow]Parallel Processing Test[/]");
        
        var allFiles = TestFiles.Values.SelectMany(f => f).ToList();
        if (allFiles.Count < 2) return;

        // Create processors
        var baseProcessor = CreateProcessor();
        var parallelProcessor = new ParallelBatchProcessor(baseProcessor);

        // Sequential processing
        var seqStopwatch = Stopwatch.StartNew();
        var seqChunks = 0;
        
        foreach (var file in allFiles.Take(5))
        {
            await foreach (var chunk in baseProcessor.ProcessAsync(
                file.Path, 
                new ChunkingOptions { Strategy = "Semantic", MaxChunkSize = 512 }))
            {
                seqChunks++;
            }
        }
        seqStopwatch.Stop();

        // Parallel processing
        var parStopwatch = Stopwatch.StartNew();
        var parResult = await parallelProcessor.ProcessBatchAsync(
            allFiles.Take(5).Select(f => f.Path),
            new ChunkingOptions { Strategy = "Semantic", MaxChunkSize = 512 });
        parStopwatch.Stop();

        var speedup = (double)seqStopwatch.ElapsedMilliseconds / parStopwatch.ElapsedMilliseconds;

        var table = new Table();
        table.AddColumn("Processing Type");
        table.AddColumn("Time (ms)");
        table.AddColumn("Chunks");
        table.AddColumn("Throughput");
        
        table.AddRow(
            "Sequential",
            seqStopwatch.ElapsedMilliseconds.ToString(),
            seqChunks.ToString(),
            $"{allFiles.Take(5).Count() / seqStopwatch.Elapsed.TotalSeconds:F2} files/s"
        );
        
        table.AddRow(
            "Parallel",
            parStopwatch.ElapsedMilliseconds.ToString(),
            parResult.TotalChunks.ToString(),
            $"{allFiles.Take(5).Count() / parStopwatch.Elapsed.TotalSeconds:F2} files/s"
        );
        
        table.AddRow(
            "[green]Speedup[/]",
            "",
            "",
            $"[green]{speedup:F2}x[/]"
        );

        AnsiConsole.Write(table);
    }

    static async Task TestCacheEffectiveness(IDocumentProcessor processor)
    {
        AnsiConsole.MarkupLine("\n[yellow]Cache Effectiveness Test[/]");
        
        var testFile = TestFiles.Values.SelectMany(f => f).FirstOrDefault();
        if (testFile == null) return;

        var cache = new LruMemoryCache(100);
        var cachedProcessor = new MemoryEfficientProcessor(processor, cache);
        var options = new ChunkingOptions { Strategy = "Intelligent", MaxChunkSize = 512 };

        // First run (cache miss)
        var firstStopwatch = Stopwatch.StartNew();
        var firstChunks = 0;
        
        await foreach (var chunk in cachedProcessor.ProcessStreamAsync(testFile.Path, options))
        {
            firstChunks++;
        }
        firstStopwatch.Stop();

        // Second run (cache hit)
        var secondStopwatch = Stopwatch.StartNew();
        var secondChunks = 0;
        
        await foreach (var chunk in cachedProcessor.ProcessStreamAsync(testFile.Path, options))
        {
            secondChunks++;
        }
        secondStopwatch.Stop();

        var cacheSpeedup = (double)firstStopwatch.ElapsedMilliseconds / secondStopwatch.ElapsedMilliseconds;
        var stats = cache.GetStatistics();

        var table = new Table();
        table.AddColumn("Metric");
        table.AddColumn("First Run");
        table.AddColumn("Second Run");
        
        table.AddRow("Time (ms)", 
            firstStopwatch.ElapsedMilliseconds.ToString(),
            secondStopwatch.ElapsedMilliseconds.ToString());
        
        table.AddRow("Chunks", 
            firstChunks.ToString(),
            secondChunks.ToString());
        
        table.AddRow("Cache Hit Rate", 
            "0%",
            $"{stats.HitRatePercentage:F1}%");
        
        table.AddRow("[green]Speedup[/]", 
            "-",
            $"[green]{cacheSpeedup:F2}x[/]");

        AnsiConsole.Write(table);
    }

    static async Task TestRAGQuality(IDocumentProcessor processor)
    {
        AnsiConsole.MarkupLine("\n[yellow]RAG Quality Assessment[/]");
        
        var mdFile = TestFiles.GetValueOrDefault("MD")?.FirstOrDefault();
        if (mdFile == null) return;

        var strategies = new[] { "Intelligent", "Semantic", "Paragraph", "FixedSize" };
        var qualityResults = new List<RAGQualityResult>();

        foreach (var strategy in strategies)
        {
            var chunks = new List<DocumentChunk>();
            await foreach (var chunk in processor.ProcessAsync(
                mdFile.Path,
                new ChunkingOptions { Strategy = strategy, MaxChunkSize = 512, OverlapSize = 64 }))
            {
                chunks.Add(chunk);
            }

            var quality = AssessRAGQuality(chunks);
            quality.Strategy = strategy;
            qualityResults.Add(quality);
        }

        var table = new Table();
        table.AddColumn("Strategy");
        table.AddColumn("Chunks");
        table.AddColumn("Avg Size");
        table.AddColumn("Completeness");
        table.AddColumn("Overlap Quality");
        table.AddColumn("Score");

        foreach (var result in qualityResults.OrderByDescending(r => r.OverallScore))
        {
            table.AddRow(
                result.Strategy,
                result.ChunkCount.ToString(),
                result.AvgChunkSize.ToString("F0"),
                $"{result.CompletenessScore:P0}",
                $"{result.OverlapQuality:P0}",
                result.OverallScore >= 0.8 ? $"[green]{result.OverallScore:P0}[/]" : 
                result.OverallScore >= 0.6 ? $"[yellow]{result.OverallScore:P0}[/]" :
                $"[red]{result.OverallScore:P0}[/]"
            );
        }

        AnsiConsole.Write(table);
    }

    static RAGQualityResult AssessRAGQuality(List<DocumentChunk> chunks)
    {
        var result = new RAGQualityResult
        {
            ChunkCount = chunks.Count,
            AvgChunkSize = chunks.Average(c => c.Content.Length)
        };

        // 1. Semantic Completeness: Check for complete thoughts/sentences
        var semanticCompleteCount = 0;
        foreach (var chunk in chunks)
        {
            var content = chunk.Content.Trim();
            
            // Check multiple indicators of semantic completeness
            var hasCompleteSentence = content.EndsWith('.') || content.EndsWith('!') || 
                                     content.EndsWith('?') || content.EndsWith(':');
            var hasMinimumLength = content.Length > 50; // At least 50 chars for meaningful content
            var hasMultipleSentences = CountSentences(content) > 1;
            var startsWithCapital = content.Length > 0 && char.IsUpper(content[0]);
            
            // Score based on multiple factors
            var completenessFactors = 0;
            if (hasCompleteSentence) completenessFactors++;
            if (hasMinimumLength) completenessFactors++;
            if (hasMultipleSentences) completenessFactors++;
            if (startsWithCapital) completenessFactors++;
            
            if (completenessFactors >= 3)
                semanticCompleteCount++;
        }
        result.CompletenessScore = chunks.Count > 0 ? (double)semanticCompleteCount / chunks.Count : 0;

        // 2. Context Preservation: Measure overlap quality and continuity
        var contextScores = new List<double>();
        for (int i = 1; i < chunks.Count; i++)
        {
            var prevChunk = chunks[i - 1].Content;
            var currChunk = chunks[i].Content;
            
            // Check for actual content overlap (not just word matching)
            var overlapLength = GetOverlapLength(prevChunk, currChunk);
            var expectedOverlap = Math.Min(128, Math.Min(prevChunk.Length, currChunk.Length) / 4); // 25% or 128 chars
            
            // Score based on overlap presence and quality
            var overlapScore = 0.0;
            if (overlapLength > 0)
            {
                overlapScore = Math.Min(1.0, (double)overlapLength / expectedOverlap);
                
                // Bonus for maintaining sentence boundaries
                if (HasSentenceBoundaryOverlap(prevChunk, currChunk))
                    overlapScore = Math.Min(1.0, overlapScore * 1.2);
            }
            
            contextScores.Add(overlapScore);
        }
        result.OverlapQuality = contextScores.Any() ? contextScores.Average() : 0;

        // 3. Information Density: Measure meaningful content vs filler
        var densityScore = CalculateInformationDensity(chunks);

        // Overall score weighted by importance
        result.OverallScore = (result.CompletenessScore * 0.4) + 
                            (result.OverlapQuality * 0.3) + 
                            (densityScore * 0.3);

        return result;
    }
    
    static int CountSentences(string text)
    {
        // Simple sentence counting
        var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        return sentences.Count(s => s.Trim().Length > 10); // Filter out very short fragments
    }
    
    static int GetOverlapLength(string prev, string curr)
    {
        // Find the longest common substring at the end of prev and beginning of curr
        var maxOverlap = Math.Min(prev.Length, Math.Min(curr.Length, 256));
        
        for (int overlapSize = maxOverlap; overlapSize > 0; overlapSize--)
        {
            var prevTail = prev.Substring(Math.Max(0, prev.Length - overlapSize));
            if (curr.StartsWith(prevTail))
                return overlapSize;
        }
        
        return 0;
    }
    
    static bool HasSentenceBoundaryOverlap(string prev, string curr)
    {
        // Check if overlap maintains sentence boundaries
        var overlapLength = GetOverlapLength(prev, curr);
        if (overlapLength == 0) return false;
        
        var overlapText = curr.Substring(0, overlapLength);
        return overlapText.Contains('.') || overlapText.Contains('!') || overlapText.Contains('?');
    }
    
    static double CalculateInformationDensity(List<DocumentChunk> chunks)
    {
        var densityScores = new List<double>();
        
        foreach (var chunk in chunks)
        {
            var content = chunk.Content;
            var words = content.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            // Count meaningful words (not just whitespace or punctuation)
            var meaningfulWords = words.Count(w => w.Length > 2 && !string.IsNullOrWhiteSpace(w));
            var totalWords = words.Length;
            
            if (totalWords > 0)
            {
                var density = (double)meaningfulWords / totalWords;
                densityScores.Add(density);
            }
        }
        
        return densityScores.Any() ? densityScores.Average() : 0;
    }

    static void DisplayResults(List<BenchmarkResult> results)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold blue]Benchmark Results Summary[/]"));

        // Group by file type
        var byFileType = results.GroupBy(r => r.FileType);

        foreach (var group in byFileType)
        {
            AnsiConsole.MarkupLine($"\n[yellow]{group.Key} Files:[/]");
            
            var table = new ConsoleTable("File", "Strategy", "Chunk Size", "Chunks", "Time (ms)", "Memory", "Throughput");
            
            foreach (var result in group.OrderBy(r => r.FileName).ThenBy(r => r.Strategy))
            {
                table.AddRow(
                    result.FileName.Length > 20 ? result.FileName.Substring(0, 17) + "..." : result.FileName,
                    result.Strategy,
                    result.ChunkSize.ToString(),
                    result.ChunkCount.ToString(),
                    result.ProcessingTime.TotalMilliseconds.ToString("F0"),
                    FormatFileSize(result.MemoryUsed),
                    $"{result.ThroughputMBps:F2} MB/s"
                );
            }
            
            Console.WriteLine(table.ToString());
        }

        // Overall statistics
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold green]Overall Statistics[/]"));
        
        var overallTable = new Table();
        overallTable.AddColumn("Metric");
        overallTable.AddColumn("Value");
        
        overallTable.AddRow("Total Files Processed", 
            results.Select(r => r.FileName).Distinct().Count().ToString());
        overallTable.AddRow("Total Chunks Generated", 
            results.Sum(r => r.ChunkCount).ToString());
        overallTable.AddRow("Average Processing Time", 
            $"{results.Average(r => r.ProcessingTime.TotalMilliseconds):F0} ms");
        overallTable.AddRow("Average Throughput", 
            $"{results.Average(r => r.ThroughputMBps):F2} MB/s");
        overallTable.AddRow("Best Strategy (by speed)", 
            results.GroupBy(r => r.Strategy)
                .OrderByDescending(g => g.Average(r => r.ThroughputMBps))
                .First().Key);

        AnsiConsole.Write(overallTable);
    }

    private static bool _processorMessageShown = false;
    
    static IDocumentProcessor CreateProcessor()
    {
        // Use OpenAI service if API key is configured, otherwise fallback to mock
        ITextCompletionService textService;
        
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrEmpty(apiKey) && !apiKey.Contains("your-") && !apiKey.Contains("here"))
        {
            textService = new OpenAiTextCompletionService(apiKey);
            if (!_processorMessageShown)
            {
                AnsiConsole.MarkupLine("[green]Using OpenAI API for text completion[/]");
                _processorMessageShown = true;
            }
        }
        else
        {
            textService = new MockTextCompletionService();
            if (!_processorMessageShown)
            {
                AnsiConsole.MarkupLine("[yellow]Using mock text completion service (no API key found)[/]");
                _processorMessageShown = true;
            }
        }
        
        var readerFactory = new DocumentReaderFactory();
        var parserFactory = new DocumentParserFactory(textService);
        var strategyFactory = new ChunkingStrategyFactory();
        
        return new DocumentProcessor(readerFactory, parserFactory, strategyFactory);
    }

    static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size = size / 1024;
        }
        
        return $"{size:F2} {sizes[order]}";
    }
}

class TestFile
{
    public string Path { get; }
    public string Name { get; }
    public string Extension { get; }
    public long Size { get; }

    public TestFile(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        Extension = System.IO.Path.GetExtension(path).TrimStart('.').ToUpper();
        var info = new FileInfo(path);
        Size = info.Exists ? info.Length : 0;
    }
}

class BenchmarkResult
{
    public string FileName { get; set; } = "";
    public string FileType { get; set; } = "";
    public long FileSize { get; set; }
    public string Strategy { get; set; } = "";
    public int ChunkSize { get; set; }
    public int ChunkCount { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public long MemoryUsed { get; set; }
    public double ThroughputMBps { get; set; }
    public double AvgChunkSize { get; set; }
}

class RAGQualityResult
{
    public string Strategy { get; set; } = "";
    public int ChunkCount { get; set; }
    public double AvgChunkSize { get; set; }
    public double CompletenessScore { get; set; }
    public double OverlapQuality { get; set; }
    public double OverallScore { get; set; }
}