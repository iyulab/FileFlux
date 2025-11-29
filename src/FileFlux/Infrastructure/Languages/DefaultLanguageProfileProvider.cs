using System.Text.RegularExpressions;
using FileFlux.Core;

namespace FileFlux.Infrastructure.Languages;

/// <summary>
/// Default implementation of ILanguageProfileProvider with auto-detection support.
/// Supports 10 major languages with Unicode script-based detection.
/// </summary>
public sealed class DefaultLanguageProfileProvider : ILanguageProfileProvider
{
    private readonly Dictionary<string, ILanguageProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public ILanguageProfile DefaultProfile => EnglishLanguageProfile.Instance;

    public IReadOnlyList<string> SupportedLanguages => _profiles.Keys.ToList().AsReadOnly();

    public DefaultLanguageProfileProvider()
    {
        // Register all built-in language profiles
        RegisterProfile(EnglishLanguageProfile.Instance);
        RegisterProfile(KoreanLanguageProfile.Instance);
        RegisterProfile(ChineseLanguageProfile.Instance);
        RegisterProfile(JapaneseLanguageProfile.Instance);
        RegisterProfile(SpanishLanguageProfile.Instance);
        RegisterProfile(FrenchLanguageProfile.Instance);
        RegisterProfile(GermanLanguageProfile.Instance);
        RegisterProfile(ArabicLanguageProfile.Instance);
        RegisterProfile(HindiLanguageProfile.Instance);
        RegisterProfile(PortugueseLanguageProfile.Instance);
        RegisterProfile(RussianLanguageProfile.Instance);
    }

    public ILanguageProfile GetProfile(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return DefaultProfile;

        // Try exact match first
        if (_profiles.TryGetValue(languageCode, out var profile))
            return profile;

        // Try base language code (e.g., "zh-CN" -> "zh")
        var dashIndex = languageCode.IndexOf('-');
        if (dashIndex > 0)
        {
            var baseCode = languageCode[..dashIndex];
            if (_profiles.TryGetValue(baseCode, out profile))
                return profile;
        }

        return DefaultProfile;
    }

    public ILanguageProfile DetectAndGetProfile(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return DefaultProfile;

        // Sample text for detection (first 1000 chars for efficiency)
        var sample = text.Length > 1000 ? text[..1000] : text;

        // Count character types using Unicode ranges
        var stats = AnalyzeTextScript(sample);

        // Determine primary language based on script analysis
        var detectedCode = DetermineLanguageFromStats(stats);
        return GetProfile(detectedCode);
    }

    public bool IsSupported(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return false;

        if (_profiles.ContainsKey(languageCode))
            return true;

        // Check base language code
        var dashIndex = languageCode.IndexOf('-');
        if (dashIndex > 0)
        {
            return _profiles.ContainsKey(languageCode[..dashIndex]);
        }

        return false;
    }

    public void RegisterProfile(ILanguageProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profiles[profile.LanguageCode] = profile;
    }

    #region Script Detection

    private static ScriptStats AnalyzeTextScript(string text)
    {
        var stats = new ScriptStats();

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
                continue;

            stats.TotalChars++;

            // Korean (Hangul)
            if (c is >= '\uAC00' and <= '\uD7AF' or >= '\u1100' and <= '\u11FF' or >= '\u3130' and <= '\u318F')
            {
                stats.KoreanChars++;
            }
            // Chinese (CJK Unified Ideographs)
            else if (c is >= '\u4E00' and <= '\u9FFF' or >= '\u3400' and <= '\u4DBF')
            {
                stats.ChineseChars++;
            }
            // Japanese (Hiragana, Katakana)
            else if (c is >= '\u3040' and <= '\u309F' or >= '\u30A0' and <= '\u30FF')
            {
                stats.JapaneseChars++;
            }
            // Arabic
            else if (c is >= '\u0600' and <= '\u06FF' or >= '\u0750' and <= '\u077F')
            {
                stats.ArabicChars++;
            }
            // Devanagari (Hindi)
            else if (c is >= '\u0900' and <= '\u097F')
            {
                stats.HindiChars++;
            }
            // Cyrillic (Russian)
            else if (c is >= '\u0400' and <= '\u04FF')
            {
                stats.CyrillicChars++;
            }
            // Latin (covers English, Spanish, French, German, Portuguese)
            else if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '\u00C0' and <= '\u00FF')
            {
                stats.LatinChars++;

                // Track extended Latin for non-English European languages
                if (c is >= '\u00C0' and <= '\u00FF')
                {
                    stats.ExtendedLatinChars++;
                }
            }
        }

        return stats;
    }

    private static string DetermineLanguageFromStats(ScriptStats stats)
    {
        if (stats.TotalChars == 0)
            return "en";

        // Calculate percentages
        var koreanRatio = (double)stats.KoreanChars / stats.TotalChars;
        var chineseRatio = (double)stats.ChineseChars / stats.TotalChars;
        var japaneseRatio = (double)stats.JapaneseChars / stats.TotalChars;
        var arabicRatio = (double)stats.ArabicChars / stats.TotalChars;
        var hindiRatio = (double)stats.HindiChars / stats.TotalChars;
        var cyrillicRatio = (double)stats.CyrillicChars / stats.TotalChars;

        // Non-Latin scripts take priority (they're more distinctive)
        if (koreanRatio > 0.3)
            return "ko";

        if (japaneseRatio > 0.1)
            return "ja";

        // Japanese text often contains Chinese characters, so check Japanese-specific chars first
        if (chineseRatio > 0.3)
            return "zh";

        if (arabicRatio > 0.3)
            return "ar";

        if (hindiRatio > 0.3)
            return "hi";

        if (cyrillicRatio > 0.3)
            return "ru";

        // For Latin-script languages, we'd need more sophisticated detection
        // For now, default to English if predominantly Latin
        return "en";
    }

    private struct ScriptStats
    {
        public int TotalChars;
        public int KoreanChars;
        public int ChineseChars;
        public int JapaneseChars;
        public int ArabicChars;
        public int HindiChars;
        public int CyrillicChars;
        public int LatinChars;
        public int ExtendedLatinChars;
    }

    #endregion
}
