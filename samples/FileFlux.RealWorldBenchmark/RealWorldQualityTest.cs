using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FileFlux;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using FileFlux.RealWorldBenchmark.Metrics;
using FileFlux.RealWorldBenchmark.Services;
using DotNetEnv;

namespace FileFlux.RealWorldBenchmark;

public class RealWorldQualityTest
{
    private static readonly string TestDataPath = @"D:\data\FileFlux\test";
    private readonly IServiceProvider _serviceProvider;
    private readonly IDocumentProcessor _processor;
    private readonly RAGQualityAnalyzer _qualityAnalyzer;

    public RealWorldQualityTest()
    {
        // Load environment
        var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env.local");
        if (File.Exists(envPath))
        {
            Env.Load(envPath);
        }

        // Setup DI
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
        _processor = _serviceProvider.GetRequiredService<IDocumentProcessor>();
        _qualityAnalyzer = _serviceProvider.GetRequiredService<RAGQualityAnalyzer>();
    }

    public async Task<RealWorldQualityReport> RunQualityAssessmentAsync()
    {
        var report = new RealWorldQualityReport
        {
            StartTime = DateTime.UtcNow,
            TestFiles = new List<QualityTestResult>()
        };

        Console.WriteLine("🔍 Real World Quality Assessment Starting...");
        Console.WriteLine($"📁 Test Data Path: {TestDataPath}");

        var testFiles = DiscoverTestFiles();
        Console.WriteLine($"📄 Found {testFiles.Count} test files");

        foreach (var testFile in testFiles)
        {
            Console.WriteLine($"\n🧪 Testing: {Path.GetFileName(testFile.FilePath)}");
            
            try
            {
                var stopwatch = Stopwatch.StartNew();
                
                // Process file to chunks
                var chunks = new List<DocumentChunk>();
                await foreach (var chunk in _processor.ProcessAsync(testFile.FilePath))
                {
                    chunks.Add(chunk);
                }
                
                // Analyze quality
                var qualityScore = await _qualityAnalyzer.AnalyzeAsync(chunks);
                
                stopwatch.Stop();

                var testResult = new QualityTestResult
                {
                    FilePath = testFile.FilePath,
                    FileSize = testFile.Size,
                    FileType = testFile.Type,
                    ProcessingTime = stopwatch.Elapsed,
                    ChunkCount = chunks.Count,
                    TotalCharacters = chunks.Sum(c => c.Content.Length),
                    QualityScore = qualityScore,
                    Success = true
                };

                report.TestFiles.Add(testResult);

                Console.WriteLine($"  ✅ Success: {chunks.Count} chunks, {testResult.TotalCharacters:N0} chars");
                Console.WriteLine($"  📊 Quality: Analyzed");
                Console.WriteLine($"  ⏱️  Time: {stopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ Error: {ex.Message}");
                
                report.TestFiles.Add(new QualityTestResult
                {
                    FilePath = testFile.FilePath,
                    FileSize = testFile.Size,
                    FileType = testFile.Type,
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        report.EndTime = DateTime.UtcNow;
        report.Duration = report.EndTime - report.StartTime;

        // Generate summary
        GenerateSummary(report);

        return report;
    }

    private List<TestFileInfo> DiscoverTestFiles()
    {
        var testFiles = new List<TestFileInfo>();

        if (!Directory.Exists(TestDataPath))
        {
            Console.WriteLine($"⚠️ Test data path not found: {TestDataPath}");
            return testFiles;
        }

        var extensions = new[] { ".pdf", ".docx", ".xlsx", ".pptx", ".md", ".txt", ".json", ".csv" };
        
        foreach (var extension in extensions)
        {
            var files = Directory.GetFiles(TestDataPath, $"*{extension}", SearchOption.AllDirectories);
            
            foreach (var file in files.Take(2)) // Limit to 2 files per type for speed
            {
                var fileInfo = new FileInfo(file);
                testFiles.Add(new TestFileInfo
                {
                    FilePath = file,
                    Type = extension.TrimStart('.').ToUpper(),
                    Size = fileInfo.Length
                });
            }
        }

        return testFiles.OrderBy(f => f.Size).ToList(); // Start with smaller files
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddFileFlux();

        // Configure AI services if API key is available
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrEmpty(apiKey))
        {
            services.AddSingleton<IDocumentAnalysisService>(provider =>
                new OpenAIDocumentAnalysisService(
                    apiKey,
                    Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-5-nano"));
        }
        else
        {
            services.AddSingleton<IDocumentAnalysisService, MockTextCompletionService>();
        }

        services.AddSingleton<IImageToTextService, MockImageToTextService>();
        services.AddSingleton<RAGQualityAnalyzer>();
    }

    private void GenerateSummary(RealWorldQualityReport report)
    {
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("📋 REAL WORLD QUALITY ASSESSMENT SUMMARY");
        Console.WriteLine(new string('=', 60));

        var successful = report.TestFiles.Where(t => t.Success).ToList();
        var failed = report.TestFiles.Where(t => !t.Success).ToList();

        Console.WriteLine($"📊 Overall Results:");
        Console.WriteLine($"  • Total Files Tested: {report.TestFiles.Count}");
        Console.WriteLine($"  • Successful: {successful.Count}");
        Console.WriteLine($"  • Failed: {failed.Count}");
        Console.WriteLine($"  • Success Rate: {(double)successful.Count / report.TestFiles.Count:P1}");
        Console.WriteLine($"  • Total Duration: {report.Duration.TotalSeconds:F1}s");

        if (successful.Any())
        {
            Console.WriteLine($"\n📈 Performance Metrics:");
            Console.WriteLine($"  • Avg Processing Time: {successful.Average(s => s.ProcessingTime.TotalMilliseconds):F0}ms");
            Console.WriteLine($"  • Total Characters: {successful.Sum(s => s.TotalCharacters):N0}");
            Console.WriteLine($"  • Total Chunks: {successful.Sum(s => s.ChunkCount):N0}");
            Console.WriteLine($"  • Quality Metrics: {successful.Where(s => s.QualityScore != null).Count()} files analyzed");

            var throughput = successful.Sum(s => s.TotalCharacters) / report.Duration.TotalSeconds;
            Console.WriteLine($"  • Character Throughput: {throughput:N0} chars/sec");
        }

        if (failed.Any())
        {
            Console.WriteLine($"\n❌ Failed Files:");
            foreach (var fail in failed)
            {
                Console.WriteLine($"  • {Path.GetFileName(fail.FilePath)}: {fail.ErrorMessage}");
            }
        }

        Console.WriteLine("\n✅ Real World Quality Assessment Complete!");
    }
}

public class RealWorldQualityReport
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public List<QualityTestResult> TestFiles { get; set; } = new();
}

public class QualityTestResult
{
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileType { get; set; } = string.Empty;
    public TimeSpan ProcessingTime { get; set; }
    public int ChunkCount { get; set; }
    public int TotalCharacters { get; set; }
    public object? QualityScore { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public class TestFileInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public long Size { get; set; }
}