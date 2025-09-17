using FileFlux.Infrastructure.Utils;
using Xunit;

namespace FileFlux.Tests.Utils;

public class FileNameHelperTests
{
    [Fact]
    public void NormalizeFileName_WithValidUtf8_ReturnsOriginal()
    {
        // Arrange
        var fileName = "테스트_파일명_UTF8.txt";

        // Act
        var result = FileNameHelper.NormalizeFileName(fileName);

        // Assert
        Assert.Equal(fileName, result);
    }

    [Fact]
    public void NormalizeFileName_WithEmptyString_ReturnsEmpty()
    {
        // Arrange
        var fileName = "";

        // Act
        var result = FileNameHelper.NormalizeFileName(fileName);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void NormalizeFileName_WithNull_ReturnsEmpty()
    {
        // Arrange
        string? fileName = null;

        // Act
        var result = FileNameHelper.NormalizeFileName(fileName!);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetSafeFileName_WithPath_ExtractsFileName()
    {
        // Arrange
        var filePath = @"C:\test\한글파일명.txt";

        // Act
        var result = FileNameHelper.GetSafeFileName(filePath);

        // Assert
        Assert.Equal("한글파일명.txt", result);
    }

    [Fact]
    public void IsValidFileName_WithValidName_ReturnsTrue()
    {
        // Arrange
        var fileName = "valid_file_name.txt";

        // Act
        var result = FileNameHelper.IsValidFileName(fileName);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidFileName_WithInvalidChars_ReturnsFalse()
    {
        // Arrange
        var fileName = "invalid<>file|name.txt";

        // Act
        var result = FileNameHelper.IsValidFileName(fileName);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ExtractSafeFileName_WithValidFileInfo_ReturnsNormalizedName()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var koreanFileName = Path.ChangeExtension(tempFile, "한글확장자.txt");

        try
        {
            File.Move(tempFile, koreanFileName);
            var fileInfo = new FileInfo(koreanFileName);

            // Act
            var result = FileNameHelper.ExtractSafeFileName(fileInfo);

            // Assert
            Assert.Contains("한글확장자", result);
            Assert.EndsWith(".txt", result);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (File.Exists(koreanFileName)) File.Delete(koreanFileName);
        }
    }

    [Fact]
    public void ExtractSafeFileName_WithNull_ReturnsEmpty()
    {
        // Arrange
        FileInfo? fileInfo = null;

        // Act
        var result = FileNameHelper.ExtractSafeFileName(fileInfo!);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData("한글파일명.txt", true)]
    [InlineData("English_File.docx", true)]
    [InlineData("日本語ファイル.pdf", true)]
    [InlineData("chinese中文.xlsx", true)]
    [InlineData("mixed_한글_English_123.md", true)]
    public void NormalizeFileName_WithMultiLanguageNames_HandlesCorrectly(string fileName, bool shouldBeValid)
    {
        // Act
        var result = FileNameHelper.NormalizeFileName(fileName);

        // Assert
        Assert.NotNull(result);
        if (shouldBeValid)
        {
            Assert.Equal(fileName, result);
        }
    }
}