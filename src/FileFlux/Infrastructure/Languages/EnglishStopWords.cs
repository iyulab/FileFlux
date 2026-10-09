using System.Collections.Frozen;

namespace FileFlux.Infrastructure.Languages;

/// <summary>
/// English function words that carry no topic: the one list that rule-based keyword extraction and the chunk quality
/// engine's «meaningful word» count both skip. Case-insensitive.
/// </summary>
internal static class EnglishStopWords
{
    private static readonly FrozenSet<string> s_words = new[]
    {
        "a", "an", "the", "and", "or", "but", "nor", "so", "yet", "if", "then", "than",
        "in", "on", "at", "to", "for", "of", "with", "by", "from", "into", "onto", "over", "under", "about", "after",
        "before", "between", "through", "during", "without", "within", "upon", "as",
        "is", "are", "was", "were", "be", "been", "being", "am",
        "have", "has", "had", "having", "do", "does", "did", "done",
        "will", "would", "shall", "should", "can", "could", "may", "might", "must",
        "this", "that", "these", "those", "which", "what", "who", "whom", "whose", "when", "where", "while", "why", "how",
        "it", "its", "they", "them", "their", "there", "here", "we", "our", "you", "your", "he", "she", "his", "her",
        "not", "no", "all", "any", "each", "some", "such", "also", "only", "more", "most", "other", "very", "just",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool Contains(string word) => s_words.Contains(word);
}
