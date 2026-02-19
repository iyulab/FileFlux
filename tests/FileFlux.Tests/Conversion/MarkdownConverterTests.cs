using FileFlux.Core;
using FileFlux.Infrastructure.Conversion;
using Xunit;

namespace FileFlux.Tests.Conversion;

public class MarkdownConverterTests
{
    private static readonly char[] s_newlineSeparators = ['\r', '\n'];
    private static readonly string[] s_lineSeparators = ["\r\n", "\n"];
    private readonly MarkdownConverter _converter;

    public MarkdownConverterTests()
    {
        _converter = new MarkdownConverter();
    }

    #region Basic Conversion Tests

    [Fact]
    public async Task ConvertAsync_EmptyContent_ReturnsSuccess()
    {
        // Arrange
        var rawContent = new RawContent { Text = "" };

        // Act
        var result = await _converter.ConvertAsync(rawContent);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(ConversionMethod.Heuristic, result.Method);
        Assert.Contains("Empty content", result.Warnings[0]);
    }

    [Fact]
    public async Task ConvertAsync_NullText_ReturnsSuccess()
    {
        // Arrange
        var rawContent = new RawContent { Text = null! };

        // Act
        var result = await _converter.ConvertAsync(rawContent);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.OriginalLength);
    }

    [Fact]
    public async Task ConvertAsync_PlainText_PreservesContent()
    {
        // Arrange
        var text = "This is a simple paragraph of text.\nWith multiple lines.";
        var rawContent = new RawContent { Text = text };

        // Act
        var result = await _converter.ConvertAsync(rawContent);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("This is a simple paragraph", result.Markdown);
        Assert.Contains("multiple lines", result.Markdown);
    }

    #endregion

    #region Heading Detection Tests

    [Theory]
    [InlineData("# Heading 1", "# Heading 1")]
    [InlineData("## Heading 2", "## Heading 2")]
    [InlineData("### Heading 3", "### Heading 3")]
    public async Task ConvertAsync_MarkdownHeadings_PreservesFormat(string input, string expected)
    {
        // Arrange
        var rawContent = new RawContent { Text = input };

        // Act
        var result = await _converter.ConvertAsync(rawContent);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains(expected, result.Markdown);
        Assert.True(result.Statistics.HeadingCount > 0);
    }

    [Fact]
    public async Task ConvertAsync_AllCapsHeading_ConvertsToHeading()
    {
        // Arrange
        var rawContent = new RawContent { Text = "INTRODUCTION\nSome content here" };

        // Act
        var result = await _converter.ConvertAsync(rawContent);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("##", result.Markdown);
        Assert.True(result.Statistics.HeadingCount > 0);
    }

    [Fact]
    public async Task ConvertAsync_NumberedSection_ConvertsToHeading()
    {
        // Arrange
        var rawContent = new RawContent { Text = "1. Introduction\nContent\n2.1 Background\nMore content" };

        // Act
        var result = await _converter.ConvertAsync(rawContent);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Statistics.HeadingCount > 0);
    }

    [Fact]
    public async Task ConvertAsync_HeadingLevelConstraints_Applied()
    {
        // Arrange
        var rawContent = new RawContent { Text = "# Level 1\n## Level 2" };
        var options = new MarkdownConversionOptions
        {
            MinHeadingLevel = 2,
            MaxHeadingLevel = 4
        };

        // Act
        var result = await _converter.ConvertAsync(rawContent, options);

        // Assert
        Assert.True(result.IsSuccess);
        // MinHeadingLevel=2 should convert # to ##
        var firstLine = result.Markdown.Split(s_newlineSeparators, StringSplitOptions.RemoveEmptyEntries)[0];
        Assert.StartsWith("## ", firstLine); // Single # becomes ##
        Assert.Contains("## ", result.Markdown);
    }

    #endregion

    #region List Detection Tests

    [Theory]
    [InlineData("- Item 1", "- Item 1")]
    [InlineData("* Item 2", "* Item 2")]
    [InlineData("+ Item 3", "+ Item 3")]
    [InlineData("1. First item", "1. First item")]
    public async Task ConvertAsync_MarkdownLists_PreservesFormat(string input, string expected)
    {
        // Arrange
        var rawContent = new RawContent { Text = input };

        // Act
        var result = await _converter.ConvertAsync(rawContent);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains(expected, result.Markdown);
    }

    [Theory]
    [InlineData("• Bullet item", "- Bullet item")]
    [InlineData("● Round bullet", "- Round bullet")]
    [InlineData("○ Empty bullet", "- Empty bullet")]
    [InlineData("■ Square bullet", "- Square bullet")]
    public async Task ConvertAsync_UnicodeBullets_ConvertsToMarkdown(string input, string expected)
    {
        // Arrange
        var rawContent = new RawContent { Text = input };

        // Act
        var result = await _converter.ConvertAsync(rawContent);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains(expected, result.Markdown);
    }

    [Theory]
    [InlineData("1) First item", "1. First item")]
    [InlineData("a) Alpha item", "- Alpha item")]
    [InlineData("(1) Parenthesized", "1. Parenthesized")]
    public async Task ConvertAsync_AlternativeListFormats_Converts(string input, string expected)
    {
        // Arrange
        var rawContent = new RawContent { Text = input };

        // Act
        var result = await _converter.ConvertAsync(rawContent);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains(expected, result.Markdown);
    }

    #endregion

    #region Table Detection Tests

    [Fact]
    public async Task ConvertAsync_MarkdownTable_PreservesFormat()
    {
        // Arrange
        var tableText = "| Name | Age |\n| --- | --- |\n| Alice | 30 |";
        var rawContent = new RawContent { Text = tableText };

        // Act
        var result = await _converter.ConvertAsync(rawContent);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("| Name | Age |", result.Markdown);
        Assert.Contains("| --- |", result.Markdown);
        Assert.True(result.Statistics.TableCount > 0);
    }

    [Fact]
    public async Task ConvertAsync_SimplePipeTable_AddsHeaderSeparator()
    {
        // Arrange
        var tableText = "| Name | Age |\n| Alice | 30 |\n| Bob | 25 |";
        var rawContent = new RawContent { Text = tableText };

        // Act
        var result = await _converter.ConvertAsync(rawContent);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("---", result.Markdown);
        Assert.True(result.Statistics.TableCount > 0);
    }

    [Fact]
    public async Task ConvertAsync_TableDisabled_SkipsTableProcessing()
    {
        // Arrange
        var tableText = "| Name | Age |\n| Alice | 30 |";
        var rawContent = new RawContent { Text = tableText };
        var options = new MarkdownConversionOptions { ConvertTables = false };

        // Act
        var result = await _converter.ConvertAsync(rawContent, options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Statistics.TableCount);
    }

    #endregion

    #region Code Block Tests

    [Fact]
    public async Task ConvertAsync_CodeBlock_PreservesContent()
    {
        // Arrange
        var codeText = "```python\nprint('hello')\n```";
        var rawContent = new RawContent { Text = codeText };

        // Act
        var result = await _converter.ConvertAsync(rawContent);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("```python", result.Markdown);
        Assert.Contains("print('hello')", result.Markdown);
        Assert.True(result.Statistics.CodeBlockCount > 0);
    }

    [Fact]
    public async Task ConvertAsync_TildeCodeBlock_PreservesContent()
    {
        // Arrange
        var codeText = "~~~javascript\nconst x = 1;\n~~~";
        var rawContent = new RawContent { Text = codeText };

        // Act
        var result = await _converter.ConvertAsync(rawContent);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("~~~javascript", result.Markdown);
        Assert.Contains("const x = 1;", result.Markdown);
    }

    #endregion

    #region Image Placeholder Tests

    [Theory]
    [InlineData("<!-- IMAGE_START:IMG_1 -->", "![image](embedded:img_1)")]
    [InlineData("[image:diagram]", "![diagram](embedded:img_000)")]
    [InlineData("[img_5]", "![image](embedded:img_5)")]
    public async Task ConvertAsync_ImagePlaceholder_Converts(string input, string expected)
    {
        // Arrange
        var rawContent = new RawContent { Text = input };

        // Act
        var result = await _converter.ConvertAsync(rawContent);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains(expected, result.Markdown);
        Assert.True(result.Statistics.ImagePlaceholderCount > 0);
    }

    [Fact]
    public async Task ConvertAsync_ImagePlaceholderDisabled_SkipsConversion()
    {
        // Arrange
        var rawContent = new RawContent { Text = "<!-- IMAGE_START:IMG_1 -->" };
        var options = new MarkdownConversionOptions { IncludeImagePlaceholders = false };

        // Act
        var result = await _converter.ConvertAsync(rawContent, options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Statistics.ImagePlaceholderCount);
    }

    #endregion

    #region Whitespace Normalization Tests

    [Fact]
    public async Task ConvertAsync_MultipleBlankLines_Normalized()
    {
        // Arrange
        var text = "Line 1\n\n\n\n\nLine 2";
        var rawContent = new RawContent { Text = text };

        // Act
        var result = await _converter.ConvertAsync(rawContent);

        // Assert
        Assert.True(result.IsSuccess);
        // Normalized should have at most 2 consecutive empty lines
        var normalizedLines = result.Markdown.Split(s_lineSeparators, StringSplitOptions.None);
        int consecutiveBlank = 0;
        int maxConsecutiveBlank = 0;
        foreach (var line in normalizedLines)
        {
            if (string.IsNullOrEmpty(line))
                consecutiveBlank++;
            else
            {
                maxConsecutiveBlank = Math.Max(maxConsecutiveBlank, consecutiveBlank);
                consecutiveBlank = 0;
            }
        }
        maxConsecutiveBlank = Math.Max(maxConsecutiveBlank, consecutiveBlank);
        Assert.True(maxConsecutiveBlank <= 2, $"Expected max 2 consecutive blank lines, got {maxConsecutiveBlank}");
    }

    [Fact]
    public async Task ConvertAsync_NormalizationDisabled_PreservesWhitespace()
    {
        // Arrange
        var text = "Line 1\n\n\n\n\nLine 2";
        var rawContent = new RawContent { Text = text };
        var options = new MarkdownConversionOptions { NormalizeWhitespace = false };

        // Act
        var result = await _converter.ConvertAsync(rawContent, options);

        // Assert
        Assert.True(result.IsSuccess);
        // Count blank lines - should have 4 consecutive empty lines preserved
        var blankLineCount = result.Markdown.Split(s_lineSeparators, StringSplitOptions.None)
            .Select((line, index) => new { line, index })
            .Where(x => string.IsNullOrEmpty(x.line))
            .Count();
        Assert.True(blankLineCount >= 4, $"Expected at least 4 blank lines, got {blankLineCount}");
    }

    #endregion

    #region LLM Inference Tests

    [Fact]
    public async Task ConvertAsync_LLMRequestedNoService_AddsWarning()
    {
        // Arrange
        var rawContent = new RawContent { Text = "Some text" };
        var options = new MarkdownConversionOptions { UseLLMInference = true };

        // Act
        var result = await _converter.ConvertAsync(rawContent, options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(ConversionMethod.Heuristic, result.Method);
        Assert.Contains(result.Warnings, w => w.Contains("IDocumentAnalysisService not available"));
    }

    #endregion

    #region Statistics Tests

    [Fact]
    public async Task ConvertAsync_MixedContent_CollectsStatistics()
    {
        // Arrange
        var content = @"# Main Title
This is a paragraph.

## Section 1
- Item 1
- Item 2

| Col1 | Col2 |
| --- | --- |
| A | B |

```code
example
```";
        var rawContent = new RawContent { Text = content };

        // Act
        var result = await _converter.ConvertAsync(rawContent);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Statistics.HeadingCount >= 2);
        Assert.True(result.Statistics.ListCount >= 1);
        Assert.True(result.Statistics.TableCount >= 1);
        Assert.True(result.Statistics.CodeBlockCount >= 1);
        Assert.True(result.Statistics.HeadingLevelDistribution.Count > 0);
    }

    [Fact]
    public async Task ConvertAsync_TracksOriginalAndMarkdownLength()
    {
        // Arrange
        var text = "Simple text with some content";
        var rawContent = new RawContent { Text = text };

        // Act
        var result = await _converter.ConvertAsync(rawContent);

        // Assert
        Assert.Equal(text.Length, result.OriginalLength);
        Assert.True(result.MarkdownLength > 0);
    }

    #endregion

    #region Options Tests

    [Fact]
    public async Task ConvertAsync_DefaultOptions_AllFeaturesEnabled()
    {
        // Arrange
        var options = new MarkdownConversionOptions();

        // Assert defaults
        Assert.True(options.PreserveHeadings);
        Assert.True(options.ConvertTables);
        Assert.True(options.PreserveLists);
        Assert.True(options.IncludeImagePlaceholders);
        Assert.True(options.DetectCodeBlocks);
        Assert.True(options.NormalizeWhitespace);
        Assert.False(options.UseLLMInference);
        Assert.Equal(1, options.MinHeadingLevel);
        Assert.Equal(6, options.MaxHeadingLevel);
    }

    [Fact]
    public async Task ConvertAsync_AllDisabled_MinimalProcessing()
    {
        // Arrange
        var content = "# Heading\n- List\n| Table |";
        var rawContent = new RawContent { Text = content };
        var options = new MarkdownConversionOptions
        {
            PreserveHeadings = false,
            ConvertTables = false,
            PreserveLists = false,
            IncludeImagePlaceholders = false,
            DetectCodeBlocks = false,
            NormalizeWhitespace = false
        };

        // Act
        var result = await _converter.ConvertAsync(rawContent, options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Statistics.HeadingCount);
        Assert.Equal(0, result.Statistics.TableCount);
        Assert.Equal(0, result.Statistics.ListCount);
    }

    #endregion

    #region Hints Integration Tests

    [Fact]
    public async Task ConvertAsync_WithHints_ProcessesContent()
    {
        // Arrange
        var rawContent = new RawContent
        {
            Text = "Some document content",
            Hints = new Dictionary<string, object>
            {
                ["HasHeadings"] = true,
                ["HasTables"] = true,
                ["DocumentType"] = "PDF"
            }
        };

        // Act
        var result = await _converter.ConvertAsync(rawContent);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(ConversionMethod.Heuristic, result.Method);
    }

    #endregion
}
