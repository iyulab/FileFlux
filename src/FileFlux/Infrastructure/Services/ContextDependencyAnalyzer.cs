using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Services;

/// <summary>
/// Analyzes chunk content to determine context dependency score
/// Higher scores indicate the chunk relies more on surrounding context
/// </summary>
public static class ContextDependencyAnalyzer
{
    // Korean pronouns and demonstratives
    private static readonly string[] KoreanPronouns = { "그것", "이것", "저것", "그", "이", "저", "그녀", "그들", "여기", "거기", "저기" };
    private static readonly string[] KoreanReferences = { "위의", "아래의", "앞서", "앞에서", "다음", "이전", "상기", "하기", "전술한", "후술한", "언급한" };

    // English pronouns and demonstratives
    private static readonly string[] EnglishPronouns = { "it", "this", "that", "these", "those", "he", "she", "they", "them", "here", "there" };
    private static readonly string[] EnglishReferences = { "above", "below", "previous", "following", "aforementioned", "said", "former", "latter" };

    // Patterns for incomplete sentences
    private static readonly Regex IncompleteStartPattern = new(@"^[a-z가-힣]", RegexOptions.Compiled);
    private static readonly Regex ContinuationPattern = new(@"^(and|or|but|however|therefore|thus|hence|also|또한|그리고|하지만|그러나|따라서|그래서)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Calculate context dependency score for a chunk
    /// </summary>
    /// <param name="content">Chunk content</param>
    /// <param name="language">Detected language (optional)</param>
    /// <returns>Score between 0.0 (independent) and 1.0 (highly dependent)</returns>
    public static double Calculate(string content, string? language = null)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0.0;

        var scores = new List<double>();

        // 1. Pronoun ratio (0.0 - 1.0)
        var pronounScore = CalculatePronounScore(content, language);
        scores.Add(pronounScore * 0.3); // 30% weight

        // 2. Reference expression ratio (0.0 - 1.0)
        var referenceScore = CalculateReferenceScore(content, language);
        scores.Add(referenceScore * 0.25); // 25% weight

        // 3. Incomplete sentence indicators (0.0 - 1.0)
        var incompleteScore = CalculateIncompleteScore(content);
        scores.Add(incompleteScore * 0.25); // 25% weight

        // 4. Proper noun density (inverse - fewer proper nouns = higher dependency)
        var properNounScore = 1.0 - CalculateProperNounDensity(content);
        scores.Add(properNounScore * 0.2); // 20% weight

        return Math.Min(1.0, scores.Sum());
    }

    private static double CalculatePronounScore(string content, string? language)
    {
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return 0.0;

        var pronouns = language?.StartsWith("ko") == true ? KoreanPronouns : EnglishPronouns;
        var count = words.Count(w => pronouns.Any(p =>
            w.Equals(p, StringComparison.OrdinalIgnoreCase) ||
            w.StartsWith(p, StringComparison.OrdinalIgnoreCase)));

        // Normalize: more than 10% pronouns is high
        return Math.Min(1.0, count / (double)words.Length * 10);
    }

    private static double CalculateReferenceScore(string content, string? language)
    {
        var contentLower = content.ToLowerInvariant();
        var references = language?.StartsWith("ko") == true ? KoreanReferences : EnglishReferences;

        var count = references.Count(r => contentLower.Contains(r));
        // Normalize: 3+ references is high
        return Math.Min(1.0, count / 3.0);
    }

    private static double CalculateIncompleteScore(string content)
    {
        var score = 0.0;

        // Check if starts with lowercase (mid-sentence)
        if (IncompleteStartPattern.IsMatch(content.TrimStart()))
            score += 0.3;

        // Check if starts with continuation word
        var firstWord = content.TrimStart().Split(' ').FirstOrDefault() ?? "";
        if (ContinuationPattern.IsMatch(firstWord))
            score += 0.4;

        // Check if ends without proper punctuation
        var trimmed = content.TrimEnd();
        if (trimmed.Length > 0 && !".!?。」』".Contains(trimmed[^1]))
            score += 0.3;

        return Math.Min(1.0, score);
    }

    private static double CalculateProperNounDensity(string content)
    {
        // Simple heuristic: count capitalized words (for English)
        // and words with specific patterns for Korean
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return 0.0;

        var properNouns = words.Count(w =>
            w.Length > 1 &&
            char.IsUpper(w[0]) &&
            !IsCommonSentenceStart(w));

        return Math.Min(1.0, properNouns / (double)words.Length * 5);
    }

    private static bool IsCommonSentenceStart(string word)
    {
        var common = new[] { "The", "A", "An", "It", "This", "That", "I", "We", "You", "He", "She", "They" };
        return common.Contains(word);
    }
}
