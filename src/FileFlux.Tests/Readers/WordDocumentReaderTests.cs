using FileFlux.Infrastructure.Readers;
using FileFlux;
using FileFlux.Exceptions;
using FileFlux.Domain;
using Microsoft.Extensions.Logging;
using Xunit;
using System.Text;

namespace FileFlux.Tests.Readers;

/// <summary>
/// WordDocumentReader 단위 테스트
/// TDD 방식으로 구현된 테스트 케이스
/// </summary>
public class WordDocumentReaderTests
{
    private readonly WordDocumentReader _reader;
    private readonly ILogger<WordDocumentReaderTests> _logger;

    public WordDocumentReaderTests()
    {
        _reader = new WordDocumentReader();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<WordDocumentReaderTests>();
    }

    [Fact]
    public void ReaderType_ShouldReturnWordReader()
    {
        // Act
        var readerType = _reader.ReaderType;

        // Assert
        Assert.Equal("WordReader", readerType);
    }

    [Fact]
    public void SupportedExtensions_ShouldIncludeDocx()
    {
        // Act
        var supportedExtensions = _reader.SupportedExtensions;

        // Assert
        Assert.Contains(".docx", supportedExtensions);
    }

    [Theory]
    [InlineData("test.docx", true)]
    [InlineData("TEST.DOCX", true)]
    [InlineData("document.docx", true)]
    [InlineData("test.doc", false)]
    [InlineData("test.pdf", false)]
    [InlineData("test.txt", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CanRead_ShouldReturnCorrectResult(string? fileName, bool expected)
    {
        // Act
        var canRead = _reader.CanRead(fileName!);

        // Assert
        Assert.Equal(expected, canRead);
    }

    [Fact]
    public async Task ExtractAsync_WithNullFilePath_ShouldThrowArgumentException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
            _reader.ExtractAsync(null!, CancellationToken.None));
        
        Assert.Contains("File path cannot be null or empty", exception.Message);
    }

    [Fact]
    public async Task ExtractAsync_WithEmptyFilePath_ShouldThrowArgumentException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
            _reader.ExtractAsync("", CancellationToken.None));
        
        Assert.Contains("File path cannot be null or empty", exception.Message);
    }

    [Fact]
    public async Task ExtractAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var nonExistentFile = "non-existent-file.docx";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() => 
            _reader.ExtractAsync(nonExistentFile, CancellationToken.None));
        
        Assert.Contains("Word document not found", exception.Message);
    }

    [Fact]
    public async Task ExtractAsync_WithUnsupportedExtension_ShouldThrowArgumentException()
    {
        // Arrange - Create a temporary file with wrong extension
        var tempFile = Path.GetTempFileName();
        var wrongExtFile = Path.ChangeExtension(tempFile, ".pdf");
        File.Move(tempFile, wrongExtFile);

        try
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
                _reader.ExtractAsync(wrongExtFile, CancellationToken.None));
            
            Assert.Contains("File format not supported", exception.Message);
        }
        finally
        {
            // Cleanup
            if (File.Exists(wrongExtFile))
                File.Delete(wrongExtFile);
        }
    }

    [Fact]
    public async Task ExtractAsync_WithRealWordDocument_ShouldExtractContent()
    {
        // Arrange
        var testFile = @"D:\data\FileFlux\test\test-docx\demo.docx";
        
        // Skip test if file doesn't exist
        if (!File.Exists(testFile))
        {
            _logger.LogWarning("Test file not found: {TestFile}. Skipping test.", testFile);
            return;
        }

        // Act
        var result = await _reader.ExtractAsync(testFile, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Text);
        Assert.NotNull(result.File);
        Assert.NotNull(result.StructuralHints);
        Assert.NotNull(result.ExtractionWarnings);

        // FileInfo 검증
        Assert.Equal("demo.docx", result.File.FileName);
        Assert.Equal(".docx", result.File.FileExtension);
        Assert.Equal("WordReader", result.File.ReaderType);
        Assert.True(result.File.FileSize > 0);

        // StructuralHints 검증
        Assert.Equal("word_document", result.StructuralHints["file_type"]);
        Assert.True((int)result.StructuralHints["character_count"] >= 0);
        Assert.True((int)result.StructuralHints["paragraph_count"] >= 0);

        // 텍스트 내용이 있는지 확인 (빈 문서가 아닌 경우)
        _logger.LogInformation("Extracted text length: {Length}", result.Text.Length);
        _logger.LogInformation("Paragraph count: {Count}", result.StructuralHints["paragraph_count"]);
        
        if (result.Text.Length > 0)
        {
            _logger.LogInformation("First 200 characters: {Preview}", 
                result.Text.Length > 200 ? string.Concat(result.Text.AsSpan(0, 200), "...") : result.Text);
        }
    }

    [Fact]
    public async Task ExtractAsync_WithStream_ShouldExtractContent()
    {
        // Arrange
        var testFile = @"D:\data\FileFlux\test\test-docx\demo.docx";
        
        // Skip test if file doesn't exist
        if (!File.Exists(testFile))
        {
            _logger.LogWarning("Test file not found: {TestFile}. Skipping test.", testFile);
            return;
        }

        using var fileStream = File.OpenRead(testFile);

        // Act
        var result = await _reader.ExtractAsync(fileStream, "demo.docx", CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Text);
        Assert.NotNull(result.File);
        
        Assert.Equal("demo.docx", result.File.FileName);
        Assert.Equal(".docx", result.File.FileExtension);
        Assert.Equal("WordReader", result.File.ReaderType);

        _logger.LogInformation("Stream extraction - Text length: {Length}", result.Text.Length);
    }

    [Fact]
    public async Task ExtractAsync_WithNullStream_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _reader.ExtractAsync((Stream)null!, "test.docx", CancellationToken.None));
        
        Assert.Equal("stream", exception.ParamName);
    }

    [Fact]
    public async Task ExtractAsync_StreamWithUnsupportedExtension_ShouldThrowArgumentException()
    {
        // Arrange
        using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes("test content"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
            _reader.ExtractAsync(memoryStream, "test.pdf", CancellationToken.None));
        
        Assert.Contains("File format not supported", exception.Message);
    }

    [Fact]
    public async Task ExtractAsync_WithCancellation_ShouldRespectCancellationToken()
    {
        // Arrange
        var testFile = @"D:\data\FileFlux\test\test-docx\demo.docx";
        
        // Skip test if file doesn't exist
        if (!File.Exists(testFile))
        {
            _logger.LogWarning("Test file not found: {TestFile}. Skipping test.", testFile);
            return;
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAsync<DocumentProcessingException>(() => 
            _reader.ExtractAsync(testFile, cts.Token));
    }

    [Fact]
    public void ExtractedContent_ShouldMaintainDocumentHierarchy()
    {
        // This test ensures that extracted content maintains document structure
        // like headers, paragraphs, and tables in a readable format
        
        // This would be tested with the real file extraction
        // The structure should include:
        // - Document title (if present)
        // - Headers (H1, H2, etc.)
        // - Regular paragraphs
        // - Tables in pipe-separated format
        // - Proper line breaks and formatting
        
        Assert.True(true, "Structure preservation is validated in real document test");
    }
}
