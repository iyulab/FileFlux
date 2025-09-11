using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BenchmarkDotNet.Running;
using FileFlux;
using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FileFlux.Infrastructure.Optimization;
using FileFlux.Infrastructure.Caching;
using FileFlux.Infrastructure.Services;
using FileFlux.Infrastructure.Strategies;
using Microsoft.Extensions.DependencyInjection;
using FileFlux.RealWorldBenchmark.Benchmarks;
using FileFlux.RealWorldBenchmark.Metrics;
using FileFlux.RealWorldBenchmark.Services;
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

        // Parse command line arguments
        var mode = args.Length > 0 ? args[0].ToLower() : "interactive";
        var strategyArg = args.Length > 1 ? args[1] : "";
        
        switch (mode)
        {
            case "dotnet":
            case "benchmark":
                // Run BenchmarkDotNet benchmarks
                RunDotNetBenchmarks();
                break;
            
            case "quick":
                // Quick benchmarks
                await RunQuickBenchmarks();
                break;
            
            case "comprehensive":
                // Comprehensive analysis
                await RunComprehensiveBenchmarks();
                break;
                
            // Phase 10: New test modes
            case "memory-test":
                await RunMemoryOptimizationTest(strategyArg);
                break;
                
            case "auto-strategy-test":
                await RunAutoStrategyTest();
                break;
                
            case "large-file-test":
                await RunLargeFileTest(strategyArg);
                break;
                
            case "boundary-quality-test":
                await RunBoundaryQualityTest();
                break;
                
            case "context-preservation-test":
                await RunContextPreservationTest();
                break;
                
            case "phase-comparison":
                await RunPhaseComparisonTest();
                break;
            
            default:
                // Interactive mode
                await RunInteractiveMode();
                break;
        }
    }
    
    static void RunDotNetBenchmarks()
    {
        AnsiConsole.MarkupLine("[yellow]Running BenchmarkDotNet benchmarks...[/]");
        AnsiConsole.MarkupLine("[dim]This will take several minutes for accurate measurements.[/]");
        
        var summary = BenchmarkRunner.Run<PerformanceBenchmarker>();
        
        AnsiConsole.MarkupLine("[green]Benchmarks complete! Results saved to BenchmarkDotNet.Artifacts[/]");
    }
    
    static async Task RunQuickBenchmarks()
    {
        // Discover test files
        DiscoverTestFiles();
        
        // Show test file summary
        ShowTestFileSummary();
        
        // Run quick benchmarks
        await RunBenchmarks();
    }
    
    static async Task RunComprehensiveBenchmarks()
    {
        // Discover test files
        DiscoverTestFiles();
        
        // Show test file summary
        ShowTestFileSummary();
        
        // Create processor
        var processor = CreateProcessor();
        
        // Run comprehensive analysis
        await RunComprehensiveAnalysis(processor);
    }
    
    static async Task RunInteractiveMode()
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Select benchmark mode:[/]")
                .AddChoices(
                    "Quick Benchmarks",
                    "Comprehensive Analysis", 
                    "RAG Quality Assessment",
                    "RAG Quality Comparison (NEW)",
                    "Performance Profiling",
                    "BenchmarkDotNet Suite",
                    "Export Results",
                    "Exit"
                ));
        
        switch (choice)
        {
            case "Quick Benchmarks":
                await RunQuickBenchmarks();
                break;
            case "Comprehensive Analysis":
                await RunComprehensiveBenchmarks();
                break;
            case "RAG Quality Assessment":
                await RunRAGQualityAssessment();
                break;
            case "RAG Quality Comparison (NEW)":
                await RunRAGQualityComparison();
                break;
            case "Performance Profiling":
                await RunPerformanceProfiling();
                break;
            case "BenchmarkDotNet Suite":
                RunDotNetBenchmarks();
                break;
            case "Export Results":
                await ExportResults();
                break;
            case "Exit":
                return;
        }
        
        // Ask if user wants to continue
        if (AnsiConsole.Confirm("Run another benchmark?"))
        {
            await RunInteractiveMode();
        }
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
        await RunRAGQualityAssessment();
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

    static async Task RunRAGQualityAssessment()
    {
        // Discover test files if not already done
        if (!TestFiles.Any())
        {
            DiscoverTestFiles();
        }
        
        var processor = CreateProcessor();
        var analyzer = new RAGQualityAnalyzer();
        
        AnsiConsole.Write(new Rule("[bold blue]RAG Quality Assessment[/]"));
        
        // In non-interactive mode, analyze all available file types
        var fileTypes = TestFiles.Keys.ToList();
        
        var results = new Dictionary<string, List<(string Strategy, RAGQualityReport Report)>>();
        
        await AnsiConsole.Progress()
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn(),
            })
            .StartAsync(async ctx =>
            {
                var totalFiles = fileTypes.Sum(ft => TestFiles[ft].Count);
                var task = ctx.AddTask("[green]Analyzing RAG quality[/]", maxValue: totalFiles * 4);
                
                foreach (var fileType in fileTypes)
                {
                    results[fileType] = new List<(string, RAGQualityReport)>();
                    
                    foreach (var file in TestFiles[fileType])
                    {
                        task.Description = $"Analyzing {file.Name}";
                        
                        // Read original content
                        var originalContent = await File.ReadAllTextAsync(file.Path);
                        
                        foreach (var strategy in new[] { "Intelligent", "Semantic", "Paragraph", "FixedSize" })
                        {
                            var chunks = new List<DocumentChunk>();
                            
                            await foreach (var chunk in processor.ProcessAsync(
                                file.Path,
                                new ChunkingOptions { Strategy = strategy, MaxChunkSize = 512, OverlapSize = 64 }))
                            {
                                chunks.Add(chunk);
                            }
                            
                            var report = analyzer.AnalyzeChunks(chunks, originalContent);
                            results[fileType].Add((strategy, report));
                            
                            task.Increment(1);
                        }
                    }
                }
            });
        
        // Display comprehensive results
        DisplayRAGQualityResults(results);
    }
    
    static void DisplayRAGQualityResults(Dictionary<string, List<(string Strategy, RAGQualityReport Report)>> results)
    {
        foreach (var fileType in results.Keys)
        {
            AnsiConsole.Write(new Rule($"[bold yellow]{fileType} Files RAG Quality[/]"));
            
            // Group by strategy
            var byStrategy = results[fileType].GroupBy(r => r.Strategy);
            
            var table = new Table();
            table.AddColumn("Strategy");
            table.AddColumn("Composite Score");
            table.AddColumn("Semantic");
            table.AddColumn("Context");
            table.AddColumn("Density");
            table.AddColumn("Structure");
            table.AddColumn("Retrieval");
            table.AddColumn("Boundary");
            
            foreach (var group in byStrategy)
            {
                var avgReport = group.Select(g => g.Report).ToList();
                var avgComposite = avgReport.Average(r => r.CompositeScore);
                var avgSemantic = avgReport.Average(r => r.SemanticCompleteness?.OverallScore ?? 0);
                var avgContext = avgReport.Average(r => r.ContextPreservation?.OverallScore ?? 0);
                var avgDensity = avgReport.Average(r => r.InformationDensity?.OverallScore ?? 0);
                var avgStructure = avgReport.Average(r => r.StructuralIntegrity?.OverallScore ?? 0);
                var avgRetrieval = avgReport.Average(r => r.RetrievalReadiness?.OverallScore ?? 0);
                var avgBoundary = avgReport.Average(r => r.BoundaryQuality?.OverallScore ?? 0);
                
                table.AddRow(
                    group.Key,
                    FormatScore(avgComposite),
                    FormatScore(avgSemantic),
                    FormatScore(avgContext),
                    FormatScore(avgDensity),
                    FormatScore(avgStructure),
                    FormatScore(avgRetrieval),
                    FormatScore(avgBoundary)
                );
            }
            
            AnsiConsole.Write(table);
            
            // Show recommendations
            var bestStrategy = byStrategy.OrderByDescending(g => 
                g.Select(x => x.Report.CompositeScore).Average()).First();
            
            AnsiConsole.MarkupLine($"\n[green]Best strategy for {fileType}: {bestStrategy.Key}[/]");
            
            var recommendations = bestStrategy.First().Report.Recommendations.Take(3);
            if (recommendations.Any())
            {
                AnsiConsole.MarkupLine("\n[yellow]Top Recommendations:[/]");
                foreach (var rec in recommendations)
                {
                    AnsiConsole.MarkupLine($"  • {rec}");
                }
            }
        }
    }
    
    static string FormatScore(double score)
    {
        var percentage = score * 100;
        if (score >= 0.8)
            return $"[green]{percentage:F0}%[/]";
        else if (score >= 0.6)
            return $"[yellow]{percentage:F0}%[/]";
        else
            return $"[red]{percentage:F0}%[/]";
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
    
    static async Task RunRAGQualityComparison()
    {
        AnsiConsole.Write(new Rule("[bold blue]🚀 RAG Quality Comparison - SmartChunking vs Others[/]"));
        
        // Check if OpenAI API is configured
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey) || apiKey.Contains("your-") || apiKey.Contains("here"))
        {
            AnsiConsole.MarkupLine("[red]❌ OpenAI API key not configured in .env.local[/]");
            AnsiConsole.MarkupLine("[yellow]This benchmark requires a real OpenAI API key for embedding generation.[/]");
            return;
        }
        
        // Discover test files if not already done
        if (!TestFiles.Any())
        {
            DiscoverTestFiles();
        }
        
        if (!TestFiles.Any())
        {
            AnsiConsole.MarkupLine("[red]❌ No test files found. Please ensure test files exist in D:\\data\\FileFlux\\test[/]");
            return;
        }
        
        // Select a test document
        var allFiles = TestFiles.Values.SelectMany(f => f).ToList();
        var selectedFile = AnsiConsole.Prompt(
            new SelectionPrompt<TestFile>()
                .Title("Select test document:")
                .AddChoices(allFiles.Take(10)) // Limit to first 10 for performance
                .UseConverter(f => $"{f.Name} ({FormatFileSize(f.Size)})")
        );
        
        // Define test queries for RAG evaluation
        var testQueries = new List<string>
        {
            "What is the main purpose of this document?",
            "What are the key features described?",
            "How does this system work?",
            "What are the benefits mentioned?",
            "What technical details are provided?"
        };
        
        // Allow user to customize queries
        if (AnsiConsole.Confirm("Do you want to add custom test queries?"))
        {
            var customQuery = AnsiConsole.Ask<string>("Enter your test query:");
            testQueries.Add(customQuery);
        }
        
        AnsiConsole.MarkupLine($"[green]✅ Selected: {selectedFile.Name}[/]");
        AnsiConsole.MarkupLine($"[green]✅ Test queries: {testQueries.Count}[/]");
        AnsiConsole.MarkupLine($"[green]✅ Using OpenAI API for embeddings[/]");
        
        // Create services
        var processor = CreateProcessor();
        var embeddingService = new OpenAiEmbeddingService(apiKey);
        var comparison = new RAGQualityComparison(processor, embeddingService);
        
        // Run the comparison
        try
        {
            var report = await comparison.RunComparisonAsync(selectedFile.Path, testQueries);
            
            // Display results
            DisplayRAGComparisonResults(report);
            
            // Save results to file
            var reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            var reportPath = Path.Combine(Directory.GetCurrentDirectory(), $"rag-comparison-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(reportPath, reportJson);
            
            AnsiConsole.MarkupLine($"[green]📊 Report saved to: {reportPath}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Comparison failed: {ex.Message}[/]");
        }
        finally
        {
            embeddingService.Dispose();
        }
    }
    
    static void DisplayRAGComparisonResults(ComparisonReport report)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold green]📊 Comparison Results[/]"));
        
        var table = new Table();
        table.AddColumn("Strategy");
        table.AddColumn("Chunks");
        table.AddColumn("Quality");
        table.AddColumn("Retrieval");
        table.AddColumn("Completeness");
        table.AddColumn("Integrity");
        table.AddColumn("Status");
        
        // Sort by quality score descending
        var sortedResults = report.StrategyResults
            .Where(kvp => string.IsNullOrEmpty(kvp.Value.Error))
            .OrderByDescending(kvp => kvp.Value.AverageQualityScore)
            .ToList();
        
        for (int i = 0; i < sortedResults.Count; i++)
        {
            var result = sortedResults[i];
            var strategy = result.Value;
            
            var rank = i == 0 ? "[gold3]🥇[/]" : 
                      i == 1 ? "[silver]🥈[/]" : 
                      i == 2 ? "[orange3]🥉[/]" : "";
            
            var statusColor = strategy.StrategyName == "Smart" ? "[lime]NEW[/]" : "[dim]---[/]";
            
            table.AddRow(
                $"{rank} {strategy.StrategyName}",
                strategy.TotalChunks.ToString(),
                $"{strategy.AverageQualityScore:F3}",
                $"{strategy.AverageRetrievalScore:F3}",
                $"{strategy.ChunkCompleteness:F3}",
                $"{strategy.SentenceIntegrity:F3}",
                statusColor
            );
        }
        
        // Add failed strategies
        var failedResults = report.StrategyResults
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value.Error))
            .ToList();
            
        foreach (var failed in failedResults)
        {
            table.AddRow(
                failed.Value.StrategyName,
                "0",
                "N/A",
                "N/A", 
                "N/A",
                "N/A",
                $"[red]ERROR[/]"
            );
        }
        
        AnsiConsole.Write(table);
        
        // Highlight improvements
        var smartResult = report.StrategyResults.FirstOrDefault(kvp => kvp.Key == "Smart").Value;
        var intelligentResult = report.StrategyResults.FirstOrDefault(kvp => kvp.Key == "Intelligent").Value;
        
        if (smartResult != null && intelligentResult != null && string.IsNullOrEmpty(smartResult.Error) && string.IsNullOrEmpty(intelligentResult.Error))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[bold yellow]📈 Smart vs Intelligent Comparison[/]"));
            
            var qualityImprovement = ((smartResult.AverageQualityScore - intelligentResult.AverageQualityScore) / intelligentResult.AverageQualityScore) * 100;
            var completenessImprovement = ((smartResult.ChunkCompleteness - intelligentResult.ChunkCompleteness) / intelligentResult.ChunkCompleteness) * 100;
            var integrityImprovement = ((smartResult.SentenceIntegrity - intelligentResult.SentenceIntegrity) / intelligentResult.SentenceIntegrity) * 100;
            
            AnsiConsole.MarkupLine($"[green]✨ Quality Score: {qualityImprovement:+0.1;-0.1;0}%[/]");
            AnsiConsole.MarkupLine($"[green]✨ Completeness: {completenessImprovement:+0.1;-0.1;0}%[/]");
            AnsiConsole.MarkupLine($"[green]✨ Sentence Integrity: {integrityImprovement:+0.1;-0.1;0}%[/]");
            
            if (smartResult.ChunkCompleteness >= 0.7)
            {
                AnsiConsole.MarkupLine($"[lime]🎯 SUCCESS: Smart chunking achieved ≥70% completeness ({smartResult.ChunkCompleteness:P1})[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[orange1]⚠️ NEEDS IMPROVEMENT: Completeness is {smartResult.ChunkCompleteness:P1} (target: ≥70%)[/]");
            }
        }
        
        AnsiConsole.WriteLine();
    }

    static IDocumentProcessor CreateProcessor()
    {
        // Use OpenAI service if API key is configured, otherwise fallback to mock
        ITextCompletionService textService;
        
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        
        if (!string.IsNullOrEmpty(apiKey) && !apiKey.Contains("your-") && !apiKey.Contains("here"))
        {
            textService = new OpenAiTextCompletionService(apiKey, model);
            if (!_processorMessageShown)
            {
                AnsiConsole.MarkupLine($"[green]Using OpenAI API for text completion (model: {model ?? "gpt-5-nano"})[/]");
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
        
        // Phase 10: Create components for Auto strategy support
        var readerFactory = new DocumentReaderFactory();
        var parserFactory = new DocumentParserFactory(textService);
        
        // Create a temporary basic strategy factory
        var basicStrategyFactory = new ChunkingStrategyFactory();
        var strategySelector = new AdaptiveStrategySelector(textService, basicStrategyFactory);
        var serviceProvider = new SimpleServiceProvider(textService, strategySelector);
        
        // Create the main strategy factory with DI support
        var strategyFactory = new ChunkingStrategyFactory(serviceProvider);
        
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

    static async Task RunPerformanceProfiling()
{
    // Discover test files if not already done
    if (!TestFiles.Any())
    {
        DiscoverTestFiles();
    }
    
    var processor = CreateProcessor();
    var profiler = new DetailedPerformanceProfiler(processor);
    
    AnsiConsole.Write(new Rule("[bold blue]Performance Profiling[/]"));
    
    var file = AnsiConsole.Prompt(
        new SelectionPrompt<TestFile>()
            .Title("Select file to profile:")
            .AddChoices(TestFiles.Values.SelectMany(f => f))
            .UseConverter(f => $"{f.Name} ({FormatFileSize(f.Size)})")
    );
    
    var strategy = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("Select chunking strategy:")
            .AddChoices("Intelligent", "Semantic", "Paragraph", "FixedSize")
    );
    
    var chunkSize = AnsiConsole.Prompt(
        new SelectionPrompt<int>()
            .Title("Select chunk size:")
            .AddChoices(256, 512, 1024, 2048)
    );
    
    var iterations = AnsiConsole.Prompt(
        new TextPrompt<int>("Number of iterations for profiling:")
            .DefaultValue(5)
            .Validate(n => n > 0 && n <= 20)
    );
    
    var options = new ChunkingOptions
    {
        Strategy = strategy,
        MaxChunkSize = chunkSize,
        OverlapSize = Math.Min(128, chunkSize / 4)
    };
    
    PerformanceProfile profile = null;
    
    await AnsiConsole.Status()
        .StartAsync("Profiling performance...", async ctx =>
        {
            profile = await profiler.ProfileAsync(file.Path, options, iterations);
        });
    
    // Display profiling results
    DisplayPerformanceProfile(profile);
}

static void DisplayPerformanceProfile(PerformanceProfile profile)
{
    AnsiConsole.Write(new Rule("[bold green]Performance Profile Results[/]"));
    
    // File information
    var infoTable = new Table();
    infoTable.AddColumn("Property");
    infoTable.AddColumn("Value");
    
    infoTable.AddRow("File", Path.GetFileName(profile.FilePath));
    infoTable.AddRow("Size", FormatFileSize(profile.FileSize));
    infoTable.AddRow("Strategy", profile.Strategy);
    infoTable.AddRow("Chunk Size", profile.ChunkSize.ToString());
    infoTable.AddRow("Iterations", profile.Iterations.ToString());
    
    AnsiConsole.Write(infoTable);
    
    // Performance statistics
    AnsiConsole.MarkupLine("\n[yellow]Performance Statistics:[/]");
    
    var stats = profile.Statistics;
    var statsTable = new Table();
    statsTable.AddColumn("Metric");
    statsTable.AddColumn("Average");
    statsTable.AddColumn("Min");
    statsTable.AddColumn("Max");
    statsTable.AddColumn("StdDev");
    
    statsTable.AddRow(
        "Total Time",
        $"{stats.AverageTotalTime.TotalMilliseconds:F1} ms",
        $"{stats.P50.TotalMilliseconds:F1} ms",
        $"{stats.P99.TotalMilliseconds:F1} ms",
        $"{stats.StandardDeviation:F1} ms"
    );
    
    statsTable.AddRow(
        "Read Time",
        $"{stats.AverageReadTime.TotalMilliseconds:F1} ms",
        "-",
        "-",
        "-"
    );
    
    statsTable.AddRow(
        "Parse Time",
        $"{stats.AverageParseTime.TotalMilliseconds:F1} ms",
        "-",
        "-",
        "-"
    );
    
    statsTable.AddRow(
        "Chunk Time",
        $"{stats.AverageChunkTime.TotalMilliseconds:F1} ms",
        "-",
        "-",
        "-"
    );
    
    statsTable.AddRow(
        "Memory Usage",
        FormatFileSize(stats.AverageMemoryUsage),
        FormatFileSize(stats.MinMemoryUsage),
        FormatFileSize(stats.MaxMemoryUsage),
        "-"
    );
    
    statsTable.AddRow(
        "Throughput",
        $"{stats.AverageThroughput:F2} MB/s",
        $"{stats.MinThroughput:F2} MB/s",
        $"{stats.MaxThroughput:F2} MB/s",
        "-"
    );
    
    AnsiConsole.Write(statsTable);
    
    // Percentiles
    AnsiConsole.MarkupLine("\n[yellow]Latency Percentiles:[/]");
    AnsiConsole.MarkupLine($"  P50: {stats.P50.TotalMilliseconds:F1} ms");
    AnsiConsole.MarkupLine($"  P95: {stats.P95.TotalMilliseconds:F1} ms");
    AnsiConsole.MarkupLine($"  P99: {stats.P99.TotalMilliseconds:F1} ms");
    
    // Consistency check
    if (stats.ConsistentChunkCount)
    {
        AnsiConsole.MarkupLine("\n[green]✓ Chunk count consistent across iterations[/]");
    }
    else
    {
        AnsiConsole.MarkupLine("\n[yellow]⚠ Chunk count varies across iterations[/]");
    }
}

static async Task RunComprehensiveAnalysis(IDocumentProcessor processor)
{
    AnsiConsole.Write(new Rule("[bold blue]Comprehensive Analysis[/]"));
    
    var analyzer = new RAGQualityAnalyzer();
    var profiler = new DetailedPerformanceProfiler(processor);
    
    var comprehensiveResults = new List<ComprehensiveResult>();
    
    await AnsiConsole.Progress()
        .Columns(new ProgressColumn[]
        {
            new TaskDescriptionColumn(),
            new ProgressBarColumn(),
            new PercentageColumn(),
            new ElapsedTimeColumn(),
            new SpinnerColumn(),
        })
        .StartAsync(async ctx =>
        {
            var strategies = new[] { "Intelligent", "Semantic", "Paragraph", "FixedSize" };
            var chunkSizes = new[] { 256, 512, 1024 };
            
            var totalTests = TestFiles.Sum(f => f.Value.Count) * strategies.Length * chunkSizes.Length;
            var task = ctx.AddTask("[green]Running comprehensive analysis[/]", maxValue: totalTests);
            
            foreach (var fileType in TestFiles)
            {
                foreach (var file in fileType.Value.Take(2)) // Limit for time
                {
                    var originalContent = await File.ReadAllTextAsync(file.Path);
                    
                    foreach (var strategy in strategies)
                    {
                        foreach (var chunkSize in chunkSizes)
                        {
                            task.Description = $"Analyzing {file.Name} ({strategy} @ {chunkSize})";
                            
                            var options = new ChunkingOptions
                            {
                                Strategy = strategy,
                                MaxChunkSize = chunkSize,
                                OverlapSize = Math.Min(128, chunkSize / 4)
                            };
                            
                            // Collect chunks
                            var chunks = new List<DocumentChunk>();
                            await foreach (var chunk in processor.ProcessAsync(file.Path, options))
                            {
                                chunks.Add(chunk);
                            }
                            
                            // Quality analysis
                            var qualityReport = analyzer.AnalyzeChunks(chunks, originalContent);
                            
                            // Performance profiling
                            var perfProfile = await profiler.ProfileAsync(file.Path, options, 3);
                            
                            comprehensiveResults.Add(new ComprehensiveResult
                            {
                                FileName = file.Name,
                                FileType = fileType.Key,
                                FileSize = file.Size,
                                Strategy = strategy,
                                ChunkSize = chunkSize,
                                QualityReport = qualityReport,
                                PerformanceProfile = perfProfile
                            });
                            
                            task.Increment(1);
                        }
                    }
                }
            }
        });
    
    // Generate comprehensive report
    GenerateComprehensiveReport(comprehensiveResults);
}

static void GenerateComprehensiveReport(List<ComprehensiveResult> results)
{
    AnsiConsole.Write(new Rule("[bold green]Comprehensive Analysis Report[/]"));
    
    // Best configurations by file type
    var byFileType = results.GroupBy(r => r.FileType);
    
    foreach (var fileTypeGroup in byFileType)
    {
        AnsiConsole.MarkupLine($"\n[bold yellow]{fileTypeGroup.Key} Files Analysis:[/]");
        
        // Find best configuration
        var best = fileTypeGroup.OrderByDescending(r => 
            r.QualityReport.CompositeScore * 0.7 + 
            (r.PerformanceProfile.Statistics.AverageThroughput / 10) * 0.3)
            .First();
        
        AnsiConsole.MarkupLine($"[green]Optimal Configuration:[/]");
        AnsiConsole.MarkupLine($"  Strategy: {best.Strategy}");
        AnsiConsole.MarkupLine($"  Chunk Size: {best.ChunkSize}");
        AnsiConsole.MarkupLine($"  Quality Score: {best.QualityReport.CompositeScore:P0}");
        AnsiConsole.MarkupLine($"  Throughput: {best.PerformanceProfile.Statistics.AverageThroughput:F2} MB/s");
        
        // Performance comparison table
        var perfTable = new Table();
        perfTable.AddColumn("Strategy");
        perfTable.AddColumn("Chunk Size");
        perfTable.AddColumn("Quality");
        perfTable.AddColumn("Speed");
        perfTable.AddColumn("Memory");
        perfTable.AddColumn("Overall");
        
        foreach (var config in fileTypeGroup.OrderByDescending(r => 
            r.QualityReport.CompositeScore * 0.7 + 
            (r.PerformanceProfile.Statistics.AverageThroughput / 10) * 0.3).Take(5))
        {
            var overallScore = config.QualityReport.CompositeScore * 0.7 + 
                              (config.PerformanceProfile.Statistics.AverageThroughput / 10) * 0.3;
            
            perfTable.AddRow(
                config.Strategy,
                config.ChunkSize.ToString(),
                FormatScore(config.QualityReport.CompositeScore),
                $"{config.PerformanceProfile.Statistics.AverageThroughput:F1} MB/s",
                FormatFileSize(config.PerformanceProfile.Statistics.AverageMemoryUsage),
                FormatScore(overallScore)
            );
        }
        
        AnsiConsole.Write(perfTable);
    }
    
    // Overall recommendations
    AnsiConsole.Write(new Rule("[bold cyan]Overall Recommendations[/]"));
    
    var topRecommendations = results
        .SelectMany(r => r.QualityReport.Recommendations)
        .GroupBy(r => r)
        .OrderByDescending(g => g.Count())
        .Take(5)
        .Select(g => new { Recommendation = g.Key, Count = g.Count() });
    
    AnsiConsole.MarkupLine("\n[yellow]Most Common Improvement Areas:[/]");
    foreach (var rec in topRecommendations)
    {
        AnsiConsole.MarkupLine($"  • {rec.Recommendation} (found in {rec.Count} configurations)");
    }
}

static async Task ExportResults()
{
    AnsiConsole.MarkupLine("[yellow]Export functionality to be implemented[/]");
    await Task.CompletedTask;
}

// Phase 10: Memory Optimization Test
static async Task RunMemoryOptimizationTest(string strategy)
{
    AnsiConsole.Write(new Rule("[bold magenta]Memory Optimization Test (Phase 10)[/]"));
    
    // Discover test files if needed
    if (!TestFiles.Any())
    {
        DiscoverTestFiles();
    }
    
    var processor = CreateProcessor();
    var testStrategy = string.IsNullOrEmpty(strategy) ? "MemoryOptimizedIntelligent" : strategy;
    
    AnsiConsole.MarkupLine($"[yellow]Testing strategy: {testStrategy}[/]");
    
    // Find largest file for memory stress test
    var largestFile = TestFiles.Values
        .SelectMany(f => f)
        .OrderByDescending(f => f.Size)
        .FirstOrDefault();
    
    if (largestFile == null)
    {
        AnsiConsole.MarkupLine("[red]No test files found![/]");
        return;
    }
    
    var options = new ChunkingOptions
    {
        Strategy = testStrategy,
        MaxChunkSize = 512,
        OverlapSize = 128
    };
    
    // Before processing - collect garbage and measure
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    
    var memoryBefore = GC.GetTotalMemory(true);
    var stopwatch = Stopwatch.StartNew();
    var chunks = new List<DocumentChunk>();
    
    await AnsiConsole.Status()
        .StartAsync("Processing file with memory monitoring...", async ctx =>
        {
            await foreach (var chunk in processor.ProcessAsync(largestFile.Path, options))
            {
                chunks.Add(chunk);
            }
        });
    
    stopwatch.Stop();
    var memoryAfter = GC.GetTotalMemory(false);
    var memoryUsed = memoryAfter - memoryBefore;
    var memoryRatio = (double)memoryUsed / largestFile.Size;
    
    // Force garbage collection and check final memory
    GC.Collect();
    GC.WaitForPendingFinalizers();
    var finalMemory = GC.GetTotalMemory(true);
    
    // Display results
    var table = new Table();
    table.AddColumn("Metric");
    table.AddColumn("Value");
    table.AddColumn("Phase 10 Target");
    
    table.AddRow("Strategy", testStrategy, "MemoryOptimizedIntelligent");
    table.AddRow("File Size", FormatFileSize(largestFile.Size), "-");
    table.AddRow("Peak Memory", FormatFileSize(memoryUsed), "≤50% of Phase 9");
    table.AddRow("Memory Ratio", $"{memoryRatio:P2}", "≤200% of file size");
    table.AddRow("Chunks Generated", chunks.Count.ToString(), "-");
    table.AddRow("Processing Time", $"{stopwatch.ElapsedMilliseconds} ms", "-");
    table.AddRow("Throughput", $"{largestFile.Size / (1024.0 * 1024.0) / stopwatch.Elapsed.TotalSeconds:F2} MB/s", "≥1 MB/s");
    
    AnsiConsole.Write(table);
    
    // Memory efficiency assessment
    if (memoryRatio <= 2.0)
    {
        AnsiConsole.MarkupLine("[green]✅ Memory efficiency: EXCELLENT (≤200% of file size)[/]");
    }
    else if (memoryRatio <= 3.0)
    {
        AnsiConsole.MarkupLine("[yellow]⚠️ Memory efficiency: ACCEPTABLE (≤300% of file size)[/]");
    }
    else
    {
        AnsiConsole.MarkupLine("[red]❌ Memory efficiency: NEEDS IMPROVEMENT (>300% of file size)[/]");
    }
    
    // Check if chunks have memory optimization metadata
    var optimizedChunks = chunks.Count(c => 
        c.Metadata.CustomProperties.ContainsKey("MemoryOptimized") &&
        c.Metadata.CustomProperties["MemoryOptimized"].ToString() == "True");
    
    if (optimizedChunks > 0)
    {
        AnsiConsole.MarkupLine($"[green]✅ Memory optimization active: {optimizedChunks}/{chunks.Count} chunks[/]");
    }
}

// Phase 10: Auto Strategy Test
static async Task RunAutoStrategyTest()
{
    AnsiConsole.Write(new Rule("[bold magenta]Auto Strategy Selection Test (Phase 10)[/]"));
    
    if (!TestFiles.Any())
    {
        DiscoverTestFiles();
    }
    
    var processor = CreateProcessor();
    
    // Test Auto strategy with different document types
    var results = new List<(string FileType, string SelectedStrategy, int ChunkCount, TimeSpan ProcessingTime)>();
    
    await AnsiConsole.Progress()
        .Columns(new ProgressColumn[]
        {
            new TaskDescriptionColumn(),
            new ProgressBarColumn(),
            new PercentageColumn(),
            new SpinnerColumn(),
        })
        .StartAsync(async ctx =>
        {
            var totalFiles = TestFiles.Sum(ft => ft.Value.Count);
            var task = ctx.AddTask("[green]Testing Auto strategy[/]", maxValue: totalFiles);
            
            foreach (var fileType in TestFiles)
            {
                foreach (var file in fileType.Value)
                {
                    task.Description = $"Auto-selecting strategy for {file.Name}";
                    
                    var options = new ChunkingOptions
                    {
                        Strategy = "Auto", // This should trigger automatic selection
                        MaxChunkSize = 512,
                        OverlapSize = 128
                    };
                    
                    var stopwatch = Stopwatch.StartNew();
                    var chunks = new List<DocumentChunk>();
                    
                    await foreach (var chunk in processor.ProcessAsync(file.Path, options))
                    {
                        chunks.Add(chunk);
                    }
                    
                    stopwatch.Stop();
                    
                    // Extract selected strategy from chunk metadata
                    var selectedStrategy = chunks.FirstOrDefault()?.Metadata.CustomProperties
                        .ContainsKey("SelectedStrategy") == true
                        ? chunks.First().Metadata.CustomProperties["SelectedStrategy"].ToString()
                        : "Unknown";
                    
                    results.Add((fileType.Key, selectedStrategy, chunks.Count, stopwatch.Elapsed));
                    task.Increment(1);
                }
            }
        });
    
    // Display results
    var table = new Table();
    table.AddColumn("File Type");
    table.AddColumn("Auto-Selected Strategy");
    table.AddColumn("Chunks");
    table.AddColumn("Time (ms)");
    table.AddColumn("Assessment");
    
    foreach (var group in results.GroupBy(r => r.FileType))
    {
        var strategies = group.Select(g => g.SelectedStrategy).Distinct();
        var avgChunks = group.Average(g => g.ChunkCount);
        var avgTime = group.Average(g => g.ProcessingTime.TotalMilliseconds);
        
        var assessment = strategies.Count() == 1 ? "[green]Consistent[/]" : "[yellow]Varies[/]";
        
        table.AddRow(
            group.Key,
            string.Join(", ", strategies),
            $"{avgChunks:F0}",
            $"{avgTime:F0}",
            assessment
        );
    }
    
    AnsiConsole.Write(table);
    
    // Strategy distribution analysis
    AnsiConsole.MarkupLine("\n[yellow]Strategy Selection Analysis:[/]");
    var strategyGroups = results.GroupBy(r => r.SelectedStrategy);
    
    foreach (var group in strategyGroups)
    {
        var percentage = (double)group.Count() / results.Count * 100;
        AnsiConsole.MarkupLine($"  {group.Key}: {group.Count()} files ({percentage:F1}%)");
    }
}

// Phase 10: Large File Test
static async Task RunLargeFileTest(string strategy)
{
    AnsiConsole.Write(new Rule("[bold magenta]Large File Processing Test (Phase 10)[/]"));
    
    if (!TestFiles.Any())
    {
        DiscoverTestFiles();
    }
    
    var processor = CreateProcessor();
    var testStrategy = string.IsNullOrEmpty(strategy) ? "Auto" : strategy;
    
    // Find files larger than 1MB
    var largeFiles = TestFiles.Values
        .SelectMany(f => f)
        .Where(f => f.Size > 1024 * 1024) // > 1MB
        .OrderByDescending(f => f.Size)
        .Take(3)
        .ToList();
    
    if (!largeFiles.Any())
    {
        AnsiConsole.MarkupLine("[yellow]No files larger than 1MB found. Testing with largest available files.[/]");
        largeFiles = TestFiles.Values
            .SelectMany(f => f)
            .OrderByDescending(f => f.Size)
            .Take(3)
            .ToList();
    }
    
    var results = new List<(string FileName, long FileSize, int ChunkCount, TimeSpan Time, double MemoryRatio)>();
    
    await AnsiConsole.Progress()
        .Columns(new ProgressColumn[]
        {
            new TaskDescriptionColumn(),
            new ProgressBarColumn(),
            new PercentageColumn(),
            new SpinnerColumn(),
        })
        .StartAsync(async ctx =>
        {
            var task = ctx.AddTask($"[green]Testing large files with {testStrategy}[/]", maxValue: largeFiles.Count);
            
            foreach (var file in largeFiles)
            {
                task.Description = $"Processing {file.Name} ({FormatFileSize(file.Size)})";
                
                var options = new ChunkingOptions
                {
                    Strategy = testStrategy,
                    MaxChunkSize = 1024, // Larger chunks for large files
                    OverlapSize = 256
                };
                
                GC.Collect();
                var memoryBefore = GC.GetTotalMemory(true);
                var stopwatch = Stopwatch.StartNew();
                var chunks = new List<DocumentChunk>();
                
                await foreach (var chunk in processor.ProcessAsync(file.Path, options))
                {
                    chunks.Add(chunk);
                }
                
                stopwatch.Stop();
                var memoryAfter = GC.GetTotalMemory(false);
                var memoryUsed = memoryAfter - memoryBefore;
                var memoryRatio = (double)memoryUsed / file.Size;
                
                results.Add((file.Name, file.Size, chunks.Count, stopwatch.Elapsed, memoryRatio));
                task.Increment(1);
            }
        });
    
    // Display results
    var table = new Table();
    table.AddColumn("File Name");
    table.AddColumn("File Size");
    table.AddColumn("Chunks");
    table.AddColumn("Time");
    table.AddColumn("Memory Ratio");
    table.AddColumn("Status");
    
    foreach (var result in results)
    {
        var status = result.MemoryRatio < 3.0 ? "[green]✅[/]" : "[red]❌[/]";
        var throughput = result.FileSize / (1024.0 * 1024.0) / result.Time.TotalSeconds;
        
        table.AddRow(
            result.FileName,
            FormatFileSize(result.FileSize),
            result.ChunkCount.ToString(),
            $"{result.Time.TotalSeconds:F1}s ({throughput:F2} MB/s)",
            $"{result.MemoryRatio:F2}x",
            status
        );
    }
    
    AnsiConsole.Write(table);
    
    // Performance assessment
    var avgThroughput = results.Average(r => r.FileSize / (1024.0 * 1024.0) / r.Time.TotalSeconds);
    var avgMemoryRatio = results.Average(r => r.MemoryRatio);
    
    AnsiConsole.MarkupLine($"\n[yellow]Large File Performance Summary:[/]");
    AnsiConsole.MarkupLine($"  Average Throughput: {avgThroughput:F2} MB/s");
    AnsiConsole.MarkupLine($"  Average Memory Ratio: {avgMemoryRatio:F2}x");
    
    if (avgThroughput >= 1.0 && avgMemoryRatio <= 3.0)
    {
        AnsiConsole.MarkupLine("[green]✅ Large file processing: EXCELLENT[/]");
    }
    else if (avgThroughput >= 0.5 && avgMemoryRatio <= 5.0)
    {
        AnsiConsole.MarkupLine("[yellow]⚠️ Large file processing: ACCEPTABLE[/]");
    }
    else
    {
        AnsiConsole.MarkupLine("[red]❌ Large file processing: NEEDS IMPROVEMENT[/]");
    }
}

// Phase 10: Boundary Quality Test
static async Task RunBoundaryQualityTest()
{
    AnsiConsole.Write(new Rule("[bold magenta]Boundary Quality Test (Phase 10)[/]"));
    
    if (!TestFiles.Any())
    {
        DiscoverTestFiles();
    }
    
    var processor = CreateProcessor();
    var analyzer = new RAGQualityAnalyzer();
    
    // Test boundary quality with different strategies
    var strategies = new[] { "Smart", "MemoryOptimizedIntelligent", "Intelligent", "Semantic" };
    var results = new List<(string Strategy, double BoundaryScore, double ConsistencyScore)>();
    
    // Use a representative test file
    var testFile = TestFiles.Values.SelectMany(f => f).FirstOrDefault();
    if (testFile == null)
    {
        AnsiConsole.MarkupLine("[red]No test files found![/]");
        return;
    }
    
    var originalContent = await File.ReadAllTextAsync(testFile.Path);
    
    await AnsiConsole.Progress()
        .StartAsync(async ctx =>
        {
            var task = ctx.AddTask("[green]Testing boundary quality[/]", maxValue: strategies.Length);
            
            foreach (var strategy in strategies)
            {
                task.Description = $"Testing {strategy} strategy";
                
                var chunks = new List<DocumentChunk>();
                
                await foreach (var chunk in processor.ProcessAsync(
                    testFile.Path,
                    new ChunkingOptions { Strategy = strategy, MaxChunkSize = 512, OverlapSize = 128 }))
                {
                    chunks.Add(chunk);
                }
                
                // Analyze boundary quality
                var qualityReport = analyzer.AnalyzeChunks(chunks, originalContent);
                var boundaryScore = qualityReport.BoundaryQuality?.OverallScore ?? 0;
                
                // Calculate consistency score (how similar are chunk sizes)
                var chunkSizes = chunks.Select(c => c.Content.Length).ToList();
                var avgSize = chunkSizes.Average();
                var variance = chunkSizes.Sum(size => Math.Pow(size - avgSize, 2)) / chunkSizes.Count;
                var consistencyScore = Math.Max(0, 1.0 - (Math.Sqrt(variance) / avgSize));
                
                results.Add((strategy, boundaryScore, consistencyScore));
                task.Increment(1);
            }
        });
    
    // Display results
    var table = new Table();
    table.AddColumn("Strategy");
    table.AddColumn("Boundary Quality");
    table.AddColumn("Size Consistency");
    table.AddColumn("Overall Assessment");
    
    foreach (var result in results.OrderByDescending(r => r.BoundaryScore))
    {
        var overall = (result.BoundaryScore + result.ConsistencyScore) / 2;
        var assessment = overall >= 0.8 ? "[green]Excellent[/]" :
                        overall >= 0.6 ? "[yellow]Good[/]" : "[red]Needs Work[/]";
        
        table.AddRow(
            result.Strategy,
            FormatScore(result.BoundaryScore),
            FormatScore(result.ConsistencyScore),
            assessment
        );
    }
    
    AnsiConsole.Write(table);
    
    // Phase 10 specific assessment
    var smartResult = results.FirstOrDefault(r => r.Strategy == "Smart");
    if (smartResult.Strategy != null)
    {
        AnsiConsole.MarkupLine($"\n[yellow]Phase 10 Smart Strategy Assessment:[/]");
        if (smartResult.BoundaryScore >= 0.8)
        {
            AnsiConsole.MarkupLine("[green]✅ Boundary quality target achieved (≥80%)[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[orange1]⚠️ Boundary quality: {smartResult.BoundaryScore:P1} (target: ≥80%)[/]");
        }
    }
}

// Phase 10: Context Preservation Test
static async Task RunContextPreservationTest()
{
    AnsiConsole.Write(new Rule("[bold magenta]Context Preservation Test (Phase 10)[/]"));
    
    if (!TestFiles.Any())
    {
        DiscoverTestFiles();
    }
    
    var processor = CreateProcessor();
    var analyzer = new RAGQualityAnalyzer();
    
    // Test adaptive overlap with different strategies
    var strategies = new[] { "Smart", "MemoryOptimizedIntelligent", "Intelligent" };
    var testFile = TestFiles.Values.SelectMany(f => f).FirstOrDefault();
    
    if (testFile == null)
    {
        AnsiConsole.MarkupLine("[red]No test files found![/]");
        return;
    }
    
    var originalContent = await File.ReadAllTextAsync(testFile.Path);
    var results = new List<(string Strategy, double ContextScore, double AdaptiveOverlapScore)>();
    
    await AnsiConsole.Progress()
        .StartAsync(async ctx =>
        {
            var task = ctx.AddTask("[green]Testing context preservation[/]", maxValue: strategies.Length);
            
            foreach (var strategy in strategies)
            {
                task.Description = $"Analyzing {strategy} context preservation";
                
                var chunks = new List<DocumentChunk>();
                
                await foreach (var chunk in processor.ProcessAsync(
                    testFile.Path,
                    new ChunkingOptions 
                    { 
                        Strategy = strategy, 
                        MaxChunkSize = 512, 
                        OverlapSize = 128,
                        // Enable Phase 10 features
                        CustomProperties = new Dictionary<string, object>
                        {
                            ["AdaptiveOverlap"] = true,
                            ["ContextWindowSize"] = 3000
                        }
                    }))
                {
                    chunks.Add(chunk);
                }
                
                // Analyze context preservation
                var qualityReport = analyzer.AnalyzeChunks(chunks, originalContent);
                var contextScore = qualityReport.ContextPreservation?.OverallScore ?? 0;
                
                // Calculate adaptive overlap effectiveness
                var overlapLengths = new List<int>();
                for (int i = 1; i < chunks.Count; i++)
                {
                    var prevContent = chunks[i - 1].Content;
                    var currContent = chunks[i].Content;
                    var overlapLength = GetOverlapLength(prevContent, currContent);
                    overlapLengths.Add(overlapLength);
                }
                
                var avgOverlap = overlapLengths.Any() ? overlapLengths.Average() : 0;
                var adaptiveScore = Math.Min(1.0, avgOverlap / 128.0); // Normalize to 128 char target
                
                results.Add((strategy, contextScore, adaptiveScore));
                task.Increment(1);
            }
        });
    
    // Display results
    var table = new Table();
    table.AddColumn("Strategy");
    table.AddColumn("Context Preservation");
    table.AddColumn("Adaptive Overlap");
    table.AddColumn("Phase 10 Status");
    
    foreach (var result in results.OrderByDescending(r => r.ContextScore))
    {
        var phase10Feature = result.Strategy.Contains("Smart") || result.Strategy.Contains("MemoryOptimized");
        var status = phase10Feature ? "[green]Phase 10[/]" : "[dim]Legacy[/]";
        
        table.AddRow(
            result.Strategy,
            FormatScore(result.ContextScore),
            FormatScore(result.AdaptiveOverlapScore),
            status
        );
    }
    
    AnsiConsole.Write(table);
    
    // Context preservation assessment
    var bestContextScore = results.Max(r => r.ContextScore);
    AnsiConsole.MarkupLine($"\n[yellow]Context Preservation Analysis:[/]");
    AnsiConsole.MarkupLine($"  Best Context Score: {bestContextScore:P1}");
    
    if (bestContextScore >= 0.85)
    {
        AnsiConsole.MarkupLine("[green]✅ Context preservation: EXCELLENT (≥85%)[/]");
    }
    else if (bestContextScore >= 0.7)
    {
        AnsiConsole.MarkupLine("[yellow]⚠️ Context preservation: GOOD (≥70%)[/]");
    }
    else
    {
        AnsiConsole.MarkupLine("[red]❌ Context preservation: NEEDS IMPROVEMENT (<70%)[/]");
    }
}

// Phase 10: Comprehensive Phase Comparison
static async Task RunPhaseComparisonTest()
{
    AnsiConsole.Write(new Rule("[bold magenta]Phase 9 vs Phase 10 Comparison[/]"));
    
    if (!TestFiles.Any())
    {
        DiscoverTestFiles();
    }
    
    var processor = CreateProcessor();
    var analyzer = new RAGQualityAnalyzer();
    
    // Phase 9 strategies vs Phase 10 strategies
    var phase9Strategies = new[] { "Intelligent", "Semantic", "Paragraph" };
    var phase10Strategies = new[] { "Auto", "Smart", "MemoryOptimizedIntelligent" };
    
    var testFile = TestFiles.Values.SelectMany(f => f).FirstOrDefault();
    if (testFile == null)
    {
        AnsiConsole.MarkupLine("[red]No test files found![/]");
        return;
    }
    
    var originalContent = await File.ReadAllTextAsync(testFile.Path);
    var allResults = new List<(string Phase, string Strategy, RAGQualityReport Quality, TimeSpan ProcessingTime, long MemoryUsed)>();
    
    await AnsiConsole.Progress()
        .StartAsync(async ctx =>
        {
            var totalTests = phase9Strategies.Length + phase10Strategies.Length;
            var task = ctx.AddTask("[green]Running phase comparison[/]", maxValue: totalTests);
            
            // Test Phase 9 strategies
            foreach (var strategy in phase9Strategies)
            {
                task.Description = $"Testing Phase 9: {strategy}";
                
                GC.Collect();
                var memoryBefore = GC.GetTotalMemory(true);
                var stopwatch = Stopwatch.StartNew();
                var chunks = new List<DocumentChunk>();
                
                await foreach (var chunk in processor.ProcessAsync(
                    testFile.Path,
                    new ChunkingOptions { Strategy = strategy, MaxChunkSize = 512, OverlapSize = 128 }))
                {
                    chunks.Add(chunk);
                }
                
                stopwatch.Stop();
                var memoryAfter = GC.GetTotalMemory(false);
                var qualityReport = analyzer.AnalyzeChunks(chunks, originalContent);
                
                allResults.Add(("Phase 9", strategy, qualityReport, stopwatch.Elapsed, memoryAfter - memoryBefore));
                task.Increment(1);
            }
            
            // Test Phase 10 strategies
            foreach (var strategy in phase10Strategies)
            {
                task.Description = $"Testing Phase 10: {strategy}";
                
                GC.Collect();
                var memoryBefore = GC.GetTotalMemory(true);
                var stopwatch = Stopwatch.StartNew();
                var chunks = new List<DocumentChunk>();
                
                await foreach (var chunk in processor.ProcessAsync(
                    testFile.Path,
                    new ChunkingOptions { Strategy = strategy, MaxChunkSize = 512, OverlapSize = 128 }))
                {
                    chunks.Add(chunk);
                }
                
                stopwatch.Stop();
                var memoryAfter = GC.GetTotalMemory(false);
                var qualityReport = analyzer.AnalyzeChunks(chunks, originalContent);
                
                allResults.Add(("Phase 10", strategy, qualityReport, stopwatch.Elapsed, memoryAfter - memoryBefore));
                task.Increment(1);
            }
        });
    
    // Display comparison results
    var table = new Table();
    table.AddColumn("Phase");
    table.AddColumn("Strategy");
    table.AddColumn("Quality Score");
    table.AddColumn("Context");
    table.AddColumn("Boundary");
    table.AddColumn("Time (ms)");
    table.AddColumn("Memory");
    
    foreach (var result in allResults.OrderBy(r => r.Phase).ThenByDescending(r => r.Quality.CompositeScore))
    {
        var phaseColor = result.Phase == "Phase 10" ? "[green]" : "[dim]";
        var qualityColor = result.Quality.CompositeScore >= 0.8 ? "[green]" : 
                          result.Quality.CompositeScore >= 0.6 ? "[yellow]" : "[red]";
        
        table.AddRow(
            $"{phaseColor}{result.Phase}[/]",
            result.Strategy,
            $"{qualityColor}{result.Quality.CompositeScore:P0}[/]",
            FormatScore(result.Quality.ContextPreservation?.OverallScore ?? 0),
            FormatScore(result.Quality.BoundaryQuality?.OverallScore ?? 0),
            $"{result.ProcessingTime.TotalMilliseconds:F0}",
            FormatFileSize(result.MemoryUsed)
        );
    }
    
    AnsiConsole.Write(table);
    
    // Phase comparison summary
    var phase9Results = allResults.Where(r => r.Phase == "Phase 9").ToList();
    var phase10Results = allResults.Where(r => r.Phase == "Phase 10").ToList();
    
    if (phase9Results.Any() && phase10Results.Any())
    {
        var phase9AvgQuality = phase9Results.Average(r => r.Quality.CompositeScore);
        var phase10AvgQuality = phase10Results.Average(r => r.Quality.CompositeScore);
        var qualityImprovement = ((phase10AvgQuality - phase9AvgQuality) / phase9AvgQuality) * 100;
        
        var phase9AvgMemory = phase9Results.Average(r => r.MemoryUsed);
        var phase10AvgMemory = phase10Results.Average(r => r.MemoryUsed);
        var memoryReduction = ((phase9AvgMemory - phase10AvgMemory) / phase9AvgMemory) * 100;
        
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold yellow]Phase 10 Improvement Summary[/]"));
        
        AnsiConsole.MarkupLine($"[green]📈 Quality Improvement: {qualityImprovement:+0.1;-0.1;0}%[/]");
        AnsiConsole.MarkupLine($"[green]🧠 Memory Optimization: {memoryReduction:+0.1;-0.1;0}%[/]");
        
        // Check Phase 10 goals
        if (qualityImprovement > 0)
        {
            AnsiConsole.MarkupLine("[green]✅ Phase 10 Quality Goal: ACHIEVED[/]");
        }
        
        if (memoryReduction > 0)
        {
            AnsiConsole.MarkupLine("[green]✅ Phase 10 Memory Goal: ACHIEVED[/]");
        }
        
        // Best performing strategy
        var bestStrategy = allResults.OrderByDescending(r => r.Quality.CompositeScore).First();
        AnsiConsole.MarkupLine($"\n[lime]🏆 Best Performing Strategy: {bestStrategy.Strategy} ({bestStrategy.Phase})[/]");
        AnsiConsole.MarkupLine($"   Quality Score: {bestStrategy.Quality.CompositeScore:P1}");
        AnsiConsole.MarkupLine($"   Processing Time: {bestStrategy.ProcessingTime.TotalMilliseconds:F0} ms");
        AnsiConsole.MarkupLine($"   Memory Usage: {FormatFileSize(bestStrategy.MemoryUsed)}");
    }
}
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

// Phase 10: Simple Service Provider for Auto strategy support
public class SimpleServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = new();
    
    public SimpleServiceProvider(ITextCompletionService textService, IAdaptiveStrategySelector selector)
    {
        _services[typeof(ITextCompletionService)] = textService;
        _services[typeof(IAdaptiveStrategySelector)] = selector;
    }
    
    public object? GetService(Type serviceType)
    {
        return _services.TryGetValue(serviceType, out var service) ? service : null;
    }
}

class ComprehensiveResult
{
    public string FileName { get; set; }
    public string FileType { get; set; }
    public long FileSize { get; set; }
    public string Strategy { get; set; }
    public int ChunkSize { get; set; }
    public RAGQualityReport QualityReport { get; set; }
    public PerformanceProfile PerformanceProfile { get; set; }
}