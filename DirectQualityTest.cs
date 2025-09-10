using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FileFlux;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

class DirectQualityTest
{
    private static readonly string TestDataPath = @"D:\data\FileFlux\test";

    static async Task Main(string[] args)
    {
        Console.WriteLine("🚀 Direct FileFlux Quality Test");
        Console.WriteLine($"📁 Test Data Path: {TestDataPath}");

        try
        {
            var services = new ServiceCollection();
            services.AddFileFlux();
            
            var serviceProvider = services.BuildServiceProvider();
            var processor = serviceProvider.GetRequiredService<IDocumentProcessor>();

            var testFiles = DiscoverTestFiles();
            Console.WriteLine($"📄 Found {testFiles.Count} test files");

            var results = new List<TestResult>();
            var totalTime = Stopwatch.StartNew();

            foreach (var testFile in testFiles)
            {
                Console.WriteLine($"\n🧪 Testing: {Path.GetFileName(testFile.FilePath)}");
                
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    
                    var chunks = new List<DocumentChunk>();
                    await foreach (var chunk in processor.ProcessAsync(testFile.FilePath))
                    {
                        chunks.Add(chunk);
                    }
                    
                    stopwatch.Stop();

                    var result = new TestResult
                    {
                        FilePath = testFile.FilePath,
                        FileSize = testFile.Size,
                        FileType = testFile.Type,
                        ProcessingTime = stopwatch.Elapsed,
                        ChunkCount = chunks.Count,
                        TotalCharacters = chunks.Sum(c => c.Content.Length),
                        Success = true
                    };

                    results.Add(result);

                    Console.WriteLine($"  ✅ Success: {chunks.Count} chunks, {result.TotalCharacters:N0} chars");
                    Console.WriteLine($"  ⏱️  Time: {stopwatch.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Error: {ex.Message}");
                    
                    results.Add(new TestResult
                    {
                        FilePath = testFile.FilePath,
                        FileSize = testFile.Size,
                        FileType = testFile.Type,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }
            }

            totalTime.Stop();

            // Generate summary
            GenerateSummary(results, totalTime.Elapsed);
            
            // Save report
            await SaveReport(results, totalTime.Elapsed);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test failed: {ex.Message}");
        }
    }

    static List<TestFileInfo> DiscoverTestFiles()
    {
        var testFiles = new List<TestFileInfo>();

        if (!Directory.Exists(TestDataPath))
        {
            Console.WriteLine($"⚠️ Test data path not found: {TestDataPath}");
            return testFiles;
        }

        var extensions = new[] { ".pdf", ".docx", ".xlsx", ".pptx", ".md", ".txt" };
        
        foreach (var extension in extensions)
        {
            var files = Directory.GetFiles(TestDataPath, $"*{extension}", SearchOption.AllDirectories);
            
            foreach (var file in files.Take(1)) // One file per type for speed
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

        return testFiles.OrderBy(f => f.Size).ToList();
    }

    static void GenerateSummary(List<TestResult> results, TimeSpan duration)
    {
        var successful = results.Where(r => r.Success).ToList();
        var failed = results.Where(r => !r.Success).ToList();

        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("📋 DIRECT QUALITY TEST SUMMARY");
        Console.WriteLine(new string('=', 60));

        Console.WriteLine($"📊 Overall Results:");
        Console.WriteLine($"  • Total Files Tested: {results.Count}");
        Console.WriteLine($"  • Successful: {successful.Count}");
        Console.WriteLine($"  • Failed: {failed.Count}");
        Console.WriteLine($"  • Success Rate: {(double)successful.Count / results.Count:P1}");
        Console.WriteLine($"  • Total Duration: {duration.TotalSeconds:F1}s");

        if (successful.Any())
        {
            Console.WriteLine($"\n📈 Performance Metrics:");
            Console.WriteLine($"  • Avg Processing Time: {successful.Average(s => s.ProcessingTime.TotalMilliseconds):F0}ms");
            Console.WriteLine($"  • Total Characters: {successful.Sum(s => s.TotalCharacters):N0}");
            Console.WriteLine($"  • Total Chunks: {successful.Sum(s => s.ChunkCount):N0}");

            var throughput = successful.Sum(s => s.TotalCharacters) / duration.TotalSeconds;
            Console.WriteLine($"  • Character Throughput: {throughput:N0} chars/sec");
        }

        Console.WriteLine("\n✅ Direct Quality Test Complete!");
    }

    static async Task SaveReport(List<TestResult> results, TimeSpan duration)
    {
        var reportDir = Path.Combine(Directory.GetCurrentDirectory(), "claudedocs");
        Directory.CreateDirectory(reportDir);
        
        var reportPath = Path.Combine(reportDir, "Direct_Quality_Test_Report.md");

        var successful = results.Where(r => r.Success).ToList();

        var markdown = $@"# Direct Quality Test Report

**Generated**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}  
**Duration**: {duration.TotalSeconds:F1} seconds  

## 📊 Summary

| Metric | Value |
|--------|-------|
| **Files Tested** | {results.Count} |
| **Successful** | {successful.Count} |
| **Success Rate** | {(double)successful.Count / results.Count:P1} |
| **Total Characters** | {successful.Sum(s => s.TotalCharacters):N0} |
| **Total Chunks** | {successful.Sum(s => s.ChunkCount):N0} |
| **Throughput** | {successful.Sum(s => s.TotalCharacters) / duration.TotalSeconds:N0} chars/sec |

## 📋 Test Results

";

        foreach (var test in successful)
        {
            markdown += $@"### ✅ {Path.GetFileName(test.FilePath)}

- **Type**: {test.FileType}
- **Size**: {test.FileSize:N0} bytes  
- **Processing Time**: {test.ProcessingTime.TotalMilliseconds:F0}ms
- **Chunks**: {test.ChunkCount:N0}
- **Characters**: {test.TotalCharacters:N0}

";
        }

        var failed = results.Where(r => !r.Success).ToList();
        if (failed.Any())
        {
            markdown += "## ❌ Failed Tests\n\n";
            foreach (var fail in failed)
            {
                markdown += $"- **{Path.GetFileName(fail.FilePath)}**: {fail.ErrorMessage}\n";
            }
        }

        markdown += $@"

## 🎯 Conclusion

The direct quality test validates FileFlux's real-world processing capabilities with actual document files from the test directory.

---
**FileFlux Version**: 0.2.0 (Phase 15 Optimizations)
";

        await File.WriteAllTextAsync(reportPath, markdown);
        Console.WriteLine($"📄 Report saved: {reportPath}");
    }
}

class TestResult
{
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileType { get; set; } = string.Empty;
    public TimeSpan ProcessingTime { get; set; }
    public int ChunkCount { get; set; }
    public int TotalCharacters { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

class TestFileInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public long Size { get; set; }
}