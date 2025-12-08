using FileFlux.Core;
using Xunit;

namespace FileFlux.Tests.Utils;

public class TextSanitizerTests
{
    #region RemoveNullBytes Tests

    [Fact]
    public void RemoveNullBytes_WithNullBytes_RemovesThem()
    {
        // Arrange
        var textWithNulls = "Hello\0World\0Test";

        // Act
        var result = TextSanitizer.RemoveNullBytes(textWithNulls);

        // Assert
        Assert.Equal("HelloWorldTest", result);
        Assert.False(result.Contains('\0'), "Result should not contain null bytes");
    }

    [Fact]
    public void RemoveNullBytes_WithoutNullBytes_ReturnsOriginal()
    {
        // Arrange
        var cleanText = "Hello World Test";

        // Act
        var result = TextSanitizer.RemoveNullBytes(cleanText);

        // Assert
        Assert.Equal(cleanText, result);
    }

    [Fact]
    public void RemoveNullBytes_WithNull_ReturnsEmpty()
    {
        // Arrange
        string? text = null;

        // Act
        var result = TextSanitizer.RemoveNullBytes(text);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void RemoveNullBytes_WithEmptyString_ReturnsEmpty()
    {
        // Arrange
        var text = "";

        // Act
        var result = TextSanitizer.RemoveNullBytes(text);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void RemoveNullBytes_WithOnlyNullBytes_ReturnsEmpty()
    {
        // Arrange
        var text = "\0\0\0";

        // Act
        var result = TextSanitizer.RemoveNullBytes(text);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void RemoveNullBytes_WithUnicodeAndNullBytes_PreservesUnicode()
    {
        // Arrange
        var textWithNulls = "한글\0테스트\0日本語";

        // Act
        var result = TextSanitizer.RemoveNullBytes(textWithNulls);

        // Assert
        Assert.Equal("한글테스트日本語", result);
    }

    #endregion

    #region Sanitize Tests

    [Fact]
    public void Sanitize_WithNullBytes_RemovesThem()
    {
        // Arrange
        var textWithNulls = "Test\0Content";

        // Act
        var result = TextSanitizer.Sanitize(textWithNulls);

        // Assert
        Assert.Equal("TestContent", result);
    }

    [Fact]
    public void Sanitize_WithControlChars_KeepsByDefault()
    {
        // Arrange - use char codes to ensure correct characters
        var textWithControl = "Test" + (char)0x01 + "Content" + (char)0x02;

        // Act
        var result = TextSanitizer.Sanitize(textWithControl, removeControlChars: false);

        // Assert - null bytes removed, but other control chars kept
        Assert.Contains((char)0x01, result);
    }

    [Fact]
    public void Sanitize_WithControlChars_RemovesWhenRequested()
    {
        // Arrange - use char codes to ensure correct characters
        var textWithControl = "Test" + (char)0x01 + "Content" + (char)0x02 + "End";

        // Act
        var result = TextSanitizer.Sanitize(textWithControl, removeControlChars: true);

        // Assert
        Assert.Equal("TestContentEnd", result);
        Assert.DoesNotContain((char)0x01, result);
        Assert.DoesNotContain((char)0x02, result);
    }

    [Fact]
    public void Sanitize_PreservesValidWhitespace()
    {
        // Arrange - tab (0x09), newline (0x0A), carriage return (0x0D)
        var textWithWhitespace = "Line1\tTabbed\nLine2\r\nLine3";

        // Act
        var result = TextSanitizer.Sanitize(textWithWhitespace, removeControlChars: true);

        // Assert - valid whitespace preserved
        Assert.Contains('\t', result);
        Assert.Contains('\n', result);
        Assert.Contains('\r', result);
    }

    #endregion

    #region ContainsNullBytes Tests

    [Fact]
    public void ContainsNullBytes_WithNullBytes_ReturnsTrue()
    {
        // Arrange
        var textWithNulls = "Test\0Content";

        // Act
        var result = TextSanitizer.ContainsNullBytes(textWithNulls);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ContainsNullBytes_WithoutNullBytes_ReturnsFalse()
    {
        // Arrange
        var cleanText = "Test Content";

        // Act
        var result = TextSanitizer.ContainsNullBytes(cleanText);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ContainsNullBytes_WithNull_ReturnsFalse()
    {
        // Act
        var result = TextSanitizer.ContainsNullBytes(null);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region IsValidUtf8 Tests

    [Fact]
    public void IsValidUtf8_WithValidText_ReturnsTrue()
    {
        // Arrange
        var validText = "Hello World 한글 日本語";

        // Act
        var result = TextSanitizer.IsValidUtf8(validText);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidUtf8_WithNullBytes_ReturnsFalse()
    {
        // Arrange
        var textWithNulls = "Test\0Content";

        // Act
        var result = TextSanitizer.IsValidUtf8(textWithNulls);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidUtf8_WithNull_ReturnsTrue()
    {
        // Act
        var result = TextSanitizer.IsValidUtf8(null);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidUtf8_WithEmptyString_ReturnsTrue()
    {
        // Act
        var result = TextSanitizer.IsValidUtf8("");

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Real-World Scenarios

    [Fact]
    public void RemoveNullBytes_SimulatedPdfExtraction_CleansBinaryArtifacts()
    {
        // Simulate text extracted from PDF with embedded binary data
        var pdfText = "Document Title\0\0Abstract: This paper discusses\0 important topics.\0\0\0Conclusion: The results show...";

        // Act
        var result = TextSanitizer.RemoveNullBytes(pdfText);

        // Assert
        Assert.False(result.Contains('\0'), "Result should not contain null bytes");
        Assert.Contains("Document Title", result);
        Assert.Contains("Abstract: This paper discusses", result);
        Assert.Contains("Conclusion: The results show...", result);
    }

    [Fact]
    public void RemoveNullBytes_SimulatedDocxFormField_CleansBinaryArtifacts()
    {
        // Simulate text from DOCX with form fields containing null terminators
        var docxText = "Name: John\0\0Doe\0Age: 30\0\0";

        // Act
        var result = TextSanitizer.RemoveNullBytes(docxText);

        // Assert
        Assert.Equal("Name: JohnDoeAge: 30", result);
    }

    [Theory]
    [InlineData("Normal text", "Normal text")]
    [InlineData("Text\0with\0nulls", "Textwithnulls")]
    [InlineData("\0Start", "Start")]
    [InlineData("End\0", "End")]
    [InlineData("", "")]
    public void RemoveNullBytes_VariousInputs_ReturnsExpected(string input, string expected)
    {
        // Act
        var result = TextSanitizer.RemoveNullBytes(input);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion
}
