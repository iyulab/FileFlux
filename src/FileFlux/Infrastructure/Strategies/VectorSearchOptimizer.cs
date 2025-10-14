using FileFlux.Domain;
using System.Text;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// Phase 11 T11-001: Vector Search Optimization
/// Optimizes chunks for vector embedding and retrieval
/// </summary>
public class VectorSearchOptimizer
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex PunctuationRegex = new(@"[^\w\s]", RegexOptions.Compiled);
    private static readonly Regex NumberRegex = new(@"\b\d+\b", RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(@"https?://[^\s]+", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled);
    
    private readonly HashSet<string> _stopWords = new()
    {
        "the", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with",
        "by", "from", "as", "is", "was", "are", "were", "been", "being", "have",
        "has", "had", "do", "does", "did", "will", "would", "could", "should",
        "may", "might", "must", "can", "shall", "a", "an", "this", "that", "these",
        "those", "i", "you", "he", "she", "it", "we", "they", "them", "their"
    };

    /// <summary>
    /// Optimize chunk for vector search
    /// </summary>
    public OptimizedChunk OptimizeForVectorSearch(DocumentChunk chunk, VectorSearchOptions options)
    {
        var optimized = new OptimizedChunk
        {
            OriginalChunk = chunk,
            OptimizationTimestamp = DateTime.UtcNow
        };

        // 1. Semantic density optimization
        var densityResult = OptimizeSemanticDensity(chunk.Content, options);
        optimized.OptimizedContent = densityResult.OptimizedText;
        optimized.SemanticDensity = densityResult.Density;

        // 2. Embedding-friendly normalization
        optimized.NormalizedContent = NormalizeForEmbedding(optimized.OptimizedContent, options);

        // 3. Duplicate and noise removal
        optimized.CleanedContent = RemoveDuplicatesAndNoise(optimized.NormalizedContent);

        // 4. Context window optimization
        optimized.WindowOptimizedContent = OptimizeContextWindow(
            optimized.CleanedContent, 
            options.MaxTokensPerChunk ?? 512
        );

        // 5. Calculate optimization metrics
        optimized.OptimizationMetrics = CalculateOptimizationMetrics(
            chunk.Content,
            optimized.WindowOptimizedContent
        );

        // 6. Generate embedding hints
        optimized.EmbeddingHints = GenerateEmbeddingHints(optimized.WindowOptimizedContent);

        return optimized;
    }

    /// <summary>
    /// Optimize semantic density
    /// </summary>
    private SemanticDensityResult OptimizeSemanticDensity(string text, VectorSearchOptions options)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var contentWords = words.Where(w => !IsStopWord(w.ToLowerInvariant())).ToList();
        
        var density = (double)contentWords.Count / Math.Max(1, words.Length);
        
        // Adjust chunk size based on semantic density
        var optimizedText = text;
        if (density < options.MinSemanticDensity)
        {
            // Low density: remove filler words more aggressively
            optimizedText = RemoveFillerWords(text);
        }
        else if (density > options.MaxSemanticDensity)
        {
            // High density: add context for clarity
            optimizedText = AddContextualMarkers(text);
        }

        return new SemanticDensityResult
        {
            OptimizedText = optimizedText,
            Density = density,
            ContentWordCount = contentWords.Count,
            TotalWordCount = words.Length
        };
    }

    /// <summary>
    /// Normalize text for embedding
    /// </summary>
    private string NormalizeForEmbedding(string text, VectorSearchOptions options)
    {
        var normalized = text;

        // 1. Lowercase if specified
        if (options.LowercaseNormalization)
        {
            normalized = normalized.ToLowerInvariant();
        }

        // 2. Normalize whitespace
        normalized = WhitespaceRegex.Replace(normalized, " ");

        // 3. Handle URLs and emails
        if (options.ReplaceUrls)
        {
            normalized = UrlRegex.Replace(normalized, "[URL]");
            normalized = EmailRegex.Replace(normalized, "[EMAIL]");
        }

        // 4. Normalize numbers if needed
        if (options.NormalizeNumbers)
        {
            normalized = NormalizeNumbers(normalized);
        }

        // 5. Remove or normalize special characters
        if (options.RemoveSpecialCharacters)
        {
            normalized = PunctuationRegex.Replace(normalized, " ");
        }

        // 6. Expand contractions
        normalized = ExpandContractions(normalized);

        // 7. Remove extra spaces
        normalized = WhitespaceRegex.Replace(normalized, " ").Trim();

        return normalized;
    }

    /// <summary>
    /// Remove duplicates and noise
    /// </summary>
    private string RemoveDuplicatesAndNoise(string text)
    {
        var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        var uniqueSentences = new HashSet<string>();
        var result = new StringBuilder();

        foreach (var sentence in sentences)
        {
            var cleaned = sentence.Trim();
            if (string.IsNullOrWhiteSpace(cleaned) || cleaned.Length < 10)
                continue;

            // Skip duplicate sentences
            var normalized = cleaned.ToLowerInvariant();
            if (uniqueSentences.Contains(normalized))
                continue;

            uniqueSentences.Add(normalized);
            result.Append(cleaned);
            
            // Add period if missing
            if (!cleaned.EndsWith('.') && !cleaned.EndsWith('!') && !cleaned.EndsWith('?'))
            {
                result.Append('.');
            }
            result.Append(' ');
        }

        return result.ToString().Trim();
    }

    /// <summary>
    /// Optimize for context window
    /// </summary>
    private string OptimizeContextWindow(string text, int maxTokens)
    {
        var estimatedTokens = EstimateTokenCount(text);
        
        if (estimatedTokens <= maxTokens)
            return text;

        // Truncate intelligently at sentence boundaries
        var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new StringBuilder();
        var currentTokens = 0;

        foreach (var sentence in sentences)
        {
            var sentenceTokens = EstimateTokenCount(sentence);
            
            if (currentTokens + sentenceTokens > maxTokens)
                break;

            result.Append(sentence.Trim());
            if (!sentence.EndsWith('.') && !sentence.EndsWith('!') && !sentence.EndsWith('?'))
            {
                result.Append('.');
            }
            result.Append(' ');
            
            currentTokens += sentenceTokens;
        }

        var optimized = result.ToString().Trim();
        
        // Add continuation marker if truncated
        if (optimized.Length < text.Length * 0.9)
        {
            optimized += " [...]";
        }

        return optimized;
    }

    /// <summary>
    /// Calculate optimization metrics
    /// </summary>
    private OptimizationMetrics CalculateOptimizationMetrics(string original, string optimized)
    {
        var originalTokens = EstimateTokenCount(original);
        var optimizedTokens = EstimateTokenCount(optimized);
        
        var originalWords = original.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var optimizedWords = optimized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        var compressionRatio = 1.0 - ((double)optimized.Length / Math.Max(1, original.Length));
        var tokenReduction = 1.0 - ((double)optimizedTokens / Math.Max(1, originalTokens));
        
        var semanticPreservation = CalculateSemanticPreservation(originalWords, optimizedWords);

        return new OptimizationMetrics
        {
            CompressionRatio = compressionRatio,
            TokenReduction = tokenReduction,
            SemanticPreservation = semanticPreservation,
            OriginalTokenCount = originalTokens,
            OptimizedTokenCount = optimizedTokens,
            QualityScore = (semanticPreservation * 0.6) + ((1 - compressionRatio) * 0.4)
        };
    }

    /// <summary>
    /// Generate embedding hints
    /// </summary>
    private EmbeddingHints GenerateEmbeddingHints(string text)
    {
        var hints = new EmbeddingHints();
        
        // Detect primary language
        hints.PrimaryLanguage = DetectLanguage(text);
        
        // Identify key entities
        hints.KeyEntities = ExtractKeyEntities(text);
        
        // Determine text type
        hints.TextType = DetermineTextType(text);
        
        // Suggest embedding model
        hints.RecommendedModel = SuggestEmbeddingModel(text, hints.TextType);
        
        // Calculate expected dimensions
        hints.ExpectedDimensions = hints.RecommendedModel switch
        {
            "text-embedding-3-small" => 1536,
            "text-embedding-3-large" => 3072,
            "text-embedding-ada-002" => 1536,
            _ => 768
        };

        // Identify special handling needs
        hints.RequiresSpecialHandling = DetectSpecialHandlingNeeds(text);

        return hints;
    }

    /// <summary>
    /// Remove filler words
    /// </summary>
    private string RemoveFillerWords(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var filtered = words.Where(w => !IsFillerWord(w.ToLowerInvariant()));
        return string.Join(" ", filtered);
    }

    /// <summary>
    /// Add contextual markers
    /// </summary>
    private string AddContextualMarkers(string text)
    {
        // Add section markers for dense technical text
        if (IsCodeBlock(text))
        {
            return $"[CODE SECTION] {text}";
        }
        
        if (IsDataTable(text))
        {
            return $"[DATA TABLE] {text}";
        }
        
        if (IsTechnicalContent(text))
        {
            return $"[TECHNICAL] {text}";
        }

        return text;
    }

    /// <summary>
    /// Normalize numbers
    /// </summary>
    private string NormalizeNumbers(string text)
    {
        return NumberRegex.Replace(text, match =>
        {
            if (int.TryParse(match.Value, out var number))
            {
                if (number < 10)
                    return match.Value; // Keep small numbers
                if (number < 100)
                    return "[NUM_TENS]";
                if (number < 1000)
                    return "[NUM_HUNDREDS]";
                if (number < 1000000)
                    return "[NUM_THOUSANDS]";
                return "[NUM_LARGE]";
            }
            return match.Value;
        });
    }

    /// <summary>
    /// Expand contractions
    /// </summary>
    private string ExpandContractions(string text)
    {
        var contractions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "don't", "do not" },
            { "won't", "will not" },
            { "can't", "cannot" },
            { "n't", " not" },
            { "'re", " are" },
            { "'ve", " have" },
            { "'ll", " will" },
            { "'d", " would" },
            { "'m", " am" },
            { "it's", "it is" },
            { "that's", "that is" },
            { "what's", "what is" },
            { "let's", "let us" }
        };

        var result = text;
        foreach (var contraction in contractions)
        {
            result = Regex.Replace(result, contraction.Key, contraction.Value, RegexOptions.IgnoreCase);
        }

        return result;
    }

    /// <summary>
    /// Calculate semantic preservation
    /// </summary>
    private double CalculateSemanticPreservation(string[] originalWords, string[] optimizedWords)
    {
        var originalContent = originalWords
            .Where(w => !IsStopWord(w.ToLowerInvariant()))
            .Select(w => w.ToLowerInvariant())
            .ToHashSet();
            
        var optimizedContent = optimizedWords
            .Where(w => !IsStopWord(w.ToLowerInvariant()))
            .Select(w => w.ToLowerInvariant())
            .ToHashSet();

        if (originalContent.Count == 0)
            return 1.0;

        var preserved = originalContent.Intersect(optimizedContent).Count();
        return (double)preserved / originalContent.Count;
    }

    /// <summary>
    /// Detect language
    /// </summary>
    private string DetectLanguage(string text)
    {
        // Simple heuristic - could be enhanced with actual language detection
        if (Regex.IsMatch(text, @"[\u4e00-\u9fff]"))
            return "zh";
        if (Regex.IsMatch(text, @"[\u3040-\u309f\u30a0-\u30ff]"))
            return "ja";
        if (Regex.IsMatch(text, @"[\uac00-\ud7af]"))
            return "ko";
        
        return "en";
    }

    /// <summary>
    /// Extract key entities
    /// </summary>
    private List<string> ExtractKeyEntities(string text)
    {
        var entities = new List<string>();
        
        // Extract capitalized words (potential proper nouns)
        var matches = Regex.Matches(text, @"\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*\b");
        foreach (Match match in matches)
        {
            if (match.Value.Length > 2 && !IsStopWord(match.Value.ToLowerInvariant()))
            {
                entities.Add(match.Value);
            }
        }

        // Extract technical terms
        var technicalTerms = Regex.Matches(text, @"\b[A-Z]{2,}\b");
        foreach (Match match in technicalTerms)
        {
            entities.Add(match.Value);
        }

        return entities.Distinct().Take(10).ToList();
    }

    /// <summary>
    /// Determine text type
    /// </summary>
    private string DetermineTextType(string text)
    {
        if (IsCodeBlock(text))
            return "code";
        if (IsDataTable(text))
            return "tabular";
        if (IsTechnicalContent(text))
            return "technical";
        if (IsConversational(text))
            return "conversational";
        
        return "general";
    }

    /// <summary>
    /// Suggest embedding model
    /// </summary>
    private string SuggestEmbeddingModel(string text, string textType)
    {
        return textType switch
        {
            "code" => "code-embedding-002",
            "technical" => "text-embedding-3-large",
            "conversational" => "text-embedding-3-small",
            _ => "text-embedding-3-small"
        };
    }

    /// <summary>
    /// Detect special handling needs
    /// </summary>
    private bool DetectSpecialHandlingNeeds(string text)
    {
        // Check for special content that needs careful handling
        return text.Contains("```") || 
               text.Contains("<code>") ||
               text.Contains("BEGIN") ||
               text.Contains("END") ||
               Regex.IsMatch(text, @"\b(SELECT|INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Check if word is stop word
    /// </summary>
    private bool IsStopWord(string word)
    {
        return _stopWords.Contains(word);
    }

    /// <summary>
    /// Check if word is filler word
    /// </summary>
    private bool IsFillerWord(string word)
    {
        var fillerWords = new HashSet<string>
        {
            "basically", "actually", "really", "very", "just", "quite",
            "rather", "somewhat", "indeed", "perhaps", "maybe", "probably",
            "definitely", "certainly", "obviously", "clearly"
        };
        
        return fillerWords.Contains(word) || IsStopWord(word);
    }

    /// <summary>
    /// Check if text is code block
    /// </summary>
    private bool IsCodeBlock(string text)
    {
        return text.Contains("function") || 
               text.Contains("class") || 
               text.Contains("return") ||
               text.Contains("{") && text.Contains("}") ||
               text.Contains("def ") ||
               text.Contains("import ");
    }

    /// <summary>
    /// Check if text is data table
    /// </summary>
    private bool IsDataTable(string text)
    {
        var lines = text.Split('\n');
        var pipeCount = lines.Count(l => l.Contains('|'));
        return pipeCount > 2 && lines.Any(l => l.Count(c => c == '|') >= 2);
    }

    /// <summary>
    /// Check if text is technical content
    /// </summary>
    private bool IsTechnicalContent(string text)
    {
        var technicalIndicators = new[]
        {
            "algorithm", "function", "method", "parameter", "variable",
            "database", "server", "client", "API", "framework",
            "implementation", "architecture", "protocol", "interface"
        };

        var lowerText = text.ToLowerInvariant();
        return technicalIndicators.Count(indicator => lowerText.Contains(indicator)) >= 2;
    }

    /// <summary>
    /// Check if text is conversational
    /// </summary>
    private bool IsConversational(string text)
    {
        var conversationalIndicators = new[]
        {
            "you", "your", "I", "me", "we", "our",
            "?", "!", "please", "thank", "hello", "hi"
        };

        return conversationalIndicators.Count(indicator => text.Contains(indicator)) >= 3;
    }

    /// <summary>
    /// Estimate token count
    /// </summary>
    private int EstimateTokenCount(string text)
    {
        // Rough estimation: 1 token ≈ 4 characters for English
        // Adjust for other languages as needed
        return text.Length / 4;
    }
}

/// <summary>
/// Vector search optimization options
/// </summary>
public class VectorSearchOptions
{
    public double MinSemanticDensity { get; set; } = 0.3;
    public double MaxSemanticDensity { get; set; } = 0.8;
    public bool LowercaseNormalization { get; set; } = true;
    public bool ReplaceUrls { get; set; } = true;
    public bool NormalizeNumbers { get; set; } = true;
    public bool RemoveSpecialCharacters { get; set; } = false;
    public int? MaxTokensPerChunk { get; set; } = 512;
}

/// <summary>
/// Optimized chunk result
/// </summary>
public class OptimizedChunk
{
    public DocumentChunk OriginalChunk { get; set; } = null!;
    public string OptimizedContent { get; set; } = string.Empty;
    public string NormalizedContent { get; set; } = string.Empty;
    public string CleanedContent { get; set; } = string.Empty;
    public string WindowOptimizedContent { get; set; } = string.Empty;
    public double SemanticDensity { get; set; }
    public OptimizationMetrics OptimizationMetrics { get; set; } = null!;
    public EmbeddingHints EmbeddingHints { get; set; } = null!;
    public DateTime OptimizationTimestamp { get; set; }
}

/// <summary>
/// Semantic density result
/// </summary>
public class SemanticDensityResult
{
    public string OptimizedText { get; set; } = string.Empty;
    public double Density { get; set; }
    public int ContentWordCount { get; set; }
    public int TotalWordCount { get; set; }
}

/// <summary>
/// Optimization metrics
/// </summary>
public class OptimizationMetrics
{
    public double CompressionRatio { get; set; }
    public double TokenReduction { get; set; }
    public double SemanticPreservation { get; set; }
    public int OriginalTokenCount { get; set; }
    public int OptimizedTokenCount { get; set; }
    public double QualityScore { get; set; }
}

/// <summary>
/// Embedding hints
/// </summary>
public class EmbeddingHints
{
    public string PrimaryLanguage { get; set; } = "en";
    public List<string> KeyEntities { get; set; } = new();
    public string TextType { get; set; } = "general";
    public string RecommendedModel { get; set; } = "text-embedding-3-small";
    public int ExpectedDimensions { get; set; } = 1536;
    public bool RequiresSpecialHandling { get; set; }
}