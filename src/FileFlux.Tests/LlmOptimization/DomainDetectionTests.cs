using FileFlux.Infrastructure.Strategies;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace FileFlux.Tests.LlmOptimization;

/// <summary>
/// 문서 도메인 자동 탐지 기능 단위 테스트
/// </summary>
public class DomainDetectionTests
{
    private readonly ITestOutputHelper _output;

    public DomainDetectionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("API endpoint for user authentication", "Technical")]
    [InlineData("Database schema design and implementation", "Technical")]
    [InlineData("React component lifecycle and hooks", "Technical")]
    [InlineData("function calculateTotal() { return price * quantity; }", "Technical")]
    [InlineData("class UserService implements IUserService", "Technical")]
    [InlineData("method POST /api/users creates new user", "Technical")]
    public void DetectDocumentDomain_ShouldClassifyTechnicalContent(string content, string expectedDomain)
    {
        // Act
        var result = CallDetectDocumentDomain(content, new List<string> { "API" });

        // Assert
        Assert.Equal(expectedDomain, result);
        _output.WriteLine($"Content: '{content}' -> Domain: {result}");
    }

    [Theory]
    [InlineData("Business requirements and project strategy", "Business")]
    [InlineData("Project timeline and resource allocation", "Business")]
    [InlineData("Strategic planning for next quarter", "Business")]
    [InlineData("requirement analysis and stakeholder management", "Business")]
    public void DetectDocumentDomain_ShouldClassifyBusinessContent(string content, string expectedDomain)
    {
        // Act
        var result = CallDetectDocumentDomain(content, new List<string>());

        // Assert
        Assert.Equal(expectedDomain, result);
        _output.WriteLine($"Content: '{content}' -> Domain: {result}");
    }

    [Theory]
    [InlineData("Research methodology and data analysis", "Academic")]
    [InlineData("Abstract: This study investigates the correlation", "Academic")]
    [InlineData("논문 초록: 본 연구에서는 머신러닝을 활용한", "Academic")]
    [InlineData("Literature review and theoretical framework", "Academic")]
    public void DetectDocumentDomain_ShouldClassifyAcademicContent(string content, string expectedDomain)
    {
        // Act
        var result = CallDetectDocumentDomain(content, new List<string>());

        // Assert
        Assert.Equal(expectedDomain, result);
        _output.WriteLine($"Content: '{content}' -> Domain: {result}");
    }

    [Theory]
    [InlineData("Hello world this is a simple text", "General")]
    [InlineData("Random content without specific domain markers", "General")]
    public void DetectDocumentDomain_ShouldClassifyGeneralContent(string content, string expectedDomain)
    {
        // Act
        var result = CallDetectDocumentDomain(content, new List<string>());

        // Assert
        Assert.Equal(expectedDomain, result);
        _output.WriteLine($"Content: '{content}' -> Domain: {result}");
    }

    [Fact]
    public void DetectDocumentDomain_WithMultipleTechnicalKeywords_ShouldReturnTechnical()
    {
        // Arrange
        var content = "Simple application overview";
        var technicalKeywords = new List<string> { "API", "Database", "Frontend" };

        // Act
        var result = CallDetectDocumentDomain(content, technicalKeywords);

        // Assert
        Assert.Equal("Technical", result);
        _output.WriteLine($"TechKeywords: [{string.Join(", ", technicalKeywords)}] -> Domain: {result}");
    }

    /// <summary>
    /// 리플렉션을 통해 private 메서드 테스트
    /// </summary>
    private static string CallDetectDocumentDomain(string content, List<string> technicalKeywords)
    {
        var method = typeof(IntelligentChunkingStrategy)
            .GetMethod("DetectDocumentDomain", BindingFlags.NonPublic | BindingFlags.Static);

        return (string)method!.Invoke(null, new object[] { content, technicalKeywords })!;
    }
}