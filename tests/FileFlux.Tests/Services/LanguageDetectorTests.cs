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

        // Assert - the NTextCat language profile is now embedded in FileFlux itself, so it is
        // always available; a silent "unknown" here is a real regression, not an environment gap.
        Assert.Equal(expectedLanguage, language);
        Assert.True(confidence > 0.3, $"Confidence {confidence} should be > 0.3");
    }

    [Fact]
    public void Detect_KnownEnglishSample_NeverReturnsUnknown()
    {
        // Regression test for the Core14.profile.xml load failure: NTextCat ships that file as
        // legacy NuGet "content/", which SDK-style PackageReference projects never copy to output,
        // so the profile silently failed to load and every call returned ("unknown", 0.0) with no
        // error. FileFlux now embeds its own copy of the profile.
        var (language, confidence) = LanguageDetector.Detect(
            "The quarterly report shows steady revenue growth across all regional markets.");

        Assert.Equal("en", language);
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
        Assert.Equal("en", results[0].Language);
    }

    // The candidate list is a distribution: confidences sum to 1, ordered best first. NTextCat distances sit near 4000 for
    // every candidate, so normalising their inverses answered about 0.33 for each of three candidates whatever the text.
    [Theory]
    [InlineData("The quarterly review shows revenue grew in every region and we plan to hire two more engineers next quarter.", "en")]
    [InlineData("Der Umsatz ist im letzten Quartal in jeder Region gewachsen und wir planen zwei weitere Ingenieure einzustellen.", "de")]
    public void DetectMultiple_AParagraph_IsOneClearAnswerAndASmallTail(string text, string expected)
    {
        var results = LanguageDetector.DetectMultiple(text, 3);

        Assert.Equal(expected, results[0].Language);
        Assert.True(results[0].Confidence >= 0.8, $"top confidence {results[0].Confidence} should be >= 0.8 for a paragraph");
        Assert.InRange(results.Sum(r => r.Confidence), 0.999, 1.001);
        Assert.Equal(results.OrderByDescending(r => r.Confidence).Select(r => r.Language), results.Select(r => r.Language));
    }

    [Fact]
    public void DetectMultiple_AFragmentWithNoMargin_DoesNotPretendToKnow()
    {
        var results = LanguageDetector.DetectMultiple("Invoice", 3);

        Assert.True(results[0].Confidence < 0.6, $"top confidence {results[0].Confidence} should say it is close to a coin flip");
        Assert.InRange(results.Sum(r => r.Confidence), 0.999, 1.001);
    }

    [Fact]
    public void DetectMultiple_AKoreanText_IsKorean_NotNTextCatsGuessOverLatinProfiles()
    {
        var results = LanguageDetector.DetectMultiple("이것은 한국어 텍스트 샘플입니다. 언어 감지 테스트를 위한 것입니다.", 3);

        var only = Assert.Single(results);
        Assert.Equal("ko", only.Language);
        Assert.True(only.Confidence >= 0.6);
    }

    [Fact]
    public void DetectMultiple_AgreesWithDetectOnTheTopLanguage()
    {
        const string text = "Los ingresos crecieron en todas las regiones el trimestre pasado.";

        Assert.Equal(LanguageDetector.Detect(text).Language, LanguageDetector.DetectMultiple(text, 3)[0].Language);
    }

    [Theory]
    [InlineData("Hello world, this is a test.", "en", true)]
    [InlineData("Hello world, this is a test.", "ko", false)]
    [InlineData("안녕하세요, 테스트입니다.", "ko", true)]
    public void IsLanguage_ShouldValidateCorrectly(string text, string language, bool expected)
    {
        // Act
        var result = LanguageDetector.IsLanguage(text, language, 0.3);

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

        // Assert
        Assert.Equal("en", language);
        Assert.True(confidence > 0.5);
    }

    // NTextCat scores are distances near 4000 for every candidate; for a few words the best and second best are tied or
    // one apart, and the old reading labelled such text anyway ("Invoice" -> pt). A guess is "unknown", not a label.
    [Theory]
    [InlineData("Invoice")]
    [InlineData("Factuur")]
    [InlineData("Total due: 42.00 EUR Invoice 2026-1113")]
    [InlineData("Importo totale: 42,00 EUR")]
    [InlineData("Montant total : 42,00 EUR")]
    public void Detect_AFragmentWithNoMargin_IsUnknownNotAGuess(string text)
    {
        var (language, confidence) = LanguageDetector.Detect(text);

        Assert.Equal("unknown", language);
        Assert.Equal(0.0, confidence);
    }

    // A sentence of 50-65 characters in each Latin-script language keeps its label, with a confidence that says it is not a
    // coin flip (the old scale answered 0.50-0.51 for everything).
    [Theory]
    [InlineData("en", "Please send me the signed contract by Friday morning.")]
    [InlineData("de", "Der Umsatz ist im letzten Quartal in jeder Region gewachsen.")]
    [InlineData("fr", "Le chiffre d'affaires a augmenté dans chaque région ce trimestre.")]
    [InlineData("es", "Los ingresos crecieron en todas las regiones el trimestre pasado.")]
    [InlineData("it", "Il fatturato è cresciuto in ogni regione nell'ultimo trimestre.")]
    [InlineData("pt", "Por favor envie-me o contrato assinado até sexta-feira de manhã.")]
    [InlineData("nl", "De omzet is vorig kwartaal in elke regio gegroeid.")]
    public void Detect_ASentence_KeepsItsLabelWithARealConfidence(string expected, string text)
    {
        var (language, confidence) = LanguageDetector.Detect(text);

        Assert.Equal(expected, language);
        Assert.True(confidence >= 0.6, $"Confidence {confidence} should be >= 0.6 for a sentence");
    }

    [Fact]
    public void Detect_ConfidenceGrowsWithTheEvidence()
    {
        var sentence = LanguageDetector.Detect("This is a sample English text for testing language detection.");
        var paragraph = LanguageDetector.Detect(
            "The quarterly review shows revenue grew in every region and we plan to hire two more engineers next quarter.");

        Assert.Equal("en", sentence.Language);
        Assert.Equal("en", paragraph.Language);
        Assert.True(paragraph.Confidence > sentence.Confidence + 0.1,
            $"paragraph {paragraph.Confidence} should be clearly above sentence {sentence.Confidence}");
    }

    // The documented default threshold (0.6) was unreachable for any Latin-script text before the confidence was calibrated.
    [Fact]
    public void IsLanguage_DefaultThreshold_IsReachableForALatinSentence()
    {
        const string german = "Dies ist ein deutscher Text zum Testen der Spracherkennung.";

        Assert.True(LanguageDetector.IsLanguage(german, "de"));
        Assert.False(LanguageDetector.IsLanguage(german, "en"));
    }
}
