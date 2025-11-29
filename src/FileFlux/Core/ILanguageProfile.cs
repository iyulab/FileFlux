using System.Text.RegularExpressions;

namespace FileFlux.Core;

/// <summary>
/// Writing direction for text rendering and processing
/// </summary>
public enum WritingDirection
{
    /// <summary>Left-to-Right (e.g., English, Korean, Chinese)</summary>
    LeftToRight,
    /// <summary>Right-to-Left (e.g., Arabic, Hebrew)</summary>
    RightToLeft,
    /// <summary>Top-to-Bottom, Right-to-Left columns (e.g., traditional Japanese/Chinese)</summary>
    TopToBottom
}

/// <summary>
/// Number formatting conventions for a language/region
/// </summary>
public readonly struct NumberFormat
{
    /// <summary>Character used as decimal separator (e.g., '.' for EN, ',' for DE)</summary>
    public char DecimalSeparator { get; init; }

    /// <summary>Character used as thousands separator (e.g., ',' for EN, '.' for DE)</summary>
    public char ThousandsSeparator { get; init; }

    /// <summary>Standard format (1,234.56)</summary>
    public static NumberFormat Standard => new() { DecimalSeparator = '.', ThousandsSeparator = ',' };

    /// <summary>European format (1.234,56)</summary>
    public static NumberFormat European => new() { DecimalSeparator = ',', ThousandsSeparator = '.' };

    /// <summary>Space-separated format (1 234,56)</summary>
    public static NumberFormat SpaceSeparated => new() { DecimalSeparator = ',', ThousandsSeparator = ' ' };

    /// <summary>No grouping format (1234.56)</summary>
    public static NumberFormat NoGrouping => new() { DecimalSeparator = '.', ThousandsSeparator = '\0' };
}

/// <summary>
/// Quotation mark conventions for a language
/// </summary>
public readonly struct QuotationMarks
{
    /// <summary>Primary opening quote (e.g., " for EN, „ for DE, « for FR)</summary>
    public string PrimaryOpen { get; init; }

    /// <summary>Primary closing quote (e.g., " for EN, " for DE, » for FR)</summary>
    public string PrimaryClose { get; init; }

    /// <summary>Secondary/nested opening quote (e.g., ' for EN, ‚ for DE)</summary>
    public string SecondaryOpen { get; init; }

    /// <summary>Secondary/nested closing quote (e.g., ' for EN, ' for DE)</summary>
    public string SecondaryClose { get; init; }

    /// <summary>English style ("...")</summary>
    public static QuotationMarks English => new() { PrimaryOpen = "\u201C", PrimaryClose = "\u201D", SecondaryOpen = "\u2018", SecondaryClose = "\u2019" };

    /// <summary>German style (low-9 opening quotes)</summary>
    public static QuotationMarks German => new() { PrimaryOpen = "\u201E", PrimaryClose = "\u201C", SecondaryOpen = "\u201A", SecondaryClose = "\u2018" };

    /// <summary>French style (guillemets)</summary>
    public static QuotationMarks French => new() { PrimaryOpen = "\u00AB", PrimaryClose = "\u00BB", SecondaryOpen = "\u2039", SecondaryClose = "\u203A" };

    /// <summary>Japanese style (corner brackets)</summary>
    public static QuotationMarks Japanese => new() { PrimaryOpen = "\u300C", PrimaryClose = "\u300D", SecondaryOpen = "\u300E", SecondaryClose = "\u300F" };

    /// <summary>Korean style (same as Japanese)</summary>
    public static QuotationMarks Korean => Japanese;

    /// <summary>Chinese style (same as Japanese)</summary>
    public static QuotationMarks Chinese => Japanese;
}

/// <summary>
/// Abbreviation category for context-aware handling
/// </summary>
public enum AbbreviationType
{
    /// <summary>General abbreviation (etc, vs, ie)</summary>
    General,
    /// <summary>Prepositive abbreviation before names (Dr, Mr, Prof)</summary>
    Prepositive,
    /// <summary>Postpositive abbreviation after names (Jr, Sr, Inc)</summary>
    Postpositive
}

/// <summary>
/// Represents a categorized abbreviation
/// </summary>
public readonly struct Abbreviation
{
    public string Text { get; init; }
    public AbbreviationType Type { get; init; }

    public Abbreviation(string text, AbbreviationType type = AbbreviationType.General)
    {
        Text = text;
        Type = type;
    }

    public static implicit operator Abbreviation(string text) => new(text);
}

/// <summary>
/// Language-specific text segmentation profile for sentence boundary detection
/// and document structure recognition.
/// </summary>
public interface ILanguageProfile
{
    /// <summary>
    /// ISO 639-1 language code (e.g., "en", "ko", "zh")
    /// </summary>
    string LanguageCode { get; }

    /// <summary>
    /// Human-readable language name
    /// </summary>
    string LanguageName { get; }

