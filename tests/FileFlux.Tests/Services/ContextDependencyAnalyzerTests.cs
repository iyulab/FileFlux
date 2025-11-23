using FileFlux.Infrastructure.Services;
using Xunit;

namespace FileFlux.Tests.Services;

public class ContextDependencyAnalyzerTests
{
    [Fact]
    public void Calculate_IndependentText_ReturnsLowScore()
    {
        // Arrange - Text with proper nouns, complete sentences, no pronouns
        var text = "Microsoft Corporation released Windows 11 in October 2021. " +
                   "The operating system includes new features like snap layouts and virtual desktops. " +
                   "Satya Nadella announced the update during a virtual event.";

        // Act
        var score = ContextDependencyAnalyzer.Calculate(text);

        // Assert
        Assert.True(score < 0.5, $"Independent text should have low dependency score, got {score}");
    }

    [Fact]
    public void Calculate_DependentText_ReturnsHighScore()
    {
        // Arrange - Text with many pronouns and references
        var text = "It was mentioned earlier that this approach works best. " +
                   "He explained how they implemented it using the previous method. " +
                   "These results confirm what she said about them.";

        // Act
        var score = ContextDependencyAnalyzer.Calculate(text);

        // Assert
        Assert.True(score > 0.3, $"Dependent text should have higher dependency score, got {score}");
    }

    [Fact]
    public void Calculate_EmptyText_ReturnsZero()
    {
        // Act
        var score = ContextDependencyAnalyzer.Calculate("");

        // Assert
        Assert.Equal(0.0, score);
    }

    [Fact]
    public void Calculate_NullText_ReturnsZero()
    {
        // Act
        var score = ContextDependencyAnalyzer.Calculate(null!);

        // Assert
        Assert.Equal(0.0, score);
    }

    [Fact]
    public void Calculate_KoreanText_WorksCorrectly()
    {
        // Arrange - Korean text with references
        var text = "이것은 앞서 언급한 방법입니다. 그것을 사용하여 이 결과를 얻었습니다.";

        // Act
        var score = ContextDependencyAnalyzer.Calculate(text, "ko");

        // Assert
        Assert.True(score >= 0 && score <= 1, $"Score should be between 0 and 1, got {score}");
    }

    [Fact]
    public void Calculate_ScoreIsNormalized()
    {
        // Arrange
        var texts = new[]
        {
            "Simple text.",
            "This is a longer text with more content and details about various topics.",
            "He said that it was there. They knew this would happen to them."
        };

        // Act & Assert
        foreach (var text in texts)
        {
            var score = ContextDependencyAnalyzer.Calculate(text);
            Assert.True(score >= 0.0 && score <= 1.0,
                $"Score {score} should be normalized between 0 and 1");
        }
    }

    [Fact]
    public void Calculate_TextWithPronouns_HigherThanWithout()
    {
        // Arrange
        var withPronouns = "He went to the store. She was already there. They bought it together.";
        var withoutPronouns = "John went to the store. Mary was already there. The couple bought groceries together.";

        // Act
        var scoreWithPronouns = ContextDependencyAnalyzer.Calculate(withPronouns);
        var scoreWithoutPronouns = ContextDependencyAnalyzer.Calculate(withoutPronouns);

        // Assert
        Assert.True(scoreWithPronouns > scoreWithoutPronouns,
            $"Text with pronouns ({scoreWithPronouns}) should have higher score than without ({scoreWithoutPronouns})");
    }
}
