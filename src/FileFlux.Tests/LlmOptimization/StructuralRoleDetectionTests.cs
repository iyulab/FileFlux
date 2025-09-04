using FileFlux.Infrastructure.Strategies;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace FileFlux.Tests.LlmOptimization;

/// <summary>
/// 구조적 역할 자동 탐지 기능 단위 테스트
/// </summary>
public class StructuralRoleDetectionTests
{
    private readonly ITestOutputHelper _output;

    public StructuralRoleDetectionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("# Main Title", "header")]
    [InlineData("## Section Header", "header")]
    [InlineData("### Subsection", "header")]
    [InlineData("#### Deep Header", "header")]
    public void DetectStructuralRole_ShouldDetectHeaders(string content, string expectedRole)
    {
        // Act
        var result = CallDetectStructuralRole(content);

        // Assert
        Assert.Equal(expectedRole, result);
        _output.WriteLine($"Content: '{content}' -> Role: {result}");
    }

    [Theory]
    [InlineData("| Name | Age | City |\n|------|-----|------|\n| John | 25  | NYC  |", "table")]
    [InlineData("| Header 1 | Header 2 |\n| Data 1   | Data 2   |", "table")]
    [InlineData("First | Second | Third", "table")]
    public void DetectStructuralRole_ShouldDetectTables(string content, string expectedRole)
    {
        // Act
        var result = CallDetectStructuralRole(content);

        // Assert
        Assert.Equal(expectedRole, result);
        _output.WriteLine($"Content: '{content.Replace("\n", "\\n")}' -> Role: {result}");
    }

    [Theory]
    [InlineData("```javascript\nconsole.log('Hello');\n```", "code_block")]
    [InlineData("```\nSome code here\n```", "code_block")]
    [InlineData("Regular text with ```inline code``` block", "code_block")]
    public void DetectStructuralRole_ShouldDetectCodeBlocks(string content, string expectedRole)
    {
        // Act
        var result = CallDetectStructuralRole(content);

        // Assert
        Assert.Equal(expectedRole, result);
        _output.WriteLine($"Content: '{content.Replace("\n", "\\n")}' -> Role: {result}");
    }

    [Theory]
    [InlineData("- First item\n- Second item", "list")]
    [InlineData("* Bullet point\n* Another point", "list")]
    [InlineData("1. First numbered item\n2. Second item", "list")]
    [InlineData("10. Tenth item in a list", "list")]
    public void DetectStructuralRole_ShouldDetectLists(string content, string expectedRole)
    {
        // Act
        var result = CallDetectStructuralRole(content);

        // Assert
        Assert.Equal(expectedRole, result);
        _output.WriteLine($"Content: '{content.Replace("\n", "\\n")}' -> Role: {result}");
    }

    [Theory]
    [InlineData("This is regular paragraph content without special formatting.", "content")]
    [InlineData("A normal sentence with some words and punctuation.", "content")]
    [InlineData("Regular text that doesn't match any special patterns.", "content")]
    public void DetectStructuralRole_ShouldDetectRegularContent(string content, string expectedRole)
    {
        // Act
        var result = CallDetectStructuralRole(content);

        // Assert
        Assert.Equal(expectedRole, result);
        _output.WriteLine($"Content: '{content}' -> Role: {result}");
    }

    [Theory]
    [InlineData("   # Title with leading spaces", "header")]
    [InlineData("\t\t- Indented list item", "list")]
    [InlineData("  | Col1 | Col2 |  ", "table")]
    public void DetectStructuralRole_ShouldHandleWhitespace(string content, string expectedRole)
    {
        // Act
        var result = CallDetectStructuralRole(content);

        // Assert
        Assert.Equal(expectedRole, result);
        _output.WriteLine($"Content: '{content}' -> Role: {result}");
    }

    [Fact]
    public void DetectStructuralRole_WithEmptyContent_ShouldReturnContent()
    {
        // Act
        var result = CallDetectStructuralRole("");

        // Assert
        Assert.Equal("content", result);
        _output.WriteLine($"Empty content -> Role: {result}");
    }

    [Fact]
    public void DetectStructuralRole_WithMultiplePatterns_ShouldPrioritizeFirst()
    {
        // Arrange - Header가 먼저 매치되어야 함
        var content = "# Header with | table | characters |";

        // Act
        var result = CallDetectStructuralRole(content);

        // Assert
        Assert.Equal("header", result); // 헤더가 우선순위
        _output.WriteLine($"Content: '{content}' -> Role: {result} (Header takes priority)");
    }

    /// <summary>
    /// 리플렉션을 통해 private 메서드 테스트
    /// </summary>
    private static string CallDetectStructuralRole(string content)
    {
        var method = typeof(IntelligentChunkingStrategy)
            .GetMethod("DetectStructuralRole", BindingFlags.NonPublic | BindingFlags.Static);

        return (string)method!.Invoke(null, new object[] { content })!;
    }
}