    /// <summary>
    /// ISO 15924 script code (e.g., "Latn", "Hang", "Hans", "Arab")
    /// </summary>
    string ScriptCode { get; }

    /// <summary>
    /// Writing direction for this language
    /// </summary>
    WritingDirection WritingDirection { get; }

    /// <summary>
    /// Number formatting conventions
    /// </summary>
    NumberFormat NumberFormat { get; }

    /// <summary>
    /// Quotation mark conventions
    /// </summary>
    QuotationMarks QuotationMarks { get; }

    /// <summary>
    /// Regex pattern for detecting sentence endings
    /// </summary>
    Regex SentenceEndPattern { get; }

    /// <summary>
    /// Regex pattern for detecting section/heading markers
    /// </summary>
    Regex SectionMarkerPattern { get; }

    /// <summary>
    /// Common abbreviations that should not trigger sentence breaks
    /// </summary>
    IReadOnlyList<string> Abbreviations { get; }

    /// <summary>
    /// Categorized abbreviations with type information for context-aware handling
    /// </summary>
    IReadOnlyList<Abbreviation> CategorizedAbbreviations { get; }

    /// <summary>
    /// Prefixes that should not be separated (e.g., "Dr.", "Mr.")
    /// </summary>
    IReadOnlyList<string> NonBreakingPrefixes { get; }

    /// <summary>
    /// Check if the text ends with a complete sentence
    /// </summary>
    /// <param name="text">Text to check</param>
    /// <returns>True if text ends with a complete sentence</returns>
    bool EndsWithCompleteSentence(string text);

    /// <summary>
    /// Check if the line is a section marker/header
    /// </summary>
    /// <param name="line">Line to check</param>
    /// <returns>True if line is a section marker</returns>
    bool IsSectionMarker(string line);

    /// <summary>
    /// Find sentence boundaries in text
    /// </summary>
    /// <param name="text">Text to analyze</param>
    /// <returns>List of character positions where sentences end</returns>
    IReadOnlyList<int> FindSentenceBoundaries(string text);
}

/// <summary>
/// Base implementation of ILanguageProfile with common functionality
/// </summary>
public abstract class LanguageProfileBase : ILanguageProfile
{
    public abstract string LanguageCode { get; }
    public abstract string LanguageName { get; }
    public abstract Regex SentenceEndPattern { get; }
    public abstract Regex SectionMarkerPattern { get; }

    /// <summary>
    /// ISO 15924 script code. Default is "Latn" (Latin).
    /// Override in derived classes for non-Latin scripts.
    /// </summary>
    public virtual string ScriptCode => "Latn";

    /// <summary>
    /// Writing direction. Default is LeftToRight.
    /// Override for RTL languages like Arabic, Hebrew.
    /// </summary>
    public virtual WritingDirection WritingDirection => WritingDirection.LeftToRight;

    /// <summary>
    /// Number format. Default is Standard (1,234.56).
    /// Override for European or other formats.
    /// </summary>
    public virtual NumberFormat NumberFormat => NumberFormat.Standard;

    /// <summary>
    /// Quotation marks. Default is English style.
    /// Override for language-specific quotation conventions.
    /// </summary>
    public virtual QuotationMarks QuotationMarks => QuotationMarks.English;

    public virtual IReadOnlyList<string> Abbreviations => Array.Empty<string>();
    public virtual IReadOnlyList<Abbreviation> CategorizedAbbreviations => Array.Empty<Abbreviation>();
    public virtual IReadOnlyList<string> NonBreakingPrefixes => Array.Empty<string>();

    public virtual bool EndsWithCompleteSentence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.TrimEnd();
        if (trimmed.Length == 0)
            return false;

        // Check last portion of text against sentence end pattern
        var checkLength = Math.Min(50, trimmed.Length);
        var tail = trimmed[^checkLength..];
        return SentenceEndPattern.IsMatch(tail);
    }

    public virtual bool IsSectionMarker(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        return SectionMarkerPattern.IsMatch(line);
    }

    public virtual IReadOnlyList<int> FindSentenceBoundaries(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<int>();

        var boundaries = new List<int>();
        var matches = SentenceEndPattern.Matches(text);

        foreach (Match match in matches)
        {
            var endPosition = match.Index + match.Length;

            // Skip if this is an abbreviation
            if (!IsAbbreviation(text, match.Index))
            {
                boundaries.Add(endPosition);
            }
        }

        return boundaries;
    }

    /// <summary>
    /// Check if the match position is part of an abbreviation
    /// </summary>
    protected virtual bool IsAbbreviation(string text, int position)
    {
        if (Abbreviations.Count == 0)
            return false;

        // Look back to find the word before the punctuation
        var start = position;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
            start--;

        var word = text[start..position].Trim();
        return Abbreviations.Contains(word, StringComparer.OrdinalIgnoreCase);
    }
}
