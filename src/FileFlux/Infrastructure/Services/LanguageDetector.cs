using NTextCat;

namespace FileFlux.Infrastructure.Services;

/// <summary>
/// Language detection service using NTextCat
/// Supports document-level and chunk-level detection for multilingual documents
/// </summary>
public static class LanguageDetector
{
    private static readonly Lazy<RankedLanguageIdentifier?> _identifier = new(() =>
    {
        try
        {
            var factory = new RankedLanguageIdentifierFactory();

            // Try to load from embedded resource first
            var assembly = typeof(RankedLanguageIdentifierFactory).Assembly;
            var resourceNames = assembly.GetManifestResourceNames();
            var profileResource = resourceNames.FirstOrDefault(n => n.Contains("Core14") || n.Contains("profile"));

            if (profileResource != null)
            {
                using var stream = assembly.GetManifestResourceStream(profileResource);
                if (stream != null)
                {
                    return factory.Load(stream);
                }
            }

            // Fallback: try to load from file
            var assemblyPath = Path.GetDirectoryName(assembly.Location);
            if (assemblyPath != null)
            {
                var profilePath = Path.Combine(assemblyPath, "Core14.profile.xml");
                if (File.Exists(profilePath))
                {
                    return factory.Load(profilePath);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    });

    /// <summary>
    /// Detect language of the given text
    /// </summary>
    /// <param name="text">Text to analyze</param>
    /// <returns>Tuple of (language code, confidence score 0-1)</returns>
    public static (string Language, double Confidence) Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ("unknown", 0.0);

        // Try CJK detection first (NTextCat Core14 doesn't support Korean/Japanese/Chinese)
        var cjkResult = DetectCJKLanguage(text);
        if (cjkResult.Language != "unknown")
            return cjkResult;

        try
        {
            if (_identifier.Value == null)
                return ("unknown", 0.0);

            // Get sample text for detection (first 10000 chars is usually enough)
            var sampleText = text.Length > 10000 ? text.Substring(0, 10000) : text;

            var languages = _identifier.Value.Identify(sampleText);
            var topLanguage = languages.FirstOrDefault();

            if (topLanguage == null)
                return ("unknown", 0.0);

            // Calculate confidence based on distance from second best
            var confidence = CalculateConfidence(languages);
            var languageCode = NormalizeLanguageCode(topLanguage.Item1.Iso639_2T);

            return (languageCode, confidence);
        }
        catch
        {
            return ("unknown", 0.0);
        }
    }

    /// <summary>
    /// Detect CJK (Chinese, Japanese, Korean) languages using Unicode ranges.
    /// NTextCat Core14 profile doesn't include these languages.
    /// </summary>
    private static (string Language, double Confidence) DetectCJKLanguage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ("unknown", 0.0);

        var sampleText = text.Length > 10000 ? text.Substring(0, 10000) : text;

        int koreanCount = 0;
        int japaneseCount = 0;
        int chineseCount = 0;
        int totalChars = 0;

        foreach (char c in sampleText)
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsDigit(c))
                continue;

            totalChars++;

            // Korean Hangul: Syllables (U+AC00-U+D7AF), Jamo (U+1100-U+11FF), Compatibility (U+3130-U+318F)
            if ((c >= 0xAC00 && c <= 0xD7AF) || (c >= 0x1100 && c <= 0x11FF) ||
                (c >= 0x3130 && c <= 0x318F))
            {
                koreanCount++;
            }
            // Japanese Hiragana (U+3040-U+309F) and Katakana (U+30A0-U+30FF)
            else if ((c >= 0x3040 && c <= 0x309F) || (c >= 0x30A0 && c <= 0x30FF))
            {
                japaneseCount++;
            }
            // CJK Unified Ideographs (U+4E00-U+9FFF) - shared by Chinese/Japanese/Korean
            else if (c >= 0x4E00 && c <= 0x9FFF)
            {
                chineseCount++;
            }
        }

        if (totalChars == 0)
            return ("unknown", 0.0);

        // Calculate percentages
        double koreanRatio = (double)koreanCount / totalChars;
        double japaneseRatio = (double)japaneseCount / totalChars;
        double cjkIdeographRatio = (double)chineseCount / totalChars;

        // Determine language based on character distribution
        // Korean: Has Hangul characters
        if (koreanRatio > 0.1)
        {
            var confidence = Math.Min(0.95, 0.6 + koreanRatio * 0.4);
            return ("ko", Math.Round(confidence, 4));
        }

