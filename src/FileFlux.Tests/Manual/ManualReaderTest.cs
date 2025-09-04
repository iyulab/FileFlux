using FileFlux.Infrastructure.Readers;
using FileFlux.Infrastructure.Factories;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FileFlux.Tests.Manual;

/// <summary>
/// 수동 테스트 - 실제 파일로 리더 동작 확인
/// </summary>
public class ManualReaderTest
{
    private readonly ILogger<ManualReaderTest> _logger;
    private const string TestDataPath = @"D:\data\FileFlux\test";

    public ManualReaderTest()
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<ManualReaderTest>();
    }

    [Fact]
    public async Task TestAllReadersWithRealFiles()
    {
        var factory = new DocumentReaderFactory();
        
        // 테스트할 파일들
        var testFiles = new Dictionary<string, string>
        {
            ["Word"] = Path.Combine(TestDataPath, "test-docx", "demo.docx"),
            ["PowerPoint"] = Path.Combine(TestDataPath, "test-pptx", "samplepptx.pptx"),
            ["PDF"] = Path.Combine(TestDataPath, "test-pdf", "oai_gpt-oss_model_card.pdf"),
            ["Markdown"] = Path.Combine(TestDataPath, "test-markdown", "test.md")
        };

        _logger.LogInformation("🧪 Manual Reader Test Started");
        _logger.LogInformation("==========================================");

        foreach (var (type, filePath) in testFiles)
        {
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("❌ {Type} file not found: {FilePath}", type, filePath);
                continue;
            }

            var fileName = Path.GetFileName(filePath);
            var reader = factory.GetReader(fileName);

            if (reader == null)
            {
                _logger.LogError("❌ No reader found for {FileName}", fileName);
                continue;
            }

            try
            {
                _logger.LogInformation("🔍 Testing {Type} Reader ({ReaderType})", type, reader.ReaderType);
                _logger.LogInformation("   File: {FileName}", fileName);

                var result = await reader.ExtractAsync(filePath, CancellationToken.None);

                _logger.LogInformation("📊 Extraction Results:");
                _logger.LogInformation("   ✅ Text length: {Length:N0} characters", result.Text.Length);
                _logger.LogInformation("   📁 File size: {Size:N0} bytes", result.FileInfo.FileSize);
                _logger.LogInformation("   ⚠️  Warnings: {Count}", result.ExtractionWarnings.Count);
                
                // 경고사항 출력
                foreach (var warning in result.ExtractionWarnings)
                {
                    _logger.LogWarning("      ⚠️ {Warning}", warning);
                }

                // 구조적 힌트 출력
                _logger.LogInformation("   🏗️  Structural Hints:");
                foreach (var hint in result.StructuralHints)
                {
                    _logger.LogInformation("      📋 {Key}: {Value}", hint.Key, hint.Value);
                }

                // 텍스트 미리보기
                if (result.Text.Length > 0)
                {
                    var preview = result.Text.Length > 300 ? result.Text.Substring(0, 300) + "..." : result.Text;
                    _logger.LogInformation("   📝 Content Preview:");
                    _logger.LogInformation("      {Preview}", preview.Replace("\n", "\\n").Replace("\r", ""));
                }

                Assert.NotNull(result);
                Assert.NotNull(result.Text);
                Assert.Equal(fileName, result.FileInfo.FileName);

                _logger.LogInformation("   ✅ {Type} Reader Test PASSED", type);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ {Type} Reader Test FAILED: {Message}", type, ex.Message);
                throw;
            }

            _logger.LogInformation("------------------------------------------");
        }

        _logger.LogInformation("🎉 Manual Reader Test Completed!");
    }
}