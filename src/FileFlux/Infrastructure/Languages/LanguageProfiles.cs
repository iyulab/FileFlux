using System.Text.RegularExpressions;
using FileFlux.Core;

namespace FileFlux.Infrastructure.Languages;

/// <summary>
/// English language profile - Default fallback
/// </summary>
public sealed class EnglishLanguageProfile : LanguageProfileBase
{
    public static readonly EnglishLanguageProfile Instance = new();

    public override string LanguageCode => "en";
    public override string LanguageName => "English";
    public override string ScriptCode => "Latn";
    public override WritingDirection WritingDirection => WritingDirection.LeftToRight;
    public override NumberFormat NumberFormat => NumberFormat.Standard;
    public override QuotationMarks QuotationMarks => QuotationMarks.English;

    public override Regex SentenceEndPattern { get; } = new(
        @"[.!?]+(?:\s|$)|[.!?]+(?=[""'\)\]}>])",
        RegexOptions.Compiled);

    public override Regex SectionMarkerPattern { get; } = new(
        @"^#{1,6}\s+.+$|" +                          // Markdown headers
        @"^\s*\d+\.\s+.+$|" +                         // Numbered sections
        @"^\s*[A-Z]\.\s+.+$|" +                       // Letter sections
        @"^\s*(?:Chapter|Section|Part)\s+\d+",        // Named sections
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public override IReadOnlyList<string> Abbreviations { get; } = new[]
    {
        "Dr", "Mr", "Mrs", "Ms", "Prof", "Sr", "Jr",
        "vs", "etc", "Inc", "Ltd", "Corp",
        "Jan", "Feb", "Mar", "Apr", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
        "St", "Ave", "Blvd", "Rd",
        "U.S", "U.K", "E.U"
    };

    public override IReadOnlyList<Abbreviation> CategorizedAbbreviations { get; } = new Abbreviation[]
    {
        new("Dr", AbbreviationType.Prepositive),
        new("Mr", AbbreviationType.Prepositive),
        new("Mrs", AbbreviationType.Prepositive),
        new("Ms", AbbreviationType.Prepositive),
        new("Prof", AbbreviationType.Prepositive),
        new("Rev", AbbreviationType.Prepositive),
        new("Gen", AbbreviationType.Prepositive),
        new("Col", AbbreviationType.Prepositive),
        new("Lt", AbbreviationType.Prepositive),
        new("Sr", AbbreviationType.Postpositive),
        new("Jr", AbbreviationType.Postpositive),
        new("Inc", AbbreviationType.Postpositive),
        new("Ltd", AbbreviationType.Postpositive),
        new("Corp", AbbreviationType.Postpositive),
        new("vs", AbbreviationType.General),
        new("etc", AbbreviationType.General),
        new("al", AbbreviationType.General),
        new("fig", AbbreviationType.General),
        new("vol", AbbreviationType.General)
    };

    public override IReadOnlyList<string> NonBreakingPrefixes { get; } = new[]
    {
        "Mr", "Mrs", "Ms", "Dr", "Prof", "Sr", "Jr", "Rev", "Gen", "Col", "Lt", "Cmdr",
        "Mt", "St", "vs", "etc", "al", "fig", "Fig", "no", "No", "vol", "Vol"
    };
}

/// <summary>
/// Korean language profile
/// </summary>
public sealed class KoreanLanguageProfile : LanguageProfileBase
{
    public static readonly KoreanLanguageProfile Instance = new();

    public override string LanguageCode => "ko";
    public override string LanguageName => "Korean";
    public override string ScriptCode => "Hang";  // Hangul
    public override WritingDirection WritingDirection => WritingDirection.LeftToRight;
    public override NumberFormat NumberFormat => NumberFormat.Standard;  // Korea uses US-style
    public override QuotationMarks QuotationMarks => QuotationMarks.Korean;

    public override Regex SentenceEndPattern { get; } = new(
        @"(?:습니다|입니다|됩니다|합니다|있습니다|했습니다|됐습니다|겠습니다|" +
        @"습니까|입니까|됩니까|합니까|" +
        @"세요|하세요|되세요|주세요|" +
        @"(?<=[가-힣])(?:다|음|됨|함|임|것)(?:\s*$|\s+)|" +
        @"(?<=[가-힣])(?:요|죠|네)(?:\s*$|\s+)|" +
        @"[.!?。！？]+(?:\s|$))",
        RegexOptions.Compiled);

