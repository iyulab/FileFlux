using AwesomeAssertions;
using FileFlux.Core;
using FileFlux.Infrastructure.Services;

namespace FileFlux.Tests.Services;

/// <summary>
/// The rule-based extractor labelled a document by a single character: any Hangul syllable made it Korean, any CJK ideograph
/// made it Chinese (checked before kana, so ordinary Japanese with kanji read as Chinese), and every other script - French,
/// German, Spanish - fell through to English. The label is stored with the document and keys language-specific behaviour
/// downstream, so each mislabel below is a document the next stage handles in the wrong language.
/// </summary>
public class RuleBasedMetadataExtractorLanguageTests
{
    private readonly RuleBasedMetadataExtractor _extractor = new();

    private async Task<string> LanguageOf(string content)
    {
        var result = await _extractor.ExtractAsync(content, MetadataSchema.General, TestContext.Current.CancellationToken);
        return (string)result["language"];
    }

    [Fact]
    public async Task EnglishDocument_MentioningOneKoreanWord_IsStillEnglish()
    {
        var content = "The quarterly report was prepared by the regional office. The team in Seoul, led by 김민수, reviewed the figures with the finance " +
                      "department and confirmed that revenue grew in every market. The next review is scheduled for the end of the month, and the board " +
                      "will receive the final summary before it meets.";

        (await LanguageOf(content)).Should().Be("en");
    }

    [Fact]
    public async Task JapaneseDocument_WithKanji_IsJapanese()
    {
        var content = "東京は日本の首都であり、世界でも有数の大都市です。多くの企業が本社を置いており、交通網もよく発達しています。春には桜が咲き、" +
                      "たくさんの観光客が訪れます。";

        (await LanguageOf(content)).Should().Be("ja");
    }

    [Fact]
    public async Task FrenchDocument_IsNotLabelledEnglish()
    {
        var content = "Le conseil d'administration s'est réuni hier pour examiner le budget de l'année prochaine. Plusieurs membres ont souligné que les " +
                      "coûts de maintenance n'avaient pas été pris en compte dans l'estimation précédente, et la présidente a proposé de diviser le projet " +
                      "en deux phases afin de faciliter le suivi par les actionnaires.";

        (await LanguageOf(content)).Should().Be("fr");
    }

    [Fact]
    public async Task KoreanDocument_WithEnglishTerms_IsKorean()
    {
        var content = "이번 분기 보고서는 RAG 파이프라인의 성능을 다룹니다. API 응답 시간이 줄었고, 검색 정확도는 크게 향상되었습니다. 다음 단계에서는 " +
                      "캐시 정책을 검토하고 운영 환경에 배포할 계획입니다.";

        (await LanguageOf(content)).Should().Be("ko");
    }

    [Fact]
    public async Task ChineseDocument_IsStillChinese()
    {
        var content = "北京是中国的首都，也是全国的政治和文化中心。这座城市拥有悠久的历史，每年都有大量游客前来参观故宫和长城。";

        (await LanguageOf(content)).Should().Be("zh");
    }

    [Fact]
    public async Task EmptyDocument_HasNoLanguage()
    {
        var result = await _extractor.ExtractAsync("   ", MetadataSchema.General, TestContext.Current.CancellationToken);

        result.TryGetValue("language", out var language).Should().BeFalse("empty content returns the minimal metadata without a language");
        language.Should().BeNull();
    }
}
