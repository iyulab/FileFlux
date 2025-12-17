using FileFlux.Core.Infrastructure.Readers;
using FileFlux.Infrastructure.Factories;
using FileFlux;
using FileFlux.Domain;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FileFlux.Tests.Integration;

/// <summary>
/// 모든 문서 리더들의 통합 테스트
/// /test 폴더의 실제 파일들을 사용한 E2E 테스트
/// </summary>
public class DocumentReadersIntegrationTests
{
    private readonly DocumentReaderFactory _factory;
    private readonly ILogger<DocumentReadersIntegrationTests> _logger;
    private const string TestDataPath = @"D:\data\FileFlux\tests";

    public DocumentReadersIntegrationTests()
    {
        _factory = new DocumentReaderFactory();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<DocumentReadersIntegrationTests>();
    }

    [Fact]
    public void DocumentReaderFactory_ShouldRegisterAllReaders()
    {
        // Act
        var readers = _factory.GetAvailableReaders().ToList();

        // Assert
        Assert.NotEmpty(readers);
        
        var readerTypes = readers.Select(r => r.ReaderType).ToList();
        _logger.LogInformation("Registered readers: {ReaderTypes}", string.Join(", ", readerTypes));
        
        // 기본적으로 등록되어야 하는 리더들 확인
        Assert.Contains("TextReader", readerTypes);
        Assert.Contains("WordReader", readerTypes);
        Assert.Contains("ExcelReader", readerTypes);
        Assert.Contains("PowerPointReader", readerTypes);
        Assert.Contains("PdfReader", readerTypes);

        _logger.LogInformation("✅ All expected readers are registered");
    }

    [Theory]
    [InlineData("demo.docx", "WordReader")]
    [InlineData("file_example_XLS_100.xls", null)] // XLS not supported, only XLSX
    [InlineData("samplepptx.pptx", "PowerPointReader")]
    [InlineData("oai_gpt-oss_model_card.pdf", "PdfReader")]
    [InlineData("test.md", "MarkdownReader")]
    public void DocumentReaderFactory_ShouldSelectCorrectReader(string fileName, string? expectedReaderType)
    {
        // Act
        var reader = _factory.GetReader(fileName);

        // Assert
        if (expectedReaderType == null)
        {
            Assert.Null(reader);
            _logger.LogInformation("✅ Correctly rejected unsupported file: {FileName}", fileName);
        }
        else
        {
            Assert.NotNull(reader);
            Assert.Equal(expectedReaderType, reader.ReaderType);
            _logger.LogInformation("✅ Correctly selected {ReaderType} for {FileName}", expectedReaderType, fileName);
        }
    }

    [Fact]
    public async Task WordDocumentReader_WithRealFile_ShouldExtractContent()
    {
        // Arrange
        var testFile = Path.Combine(TestDataPath, "test-docx", "demo.docx");
        
        if (!File.Exists(testFile))
        {
            _logger.LogWarning("Test file not found: {TestFile}. Skipping test.", testFile);
            return;
        }

        var reader = _factory.GetReader("demo.docx");
        
        // Act
        var result = await reader!.ExtractAsync(testFile, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Text);
        
        _logger.LogInformation("📄 Word Document Test Results:");
        _logger.LogInformation("  File: {FileName} ({FileSize:N0} bytes)", result.File.Name, result.File.Size);
        _logger.LogInformation("  Extracted text length: {Length:N0} characters", result.Text.Length);
        _logger.LogInformation("  Warnings: {Count}", result.Warnings.Count);
        
        if (result.Warnings.Count != 0)
        {
            foreach (var warning in result.Warnings)
            {
                _logger.LogWarning("  ⚠️ {Warning}", warning);
            }
        }
        
        // 구조적 힌트 출력
        foreach (var hint in result.Hints)
        {
            _logger.LogInformation("  📊 {Key}: {Value}", hint.Key, hint.Value);
        }
        
        // 텍스트 미리보기
        if (result.Text.Length > 0)
        {
            var preview = result.Text.Length > 300 ? string.Concat(result.Text.AsSpan(0, 300), "...") : result.Text;
            _logger.LogInformation("  📝 Preview: {Preview}", preview);
        }

        Assert.Equal("WordReader", result.ReaderType);
        Assert.Equal(".docx", result.File.Extension);
    }