    public override Regex SectionMarkerPattern { get; } = new(
        @"^#{1,6}\s+.+$|" +                                    // Markdown headers
        @"^\s*[□■◇◆○●◎▶▷►★☆ㅇ]\s+.+$|" +                       // Korean section markers
        @"^\s*[가나다라마바사아자차카타파하]\s*[.．\)）]\s+.+$|" +  // Korean letter list
        @"^\s*\d+\s*[.．\)）]\s+.+$|" +                          // Numbered list
        @"^\s*제\s*\d+\s*[장절조항]\s*.+$",                      // Korean chapter/section
        RegexOptions.Compiled | RegexOptions.Multiline);

    public override IReadOnlyList<string> Abbreviations { get; } = new[]
    {
        "등", "외", "약", "예", "즉", "단"
    };
}

/// <summary>
/// Chinese (Simplified/Traditional) language profile
/// </summary>
public sealed class ChineseLanguageProfile : LanguageProfileBase
{
    public static readonly ChineseLanguageProfile Instance = new();

    public override string LanguageCode => "zh";
    public override string LanguageName => "Chinese";
    public override string ScriptCode => "Hans";  // Simplified Chinese (use Hant for Traditional)
    public override WritingDirection WritingDirection => WritingDirection.LeftToRight;
    public override NumberFormat NumberFormat => NumberFormat.NoGrouping;  // Chinese typically doesn't use thousands separator
    public override QuotationMarks QuotationMarks => QuotationMarks.Chinese;

    public override Regex SentenceEndPattern { get; } = new(
        @"[。！？；]+|[.!?;]+(?:\s|$)",
        RegexOptions.Compiled);

    public override Regex SectionMarkerPattern { get; } = new(
        @"^#{1,6}\s+.+$|" +                          // Markdown headers
        @"^\s*第[一二三四五六七八九十百千\d]+[章节条款]\s*|" +  // Chinese chapter markers
        @"^\s*[一二三四五六七八九十]+[、.．]\s*|" +            // Chinese number list
        @"^\s*（[一二三四五六七八九十\d]+）\s*|" +             // Parenthesized numbers
        @"^\s*[■□●○◆◇▶▷]\s+",                        // Bullet markers
        RegexOptions.Compiled | RegexOptions.Multiline);
}

/// <summary>
/// Japanese language profile
/// </summary>
public sealed class JapaneseLanguageProfile : LanguageProfileBase
{
    public static readonly JapaneseLanguageProfile Instance = new();

    public override string LanguageCode => "ja";
    public override string LanguageName => "Japanese";
    public override string ScriptCode => "Jpan";  // Japanese (includes Kanji, Hiragana, Katakana)
    public override WritingDirection WritingDirection => WritingDirection.LeftToRight;  // Modern horizontal
    public override NumberFormat NumberFormat => NumberFormat.Standard;  // Japan uses US-style
    public override QuotationMarks QuotationMarks => QuotationMarks.Japanese;

    public override Regex SentenceEndPattern { get; } = new(
        @"[。！？]+|[.!?]+(?:\s|$)|(?:です|ます|でした|ました)(?:[。！？]|\s|$)",
        RegexOptions.Compiled);

    public override Regex SectionMarkerPattern { get; } = new(
        @"^#{1,6}\s+.+$|" +                          // Markdown headers
        @"^\s*第[一二三四五六七八九十百千\d]+[章節条項]\s*|" +  // Japanese chapter markers
        @"^\s*[一二三四五六七八九十]+[、.．]\s*|" +            // Japanese number list
        @"^\s*（[一二三四五六七八九十\d]+）\s*|" +             // Parenthesized numbers
        @"^\s*[■□●○◆◇▶▷]\s+",                        // Bullet markers
        RegexOptions.Compiled | RegexOptions.Multiline);
}

/// <summary>
/// Spanish language profile
/// </summary>
public sealed class SpanishLanguageProfile : LanguageProfileBase
{
    public static readonly SpanishLanguageProfile Instance = new();

    public override string LanguageCode => "es";
    public override string LanguageName => "Spanish";
    public override string ScriptCode => "Latn";
    public override WritingDirection WritingDirection => WritingDirection.LeftToRight;
    public override NumberFormat NumberFormat => NumberFormat.European;  // Spain uses 1.234,56
    public override QuotationMarks QuotationMarks => new()
    {
        PrimaryOpen = "\u00AB", PrimaryClose = "\u00BB",
        SecondaryOpen = "\u201C", SecondaryClose = "\u201D"
    };

