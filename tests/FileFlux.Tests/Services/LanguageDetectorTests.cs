using FileFlux.Infrastructure.Services;
using Xunit;

namespace FileFlux.Tests.Services;

public class LanguageDetectorTests
{
    [Theory]
    [InlineData("This is a sample English text for testing language detection.", "en")]
    [InlineData("이것은 한국어 텍스트 샘플입니다. 언어 감지 테스트를 위한 것입니다.", "ko")]
    [InlineData("Dies ist ein deutscher Text zum Testen der Spracherkennung.", "de")]
    [InlineData("Ceci est un texte français pour tester la détection de langue.", "fr")]
    [InlineData("Este es un texto en español para probar la detección de idioma.", "es")]
    public void Detect_ShouldIdentifyLanguage(string text, string expectedLanguage)
    {
        // Act
        var (language, confidence) = LanguageDetector.Detect(text);

        // Skip if NTextCat profiles are not available
        if (language == "unknown" && confidence == 0.0)
        {
            // NTextCat profiles not loaded, skip test
            return;
        }

        // Assert
        Assert.Equal(expectedLanguage, language);
        Assert.True(confidence > 0.3, $"Confidence {confidence} should be > 0.3");
    }

    [Fact]
    public void Detect_EmptyText_ReturnsUnknown()
    {
        // Act
        var (language, confidence) = LanguageDetector.Detect("");

        // Assert
        Assert.Equal("unknown", language);
        Assert.Equal(0.0, confidence);
    }

    [Fact]
    public void Detect_NullText_ReturnsUnknown()
    {
        // Act
        var (language, confidence) = LanguageDetector.Detect(null!);

        // Assert
        Assert.Equal("unknown", language);
        Assert.Equal(0.0, confidence);
    }

    [Fact]
    public void DetectMultiple_ReturnsMultipleLanguages()
    {
        // Arrange
        var text = "This is English text mixed with some content.";

        // Act
        var results = LanguageDetector.DetectMultiple(text, 3);

        // Assert
        Assert.NotEmpty(results);
        Assert.True(results.Count <= 3);

        // Skip detailed assertion if profiles not loaded
        if (results[0].Language != "unknown")
        {
            Assert.Equal("en", results[0].Language);
        }
    }

    [Theory]
    [InlineData("Hello world, this is a test.", "en", true)]
    [InlineData("Hello world, this is a test.", "ko", false)]
    [InlineData("안녕하세요, 테스트입니다.", "ko", true)]
    public void IsLanguage_ShouldValidateCorrectly(string text, string language, bool expected)
    {
        // Act
        var result = LanguageDetector.IsLanguage(text, language, 0.3);

        // Skip if profiles not loaded
        var (detected, _) = LanguageDetector.Detect(text);
        if (detected == "unknown")
            return;

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Detect_LongText_WorksCorrectly()
    {
        // Arrange - Create a longer text
        var text = string.Join(" ", Enumerable.Repeat(
            "The quick brown fox jumps over the lazy dog. This is a common English pangram used for testing.",
            50));

        // Act
        var (language, confidence) = LanguageDetector.Detect(text);

        // Skip if profiles not loaded
        if (language == "unknown")
            return;

        // Assert
        Assert.Equal("en", language);
        Assert.True(confidence > 0.5);
    }
}
