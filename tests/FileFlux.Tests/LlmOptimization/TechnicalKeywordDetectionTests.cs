using FileFlux.Infrastructure.Strategies;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace FileFlux.Tests.LlmOptimization;

/// <summary>
/// 기술 키워드 자동 탐지 기능 단위 테스트
/// </summary>
public class TechnicalKeywordDetectionTests
{
    private readonly ITestOutputHelper _output;

    public TechnicalKeywordDetectionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("REST API endpoint for user authentication", new[] { "API" })]
    [InlineData("GraphQL API with Apollo Server", new[] { "API" })]
    [InlineData("Microservice architecture with REST endpoints", new[] { "API", "Backend" })]
    public void DetectTechnicalKeywords_ShouldDetectApiKeywords(string content, string[] expectedKeywords)
    {
        // Act
        var result = CallDetectTechnicalKeywords(content);

        // Assert
        foreach (var expected in expectedKeywords)
        {
            Assert.Contains(expected, result);
        }

        _output.WriteLine($"Content: '{content}'");
        _output.WriteLine($"Detected: [{string.Join(", ", result)}]");
    }

    [Theory]
    [InlineData("PostgreSQL database schema design", new[] { "Database" })]
    [InlineData("SQL query optimization for MongoDB", new[] { "Database" })]
    [InlineData("NoSQL database with Redis caching", new[] { "Database" })]
    public void DetectTechnicalKeywords_ShouldDetectDatabaseKeywords(string content, string[] expectedKeywords)
    {
        // Act
        var result = CallDetectTechnicalKeywords(content);

        // Assert
        foreach (var expected in expectedKeywords)
        {
            Assert.Contains(expected, result);
        }

        _output.WriteLine($"Content: '{content}'");
        _output.WriteLine($"Detected: [{string.Join(", ", result)}]");
    }

    [Theory]
    [InlineData("React component with hooks and TypeScript", new[] { "Frontend" })]
    [InlineData("Vue.js application with responsive UI design", new[] { "Frontend" })]
    [InlineData("Angular frontend development", new[] { "Frontend" })]
    public void DetectTechnicalKeywords_ShouldDetectFrontendKeywords(string content, string[] expectedKeywords)
    {
        // Act
        var result = CallDetectTechnicalKeywords(content);

        // Assert
        foreach (var expected in expectedKeywords)
        {
            Assert.Contains(expected, result);
        }

        _output.WriteLine($"Content: '{content}'");
        _output.WriteLine($"Detected: [{string.Join(", ", result)}]");
    }

    [Theory]
    [InlineData("Docker containerization with Kubernetes orchestration", new[] { "DevOps" })]
    [InlineData("CI/CD pipeline with automated deployment", new[] { "DevOps" })]
    public void DetectTechnicalKeywords_ShouldDetectDevOpsKeywords(string content, string[] expectedKeywords)
    {
        // Act
        var result = CallDetectTechnicalKeywords(content);

        // Assert
        foreach (var expected in expectedKeywords)
        {
            Assert.Contains(expected, result);
        }

        _output.WriteLine($"Content: '{content}'");
        _output.WriteLine($"Detected: [{string.Join(", ", result)}]");
    }

    [Theory]
    [InlineData("Machine learning model with vector embeddings", new[] { "AI/ML" })]
    [InlineData("AI-powered recommendation system", new[] { "AI/ML" })]
    public void DetectTechnicalKeywords_ShouldDetectAiMlKeywords(string content, string[] expectedKeywords)
    {
        // Act
        var result = CallDetectTechnicalKeywords(content);

        // Assert
        foreach (var expected in expectedKeywords)
        {
            Assert.Contains(expected, result);
        }

        _output.WriteLine($"Content: '{content}'");
        _output.WriteLine($"Detected: [{string.Join(", ", result)}]");
    }

    [Fact]
    public void DetectTechnicalKeywords_WithMultipleCategories_ShouldDetectAll()
    {
        // Arrange
        var content = "Full-stack application with React frontend, Node.js backend API, PostgreSQL database, and Docker deployment";

        // Act
        var result = CallDetectTechnicalKeywords(content);

        // Assert
        Assert.Contains("Frontend", result);
        Assert.Contains("Backend", result);
        Assert.Contains("API", result);
        Assert.Contains("Database", result);
        Assert.Contains("DevOps", result);

        _output.WriteLine($"Content: '{content}'");
        _output.WriteLine($"Detected: [{string.Join(", ", result)}]");
        Assert.True(result.Count <= 5, "Should limit to maximum 5 keywords");
    }

    [Fact]
    public void DetectTechnicalKeywords_WithNonTechnicalContent_ShouldReturnEmpty()
    {
        // Arrange
        var content = "This is a simple text about cooking recipes and travel stories";

        // Act
        var result = CallDetectTechnicalKeywords(content);

        // Assert
        Assert.Empty(result);
        _output.WriteLine($"Content: '{content}'");
        _output.WriteLine($"Detected: [{string.Join(", ", result)}] (Expected: empty)");
    }

    /// <summary>
    /// 리플렉션을 통해 private 메서드 테스트
    /// </summary>
    private static List<string> CallDetectTechnicalKeywords(string content)
    {
        var method = typeof(IntelligentChunkingStrategy)
            .GetMethod("DetectTechnicalKeywords", BindingFlags.NonPublic | BindingFlags.Static);

        return (List<string>)method!.Invoke(null, new object[] { content })!;
    }
}