    public override Regex SentenceEndPattern { get; } = new(
        @"[.!?¡¿]+(?:\s|$)|[.!?]+(?=[""'\)\]}>])",
        RegexOptions.Compiled);

    public override Regex SectionMarkerPattern { get; } = new(
        @"^#{1,6}\s+.+$|" +                          // Markdown headers
        @"^\s*\d+\.\s+.+$|" +                         // Numbered sections
        @"^\s*[A-Za-z]\.\s+.+$|" +                    // Letter sections
        @"^\s*(?:Capítulo|Sección|Parte)\s+\d+",      // Named sections
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public override IReadOnlyList<string> Abbreviations { get; } = new[]
    {
        "Dr", "Dra", "Sr", "Sra", "Srta", "Prof",
        "etc", "Ej", "pág", "págs", "núm", "tel",
        "Ud", "Uds", "Vd", "Vds"
    };

    public override IReadOnlyList<Abbreviation> CategorizedAbbreviations { get; } = new Abbreviation[]
    {
        new("Dr", AbbreviationType.Prepositive),
        new("Dra", AbbreviationType.Prepositive),
        new("Sr", AbbreviationType.Prepositive),
        new("Sra", AbbreviationType.Prepositive),
        new("Srta", AbbreviationType.Prepositive),
        new("Prof", AbbreviationType.Prepositive),
        new("etc", AbbreviationType.General),
        new("Ej", AbbreviationType.General),
        new("pág", AbbreviationType.General),
        new("núm", AbbreviationType.General)
    };
}

/// <summary>
/// French language profile
/// </summary>
public sealed class FrenchLanguageProfile : LanguageProfileBase
{
    public static readonly FrenchLanguageProfile Instance = new();

    public override string LanguageCode => "fr";
    public override string LanguageName => "French";
    public override string ScriptCode => "Latn";
    public override WritingDirection WritingDirection => WritingDirection.LeftToRight;
    public override NumberFormat NumberFormat => NumberFormat.SpaceSeparated;  // France uses 1 234,56
    public override QuotationMarks QuotationMarks => QuotationMarks.French;

    // French uses space before ? ! ; :
    public override Regex SentenceEndPattern { get; } = new(
        @"[.]+(?:\s|$)|(?:\s)?[!?]+(?:\s|$)|[.!?]+(?=[""'\)\]}>«»])",
        RegexOptions.Compiled);

    public override Regex SectionMarkerPattern { get; } = new(
        @"^#{1,6}\s+.+$|" +                          // Markdown headers
        @"^\s*\d+\.\s+.+$|" +                         // Numbered sections
        @"^\s*[A-Za-z]\.\s+.+$|" +                    // Letter sections
        @"^\s*(?:Chapitre|Section|Partie)\s+\d+",     // Named sections
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public override IReadOnlyList<string> Abbreviations { get; } = new[]
    {
        "Dr", "M", "Mme", "Mlle", "Prof",
        "etc", "ex", "p", "pp", "vol",
        "cf", "fig", "chap"
    };

    public override IReadOnlyList<Abbreviation> CategorizedAbbreviations { get; } = new Abbreviation[]
    {
        new("Dr", AbbreviationType.Prepositive),
        new("M", AbbreviationType.Prepositive),
        new("Mme", AbbreviationType.Prepositive),
        new("Mlle", AbbreviationType.Prepositive),
        new("Prof", AbbreviationType.Prepositive),
        new("etc", AbbreviationType.General),
        new("ex", AbbreviationType.General),
        new("cf", AbbreviationType.General),
        new("fig", AbbreviationType.General),
        new("chap", AbbreviationType.General)
    };
}

/// <summary>
/// German language profile
/// </summary>
public sealed class GermanLanguageProfile : LanguageProfileBase
{
    public static readonly GermanLanguageProfile Instance = new();

    public override string LanguageCode => "de";
    public override string LanguageName => "German";
    public override string ScriptCode => "Latn";
    public override WritingDirection WritingDirection => WritingDirection.LeftToRight;
    public override NumberFormat NumberFormat => NumberFormat.European;  // Germany uses 1.234,56
    public override QuotationMarks QuotationMarks => QuotationMarks.German;

