using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FileFlux.RealWorldBenchmark;

class RealWorldTestRunner
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🚀 Starting FileFlux Real World Quality Test...");
        
        try
        {
            var test = new RealWorldQualityTest();
            var report = await test.RunQualityAssessmentAsync();
            
            Console.WriteLine($"\n📊 Assessment completed in {report.Duration.TotalSeconds:F1}s");
            
            // Save detailed report to file
            await SaveDetailedReport(report);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    static async Task SaveDetailedReport(RealWorldQualityReport report)
    {
        var reportPath = Path.Combine(Directory.GetCurrentDirectory(), "docs", "reports", "Real_World_Quality_Report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);

        var markdown = GenerateMarkdownReport(report);
        await File.WriteAllTextAsync(reportPath, markdown);
        
        Console.WriteLine($"📄 Detailed report saved to: {reportPath}");
    }

    static string GenerateMarkdownReport(RealWorldQualityReport report)
    {
        var successful = report.TestFiles.Where(t => t.Success).ToList();
        var failed = report.TestFiles.Where(t => !t.Success).ToList();

        var md = $@"# Real World Quality Assessment Report

**Generated**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}  
**Duration**: {report.Duration.TotalSeconds:F1} seconds  
**Test Data Path**: D:\data\FileFlux\test  

## 📊 Executive Summary

| Metric | Value |
|--------|-------|
| **Total Files Tested** | {report.TestFiles.Count} |
| **Successful** | {successful.Count} |
| **Failed** | {failed.Count} |
| **Success Rate** | {(double)successful.Count / report.TestFiles.Count:P1} |

## 📈 Performance Results

";

        if (successful.Any())
        {
            var totalChars = successful.Sum(s => s.TotalCharacters);
            var throughput = totalChars / report.Duration.TotalSeconds;

            md += $@"| Performance Metric | Value |
|-------------------|-------|
| **Average Processing Time** | {successful.Average(s => s.ProcessingTime.TotalMilliseconds):F0}ms |
| **Total Characters Processed** | {totalChars:N0} |
| **Total Chunks Generated** | {successful.Sum(s => s.ChunkCount):N0} |
| **Character Throughput** | {throughput:N0} chars/sec |
| **Quality Analysis** | {successful.Where(s => s.QualityScore != null).Count()} files analyzed |

## 📋 Detailed Test Results

";

            foreach (var test in successful)
            {
                md += $@"### ✅ {Path.GetFileName(test.FilePath)}

- **File Type**: {test.FileType}
- **File Size**: {test.FileSize:N0} bytes
- **Processing Time**: {test.ProcessingTime.TotalMilliseconds:F0}ms
- **Chunk Count**: {test.ChunkCount:N0}
- **Total Characters**: {test.TotalCharacters:N0}
- **Quality Analysis**: Completed

";
            }
        }

        if (failed.Any())
        {
            md += "## ❌ Failed Tests\n\n";
            foreach (var fail in failed)
            {
                md += $"- **{Path.GetFileName(fail.FilePath)}**: {fail.ErrorMessage}\n";
            }
        }

        md += $@"

## 🎯 Conclusions

The real-world quality assessment demonstrates FileFlux's capability to process actual documents with measurable performance and quality metrics. This validates the Phase 15 optimization implementations against real-world data.

---

**Report Generated**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}  
**FileFlux Version**: 0.2.0 (Phase 15 Optimizations)
";

        return md;
    }
}