    [Fact]
    public async Task PowerPointDocumentReader_WithRealFile_ShouldExtractContent()
    {
        // Arrange
        var testFile = Path.Combine(TestDataPath, "test-pptx", "samplepptx.pptx");
        
        if (!File.Exists(testFile))
        {
            _logger.LogWarning("Test file not found: {TestFile}. Skipping test.", testFile);
            return;
        }

        var reader = _factory.GetReader("samplepptx.pptx");
        
        // Act
        var result = await reader!.ExtractAsync(testFile, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Text);
        
        _logger.LogInformation("🎞️ PowerPoint Document Test Results:");
        _logger.LogInformation("  File: {FileName} ({FileSize:N0} bytes)", result.File.Name, result.File.Size);
        _logger.LogInformation("  Extracted text length: {Length:N0} characters", result.Text.Length);
        _logger.LogInformation("  Warnings: {Count}", result.Warnings.Count);
        
        if (result.Warnings.Count != 0)
        {
            foreach (var warning in result.Warnings)
            {
                _logger.LogWarning("  ⚠️ {Warning}", warning);
            }
        }
        
        // 구조적 힌트 출력
        foreach (var hint in result.Hints)
        {
            _logger.LogInformation("  📊 {Key}: {Value}", hint.Key, hint.Value);
        }
        
        // 슬라이드 구조 확인
        if (result.Text.Contains("## Slide"))
        {
            _logger.LogInformation("  ✅ Slide structure preserved");
        }
        
        Assert.Equal("PowerPointReader", result.ReaderType);
        Assert.Equal(".pptx", result.File.Extension);
    }

    [Fact]
    public async Task PdfDocumentReader_WithRealFile_ShouldExtractContent()
    {
        // Arrange
        var testFile = Path.Combine(TestDataPath, "test-pdf", "oai_gpt-oss_model_card.pdf");
        
        if (!File.Exists(testFile))
        {
            _logger.LogWarning("Test file not found: {TestFile}. Skipping test.", testFile);
            return;
        }

        var reader = _factory.GetReader("oai_gpt-oss_model_card.pdf");
        
        // Act
        var result = await reader!.ExtractAsync(testFile, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Text);
        
        _logger.LogInformation("📑 PDF Document Test Results:");
        _logger.LogInformation("  File: {FileName} ({FileSize:N0} bytes)", result.File.Name, result.File.Size);
        _logger.LogInformation("  Extracted text length: {Length:N0} characters", result.Text.Length);
        _logger.LogInformation("  Warnings: {Count}", result.Warnings.Count);
        
        if (result.Warnings.Count != 0)
        {
            foreach (var warning in result.Warnings)
            {
                _logger.LogWarning("  ⚠️ {Warning}", warning);
            }
        }
        
        // 구조적 힌트 출력
        foreach (var hint in result.Hints)
        {
            _logger.LogInformation("  📊 {Key}: {Value}", hint.Key, hint.Value);
        }
        
        Assert.Equal("PdfReader", result.ReaderType);
        Assert.Equal(".pdf", result.File.Extension);
    }

    [Fact]
    public async Task MarkdownDocumentReader_WithRealFile_ShouldExtractContent()
    {
        // Arrange
        var testFile = Path.Combine(TestDataPath, "test-md", "next-js-installation.md");
        
        if (!File.Exists(testFile))
        {
            _logger.LogWarning("Test file not found: {TestFile}. Skipping test.", testFile);
            return;
        }

        var reader = _factory.GetReader("test.md");
        
        // Act
        var result = await reader!.ExtractAsync(testFile, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Text);
        
        _logger.LogInformation("📝 Markdown Document Test Results:");
        _logger.LogInformation("  File: {FileName} ({FileSize:N0} bytes)", result.File.Name, result.File.Size);
        _logger.LogInformation("  Extracted text length: {Length:N0} characters", result.Text.Length);
        _logger.LogInformation("  Warnings: {Count}", result.Warnings.Count);
        
        // 마크다운 특화 구조 확인
        if (result.Hints.TryGetValue("has_headers", out object? value) && (bool)value)
        {
            _logger.LogInformation("  ✅ Markdown headers detected: {Count}", result.Hints["header_count"]);
        }
        
        if (result.Hints.TryGetValue("has_code_blocks", out object? codeValue) && (bool)codeValue)
        {
            _logger.LogInformation("  ✅ Code blocks detected: {Count}", result.Hints["code_block_count"]);
        }
        
        Assert.Equal("MarkdownReader", result.ReaderType);
        Assert.Equal(".md", result.File.Extension);
        Assert.True(result.Text.Length > 0, "Markdown file should have extractable content");
    }