    public override Regex SentenceEndPattern { get; } = new(
        "[.!?]+(?:\\s|$)|[.!?]+(?=[\"'\\)\\]}>„\u201C])",
        RegexOptions.Compiled);

    public override Regex SectionMarkerPattern { get; } = new(
        @"^#{1,6}\s+.+$|" +                          // Markdown headers
        @"^\s*\d+\.\s+.+$|" +                         // Numbered sections
        @"^\s*[A-Za-z]\.\s+.+$|" +                    // Letter sections
        @"^\s*(?:Kapitel|Abschnitt|Teil)\s+\d+",      // Named sections
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public override IReadOnlyList<string> Abbreviations { get; } = new[]
    {
        "Dr", "Hr", "Fr", "Prof",
        "z.B", "d.h", "u.a", "usw", "etc",
        "Nr", "Bd", "S", "Aufl"
    };

    public override IReadOnlyList<Abbreviation> CategorizedAbbreviations { get; } = new Abbreviation[]
    {
        new("Dr", AbbreviationType.Prepositive),
        new("Hr", AbbreviationType.Prepositive),
        new("Fr", AbbreviationType.Prepositive),
        new("Prof", AbbreviationType.Prepositive),
        new("z.B", AbbreviationType.General),
        new("d.h", AbbreviationType.General),
        new("u.a", AbbreviationType.General),
        new("usw", AbbreviationType.General),
        new("etc", AbbreviationType.General),
        new("Nr", AbbreviationType.General),
        new("Bd", AbbreviationType.General),
        new("S", AbbreviationType.General),
        new("Aufl", AbbreviationType.General)
    };
}

/// <summary>
/// Arabic language profile
/// </summary>
public sealed class ArabicLanguageProfile : LanguageProfileBase
{
    public static readonly ArabicLanguageProfile Instance = new();

    public override string LanguageCode => "ar";
    public override string LanguageName => "Arabic";
    public override string ScriptCode => "Arab";
    public override WritingDirection WritingDirection => WritingDirection.RightToLeft;
    public override NumberFormat NumberFormat => NumberFormat.Standard;  // Arabic uses Western numerals with US-style
    public override QuotationMarks QuotationMarks => new()
    {
        PrimaryOpen = "\u00AB", PrimaryClose = "\u00BB",
        SecondaryOpen = "\u2039", SecondaryClose = "\u203A"
    };

    public override Regex SentenceEndPattern { get; } = new(
        @"[.。۔؟!！？]+(?:\s|$)",  // Arabic/Persian full stop and question mark
        RegexOptions.Compiled | RegexOptions.RightToLeft);

    public override Regex SectionMarkerPattern { get; } = new(
        @"^#{1,6}\s+.+$|" +                          // Markdown headers
        @"^\s*\d+[.．\)）]\s*|" +                      // Numbered sections
        @"^\s*[■□●○◆◇]\s+",                          // Bullet markers
        RegexOptions.Compiled | RegexOptions.Multiline);
}

/// <summary>
/// Hindi language profile
/// </summary>
public sealed class HindiLanguageProfile : LanguageProfileBase
{
    public static readonly HindiLanguageProfile Instance = new();

    public override string LanguageCode => "hi";
    public override string LanguageName => "Hindi";
    public override string ScriptCode => "Deva";  // Devanagari
    public override WritingDirection WritingDirection => WritingDirection.LeftToRight;
    public override NumberFormat NumberFormat => NumberFormat.Standard;  // India uses US-style for most purposes
    public override QuotationMarks QuotationMarks => QuotationMarks.English;  // Hindi typically uses English-style quotes

    // Hindi uses Devanagari Danda (।) and Double Danda (॥) as sentence terminators
    public override Regex SentenceEndPattern { get; } = new(
        @"[।॥]+(?:\s|$)|[.!?]+(?:\s|$)",
        RegexOptions.Compiled);

    public override Regex SectionMarkerPattern { get; } = new(
        @"^#{1,6}\s+.+$|" +                          // Markdown headers
        @"^\s*\d+[.．\)）]\s*|" +                      // Numbered sections
        @"^\s*[क-ह][.．\)）]\s*|" +                    // Hindi letter list
        @"^\s*[■□●○◆◇]\s+",                          // Bullet markers
        RegexOptions.Compiled | RegexOptions.Multiline);
}

/// <summary>
/// Portuguese language profile
/// </summary>
public sealed class PortugueseLanguageProfile : LanguageProfileBase
{
    public static readonly PortugueseLanguageProfile Instance = new();