        // Japanese: Has Hiragana/Katakana (distinguishes from Chinese)
        if (japaneseRatio > 0.05)
        {
            var confidence = Math.Min(0.95, 0.6 + (japaneseRatio + cjkIdeographRatio * 0.3) * 0.4);
            return ("ja", Math.Round(confidence, 4));
        }

        // Chinese: Has CJK Ideographs but no Japanese kana or Korean Hangul
        if (cjkIdeographRatio > 0.1)
        {
            var confidence = Math.Min(0.95, 0.6 + cjkIdeographRatio * 0.4);
            return ("zh", Math.Round(confidence, 4));
        }

        return ("unknown", 0.0);
    }

    /// <summary>
    /// Detect language with detailed results for multilingual documents
    /// </summary>
    /// <param name="text">Text to analyze</param>
    /// <param name="topN">Number of top languages to return</param>
    /// <returns>List of (language code, confidence) tuples</returns>
    public static List<(string Language, double Confidence)> DetectMultiple(string text, int topN = 3)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<(string, double)> { ("unknown", 0.0) };

        try
        {
            if (_identifier.Value == null)
                return new List<(string, double)> { ("unknown", 0.0) };

            var sampleText = text.Length > 10000 ? text.Substring(0, 10000) : text;
            var languages = _identifier.Value.Identify(sampleText).Take(topN).ToList();

            if (languages.Count == 0)
                return new List<(string, double)> { ("unknown", 0.0) };

            var results = new List<(string Language, double Confidence)>();
            var totalScore = languages.Sum(l => 1.0 / (l.Item2 + 0.0001));

            foreach (var lang in languages)
            {
                var normalizedScore = (1.0 / (lang.Item2 + 0.0001)) / totalScore;
                var languageCode = NormalizeLanguageCode(lang.Item1.Iso639_2T);
                results.Add((languageCode, Math.Round(normalizedScore, 4)));
            }

            return results;
        }
        catch
        {
            return new List<(string, double)> { ("unknown", 0.0) };
        }
    }

    /// <summary>
    /// Quick language check - returns true if text is likely in the specified language
    /// </summary>
    public static bool IsLanguage(string text, string languageCode, double minConfidence = 0.6)
    {
        var (detected, confidence) = Detect(text);
        return detected.Equals(languageCode, StringComparison.OrdinalIgnoreCase) && confidence >= minConfidence;
    }

    /// <summary>
    /// Calculate confidence score based on language identification results
    /// </summary>
    private static double CalculateConfidence(IEnumerable<Tuple<LanguageInfo, double>> languages)
    {
        var langList = languages.Take(2).ToList();

        if (langList.Count == 0)
            return 0.0;

        if (langList.Count == 1)
            return 0.95; // Only one language detected = high confidence

        // Calculate relative confidence based on score difference
        var firstScore = langList[0].Item2;
        var secondScore = langList[1].Item2;

        // Lower score is better in NTextCat
        // Large difference = high confidence
        var scoreDifference = secondScore - firstScore;
        var confidence = Math.Min(0.99, 0.5 + (scoreDifference / (firstScore + 0.0001)) * 0.5);

        return Math.Round(Math.Max(0.1, confidence), 4);
    }

    /// <summary>
    /// Normalize language codes to ISO 639-1 (2-letter) format
    /// </summary>
    private static string NormalizeLanguageCode(string iso639_2)
    {
        // Map ISO 639-2T to common 2-letter codes
        return iso639_2?.ToLowerInvariant() switch
        {
            "eng" => "en",
            "kor" => "ko",
            "jpn" => "ja",
            "zho" => "zh",
            "cmn" => "zh",
            "deu" => "de",
            "fra" => "fr",
            "spa" => "es",
            "ita" => "it",
            "por" => "pt",
            "rus" => "ru",
            "ara" => "ar",
            "hin" => "hi",
            "vie" => "vi",
            "tha" => "th",
            "nld" => "nl",
            "pol" => "pl",
            "tur" => "tr",
            "ukr" => "uk",
            "ces" => "cs",
            "ell" => "el",
            "heb" => "he",
            "ind" => "id",
            "msa" => "ms",
            "swe" => "sv",
            "dan" => "da",
            "fin" => "fi",
            "nor" => "no",
            "hun" => "hu",
            "ron" => "ro",
            "bul" => "bg",
            "cat" => "ca",
            "hrv" => "hr",
            "slk" => "sk",
            "slv" => "sl",
            "srp" => "sr",
            "lit" => "lt",
            "lav" => "lv",
            "est" => "et",
            _ => iso639_2?.ToLowerInvariant() ?? "unknown"
        };
    }

}