    [Fact]
    public async Task AllReaders_ShouldProduceValidOutput()
    {
        // Arrange
        var testFiles = new Dictionary<string, string>
        {
            ["Word"] = Path.Combine(TestDataPath, "test-docx", "demo.docx"),
            ["PowerPoint"] = Path.Combine(TestDataPath, "test-pptx", "samplepptx.pptx"),
            ["PDF"] = Path.Combine(TestDataPath, "test-pdf", "oai_gpt-oss_model_card.pdf"),
            ["Markdown"] = Path.Combine(TestDataPath, "test-md", "next-js-installation.md")
        };

        var results = new Dictionary<string, RawContent>();

        // Act & Assert
        foreach (var (type, filePath) in testFiles)
        {
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Test file not found: {FilePath}. Skipping {Type}.", filePath, type);
                continue;
            }

            var fileName = Path.GetFileName(filePath);
            var reader = _factory.GetReader(fileName);
            
            Assert.NotNull(reader);
            
            var result = await reader.ExtractAsync(filePath, null, CancellationToken.None);
            results[type] = result;
            
            // 모든 결과가 유효한지 검증
            Assert.NotNull(result);
            Assert.NotNull(result.Text);
            Assert.NotNull(result.File);
            Assert.NotNull(result.Hints);
            Assert.NotNull(result.Warnings);
            
            Assert.Equal(fileName, result.File.Name);
            Assert.True(result.File.Size > 0);
            Assert.True(result.ExtractedAt > DateTime.UtcNow.AddMinutes(-1));
            
            _logger.LogInformation("✅ {Type}: {Length:N0} chars extracted", type, result.Text.Length);
        }

        // 요약 로그
        _logger.LogInformation("📊 Integration Test Summary:");
        foreach (var (type, result) in results)
        {
            _logger.LogInformation("  {Type}: {Length:N0} characters, {Warnings} warnings", 
                type, result.Text.Length, result.Warnings.Count);
        }
    }

    [Fact]
    public void AllTestFiles_ShouldExistOrLogWarning()
    {
        // Arrange
        var expectedFiles = new[]
        {
            Path.Combine(TestDataPath, "test-docx", "demo.docx"),
            Path.Combine(TestDataPath, "test-pptx", "samplepptx.pptx"),
            Path.Combine(TestDataPath, "test-pdf", "oai_gpt-oss_model_card.pdf"),
            Path.Combine(TestDataPath, "test-md", "next-js-installation.md"),
            Path.Combine(TestDataPath, "test-xlsx", "file_example_XLS_100.xls")
        };

        // Act & Assert
        _logger.LogInformation("📂 Test Files Availability:");
        
        var existingFiles = 0;
        var missingFiles = 0;
        
        foreach (var filePath in expectedFiles)
        {
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                _logger.LogInformation("  ✅ {FileName}: {Size:N0} bytes", 
                    Path.GetFileName(filePath), fileInfo.Length);
                existingFiles++;
            }
            else
            {
                _logger.LogWarning("  ❌ Missing: {FilePath}", filePath);
                missingFiles++;
            }
        }
        
        _logger.LogInformation("📊 Files Status: {Existing} existing, {Missing} missing", 
            existingFiles, missingFiles);
            
        // 최소 한 개 이상의 테스트 파일은 있어야 함
        Assert.True(existingFiles > 0, "At least one test file should be available for testing");
    }
}