    public override string LanguageCode => "pt";
    public override string LanguageName => "Portuguese";
    public override string ScriptCode => "Latn";
    public override WritingDirection WritingDirection => WritingDirection.LeftToRight;
    public override NumberFormat NumberFormat => NumberFormat.European;  // Portugal uses 1.234,56 (Brazil varies)
    public override QuotationMarks QuotationMarks => new()
    {
        PrimaryOpen = "\u00AB", PrimaryClose = "\u00BB",
        SecondaryOpen = "\u201C", SecondaryClose = "\u201D"
    };

    public override Regex SentenceEndPattern { get; } = new(
        @"[.!?]+(?:\s|$)|[.!?]+(?=[""'\)\]}>])",
        RegexOptions.Compiled);

    public override Regex SectionMarkerPattern { get; } = new(
        @"^#{1,6}\s+.+$|" +                          // Markdown headers
        @"^\s*\d+\.\s+.+$|" +                         // Numbered sections
        @"^\s*[A-Za-z]\.\s+.+$|" +                    // Letter sections
        @"^\s*(?:Capítulo|Seção|Parte)\s+\d+",        // Named sections
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public override IReadOnlyList<string> Abbreviations { get; } = new[]
    {
        "Dr", "Dra", "Sr", "Sra", "Srta", "Prof",
        "etc", "ex", "pág", "págs", "núm", "tel",
        "V.Ex", "V.S"
    };

    public override IReadOnlyList<Abbreviation> CategorizedAbbreviations { get; } = new Abbreviation[]
    {
        new("Dr", AbbreviationType.Prepositive),
        new("Dra", AbbreviationType.Prepositive),
        new("Sr", AbbreviationType.Prepositive),
        new("Sra", AbbreviationType.Prepositive),
        new("Srta", AbbreviationType.Prepositive),
        new("Prof", AbbreviationType.Prepositive),
        new("etc", AbbreviationType.General),
        new("ex", AbbreviationType.General),
        new("pág", AbbreviationType.General),
        new("núm", AbbreviationType.General)
    };
}

/// <summary>
/// Russian language profile
/// </summary>
public sealed class RussianLanguageProfile : LanguageProfileBase
{
    public static readonly RussianLanguageProfile Instance = new();

    public override string LanguageCode => "ru";
    public override string LanguageName => "Russian";
    public override string ScriptCode => "Cyrl";  // Cyrillic
    public override WritingDirection WritingDirection => WritingDirection.LeftToRight;
    public override NumberFormat NumberFormat => NumberFormat.SpaceSeparated;  // Russia uses 1 234,56
    public override QuotationMarks QuotationMarks => new()
    {
        PrimaryOpen = "\u00AB", PrimaryClose = "\u00BB",
        SecondaryOpen = "\u201E", SecondaryClose = "\u201C"
    };

    public override Regex SentenceEndPattern { get; } = new(
        @"[.!?]+(?:\s|$)|[.!?]+(?=[""'\)\]}>«»])",
        RegexOptions.Compiled);

    public override Regex SectionMarkerPattern { get; } = new(
        @"^#{1,6}\s+.+$|" +                          // Markdown headers
        @"^\s*\d+\.\s+.+$|" +                         // Numbered sections
        @"^\s*[А-Яа-я]\.\s+.+$|" +                    // Cyrillic letter sections
        @"^\s*(?:Глава|Раздел|Часть)\s+\d+",          // Named sections
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public override IReadOnlyList<string> Abbreviations { get; } = new[]
    {
        "г", "гг", "др", "проф", "т.д", "т.е", "т.п",
        "и т.д", "и т.п", "и др", "см", "ср", "напр"
    };

    public override IReadOnlyList<Abbreviation> CategorizedAbbreviations { get; } = new Abbreviation[]
    {
        new("г", AbbreviationType.Postpositive),      // year (after dates)
        new("гг", AbbreviationType.Postpositive),     // years
        new("др", AbbreviationType.General),
        new("проф", AbbreviationType.Prepositive),
        new("т.д", AbbreviationType.General),
        new("т.е", AbbreviationType.General),
        new("т.п", AbbreviationType.General),
        new("см", AbbreviationType.General),
        new("ср", AbbreviationType.General),
        new("напр", AbbreviationType.General)
    };
}
