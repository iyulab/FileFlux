using FileFlux.Domain;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// Phase 11 T11-002: Search Metadata Enhancement
/// Enriches chunks with metadata for improved search performance
/// </summary>
public class SearchMetadataEnricher
{
    private static readonly Regex HeaderRegex = new(@"^#{1,6}\s+(.+)$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex SentenceRegex = new(@"[.!?]+[\s]+", RegexOptions.Compiled);
    private static readonly Regex KeywordRegex = new(@"\b[A-Za-z]{3,}\b", RegexOptions.Compiled);

    /// <summary>
    /// Enrich chunk with search metadata
    /// </summary>
    public EnrichedChunk EnrichWithSearchMetadata(DocumentChunk chunk, EnrichmentOptions options)
    {
        var enriched = new EnrichedChunk
        {
            OriginalChunk = chunk,
            EnrichmentTimestamp = DateTime.UtcNow
        };

        // 1. Build hierarchical metadata
        enriched.HierarchicalMetadata = BuildHierarchicalMetadata(chunk, options);

        // 2. Extract advanced keywords
        enriched.ExtractedKeywords = ExtractAdvancedKeywords(chunk.Content, options);

        // 3. Generate semantic tags
        enriched.SemanticTags = GenerateSemanticTags(chunk.Content, enriched.ExtractedKeywords);

        // 4. Map chunk relationships
        enriched.ChunkRelationships = MapChunkRelationships(chunk, options);

        // 5. Calculate search scores
        enriched.SearchScores = CalculateSearchScores(chunk, enriched);

        // 6. Generate search hints
        enriched.SearchHints = GenerateSearchHints(enriched);

        return enriched;
    }

    /// <summary>
    /// Build hierarchical metadata
    /// </summary>
    private HierarchicalMetadata BuildHierarchicalMetadata(DocumentChunk chunk, EnrichmentOptions options)
    {
        var metadata = new HierarchicalMetadata();

        // Document level
        metadata.DocumentLevel = new DocumentLevelMetadata
        {
            DocumentId = chunk.Metadata?.FileName ?? chunk.Id.ToString(),
            DocumentTitle = chunk.Metadata?.Title ?? "Untitled",
            DocumentType = chunk.Metadata?.FileType ?? "Unknown",
            TotalChunks = chunk.Metadata?.CustomProperties?.GetValueOrDefault("TotalChunks", 1) as int? ?? 1,
            DocumentLanguage = DetectLanguage(chunk.Content)
        };

        // Section level
        var sectionInfo = ExtractSectionInfo(chunk);
        metadata.SectionLevel = new SectionLevelMetadata
        {
            SectionTitle = sectionInfo.Title,
            SectionNumber = sectionInfo.Number,
            SectionDepth = sectionInfo.Depth,
            ParentSection = sectionInfo.ParentSection,
            SubsectionCount = sectionInfo.SubsectionCount
        };

        // Paragraph level
        metadata.ParagraphLevel = new ParagraphLevelMetadata
        {
            ParagraphIndex = chunk.Index,
            SentenceCount = CountSentences(chunk.Content),
            WordCount = CountWords(chunk.Content),
            AverageSentenceLength = CalculateAverageSentenceLength(chunk.Content),
            ReadabilityScore = CalculateReadabilityScore(chunk.Content)
        };

        // Chunk level
        metadata.ChunkLevel = new ChunkLevelMetadata
        {
            ChunkId = chunk.Id.ToString(),
            ChunkIndex = chunk.Index,
            StartPosition = chunk.Location.StartChar,
            EndPosition = chunk.Location.EndChar,
            ChunkStrategy = chunk.Strategy ?? "Unknown",
            ChunkQuality = chunk.Quality
        };

        return metadata;
    }

    /// <summary>
    /// Extract advanced keywords using TF-IDF-like approach
    /// </summary>
    private ExtractedKeywords ExtractAdvancedKeywords(string content, EnrichmentOptions options)
    {
        var keywords = new ExtractedKeywords();

        // Calculate term frequency
        var termFrequency = CalculateTermFrequency(content);

        // Apply TF-IDF scoring (simplified without corpus)
        var tfidfScores = new Dictionary<string, double>();
        foreach (var term in termFrequency)
        {
            // Simplified IDF calculation
            var idf = Math.Log(100.0 / (1.0 + term.Value)); // Assume corpus of 100 docs
            tfidfScores[term.Key] = term.Value * idf;
        }

        // Select top keywords
        keywords.TfIdfKeywords = tfidfScores
            .OrderByDescending(kvp => kvp.Value)
            .Take(options.MaxKeywordsPerChunk)
            .Select(kvp => new KeywordScore { Keyword = kvp.Key, Score = kvp.Value })
            .ToList();

        // Extract named entities (simplified)
        keywords.NamedEntities = ExtractNamedEntities(content);

        // Extract technical terms
        keywords.TechnicalTerms = ExtractTechnicalTerms(content);

        // Extract phrases (bigrams and trigrams)
        keywords.KeyPhrases = ExtractKeyPhrases(content, options.MaxPhrasesPerChunk);

        return keywords;
    }

    /// <summary>
    /// Generate semantic tags
    /// </summary>
    private SemanticTags GenerateSemanticTags(string content, ExtractedKeywords keywords)
    {
        var tags = new SemanticTags();

        // Topic classification
        tags.Topics = ClassifyTopics(content, keywords);

        // Domain identification
        tags.Domain = IdentifyDomain(content, keywords);

        // Intent detection
        tags.Intent = DetectIntent(content);

        // Sentiment analysis (simplified)
        tags.Sentiment = AnalyzeSentiment(content);

        // Content type
        tags.ContentType = DetermineContentType(content);

        // Complexity level
        tags.ComplexityLevel = AssessComplexity(content);

        // Audience type
        tags.AudienceType = InferAudience(content, tags.ComplexityLevel);

        return tags;
    }

    /// <summary>
    /// Map chunk relationships
    /// </summary>
    private ChunkRelationships MapChunkRelationships(DocumentChunk chunk, EnrichmentOptions options)
    {
        var relationships = new ChunkRelationships
        {
            ChunkId = chunk.Id.ToString()
        };

        // Sequential relationships
        relationships.PreviousChunkId = chunk.Index > 0
            ? GenerateChunkId(chunk.Metadata?.FileName, chunk.Index - 1)
            : null;

        relationships.NextChunkId = GenerateChunkId(chunk.Metadata?.FileName, chunk.Index + 1);

        // Hierarchical relationships
        relationships.ParentChunkId = DetermineParentChunk(chunk);
        relationships.ChildChunkIds = new List<string>(); // Would be populated in full implementation

        // Reference relationships
        relationships.ReferencedChunks = ExtractReferences(chunk.Content);
        relationships.ReferencedByChunks = new List<string>(); // Would need corpus analysis

        // Semantic relationships
        relationships.SimilarChunks = new List<string>(); // Would need embedding comparison
        relationships.RelatedChunks = FindRelatedChunks(chunk);

        return relationships;
    }

    /// <summary>
    /// Calculate search scores
    /// </summary>
    private SearchScores CalculateSearchScores(DocumentChunk chunk, EnrichedChunk enriched)
    {
        var scores = new SearchScores();

        // Keyword match potential
        scores.KeywordMatchScore = CalculateKeywordMatchPotential(enriched.ExtractedKeywords);

        // Semantic relevance
        scores.SemanticRelevanceScore = CalculateSemanticRelevance(enriched.SemanticTags);

        // Structural importance
        scores.StructuralImportanceScore = CalculateStructuralImportance(enriched.HierarchicalMetadata);

        // Information density
        scores.InformationDensityScore = CalculateInformationDensity(chunk.Content);

        // Uniqueness score
        scores.UniquenessScore = CalculateUniqueness(enriched.ExtractedKeywords);

        // Overall search quality
        scores.OverallSearchQuality = (
            scores.KeywordMatchScore * 0.25 +
            scores.SemanticRelevanceScore * 0.25 +
            scores.StructuralImportanceScore * 0.2 +
            scores.InformationDensityScore * 0.15 +
            scores.UniquenessScore * 0.15
        );

        return scores;
    }

    /// <summary>
    /// Generate search hints
    /// </summary>
    private SearchHints GenerateSearchHints(EnrichedChunk enriched)
    {
        var hints = new SearchHints();

        // Best search queries
        hints.SuggestedQueries = GenerateSuggestedQueries(enriched);

        // Indexing recommendations
        hints.IndexingRecommendations = new IndexingRecommendations
        {
            PrimaryIndex = enriched.ExtractedKeywords.TfIdfKeywords.FirstOrDefault()?.Keyword ?? "",
            SecondaryIndices = enriched.ExtractedKeywords.TfIdfKeywords
                .Skip(1)
                .Take(3)
                .Select(k => k.Keyword)
                .ToList(),
            ShouldBoost = enriched.SearchScores.OverallSearchQuality > 0.7,
            BoostFactor = Math.Max(1.0, enriched.SearchScores.OverallSearchQuality * 1.5)
        };

        // Retrieval hints
        hints.RetrievalHints = new RetrievalHints
        {
            PreferredSearchType = DeterminePreferredSearchType(enriched),
            ExpectedQueryTypes = PredictQueryTypes(enriched),
            OptimalEmbeddingModel = SelectOptimalEmbeddingModel(enriched),
            RequiresContextExpansion = enriched.HierarchicalMetadata.SectionLevel.SectionDepth > 2
        };

        return hints;
    }

    // Helper methods

    private SectionInfo ExtractSectionInfo(DocumentChunk chunk)
    {
        var info = new SectionInfo();

        var headerMatch = HeaderRegex.Match(chunk.Content);
        if (headerMatch.Success)
        {
            info.Title = headerMatch.Groups[1].Value;
            info.Depth = headerMatch.Value.TakeWhile(c => c == '#').Count();
        }

        info.Number = chunk.Index / 10; // Simplified section numbering
        info.SubsectionCount = HeaderRegex.Matches(chunk.Content).Count - 1;

        return info;
    }

    private int CountSentences(string text)
    {
        return SentenceRegex.Matches(text).Count + 1;
    }

    private int CountWords(string text)
    {
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private double CalculateAverageSentenceLength(string text)
    {
        var sentences = SentenceRegex.Split(text).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (sentences.Count == 0) return 0;

        return sentences.Average(s => s.Split(' ').Length);
    }

    private double CalculateReadabilityScore(string text)
    {
        // Simplified Flesch Reading Ease approximation
        var sentences = CountSentences(text);
        var words = CountWords(text);
        var syllables = EstimateSyllables(text);

        if (sentences == 0 || words == 0) return 0;

        var score = 206.835 - 1.015 * (words / (double)sentences) - 84.6 * (syllables / (double)words);
        return Math.Max(0, Math.Min(100, score));
    }

    private int EstimateSyllables(string text)
    {
        // Very simplified syllable estimation
        return Regex.Matches(text, @"[aeiouAEIOU]+").Count;
    }

    private Dictionary<string, int> CalculateTermFrequency(string content)
    {
        var words = KeywordRegex.Matches(content)
            .Select(m => m.Value.ToLowerInvariant())
            .Where(w => w.Length > 3 && !IsStopWord(w));

        var frequency = new Dictionary<string, int>();
        foreach (var word in words)
        {
            frequency[word] = frequency.GetValueOrDefault(word, 0) + 1;
        }

        return frequency;
    }

    private List<string> ExtractNamedEntities(string content)
    {
        // Simplified named entity extraction (capitalized phrases)
        var pattern = @"\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*\b";
        return Regex.Matches(content, pattern)
            .Select(m => m.Value)
            .Distinct()
            .Take(10)
            .ToList();
    }

    private List<string> ExtractTechnicalTerms(string content)
    {
        var technicalPatterns = new[]
        {
            @"\b[A-Z]{2,}\b", // Acronyms
            @"\b\w+\(\)", // Function names
            @"\b\w+\.\w+", // Dot notation
            @"\b(?:class|interface|struct|enum)\s+\w+", // Type definitions
        };

        var terms = new HashSet<string>();
        foreach (var pattern in technicalPatterns)
        {
            var matches = Regex.Matches(content, pattern);
            foreach (Match match in matches)
            {
                terms.Add(match.Value);
            }
        }

        return terms.Take(15).ToList();
    }

    private List<string> ExtractKeyPhrases(string content, int maxPhrases)
    {
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var phrases = new List<string>();

        // Extract bigrams
        for (int i = 0; i < words.Length - 1; i++)
        {
            if (!IsStopWord(words[i].ToLowerInvariant()) && !IsStopWord(words[i + 1].ToLowerInvariant()))
            {
                phrases.Add($"{words[i]} {words[i + 1]}");
            }
        }

        // Extract trigrams
        for (int i = 0; i < words.Length - 2; i++)
        {
            if (!IsStopWord(words[i].ToLowerInvariant()) && !IsStopWord(words[i + 2].ToLowerInvariant()))
            {
                phrases.Add($"{words[i]} {words[i + 1]} {words[i + 2]}");
            }
        }

        return phrases.Distinct().Take(maxPhrases).ToList();
    }

    private List<string> ClassifyTopics(string content, ExtractedKeywords keywords)
    {
        var topics = new List<string>();

        // Technology topics
        if (keywords.TechnicalTerms.Any() || content.Contains("software") || content.Contains("code"))
            topics.Add("Technology");

        // Business topics
        if (content.Contains("business") || content.Contains("market") || content.Contains("revenue"))
            topics.Add("Business");

        // Science topics
        if (content.Contains("research") || content.Contains("study") || content.Contains("analysis"))
            topics.Add("Science");

        return topics;
    }

    private string IdentifyDomain(string content, ExtractedKeywords keywords)
    {
        if (keywords.TechnicalTerms.Count > 5)
            return "Technical";
        if (keywords.NamedEntities.Count > 5)
            return "Business";
        if (content.Length > 1000 && keywords.TfIdfKeywords.Count > 10)
            return "Academic";

        return "General";
    }

    private string DetectIntent(string content)
    {
        if (content.Contains("how to") || content.Contains("guide") || content.Contains("tutorial"))
            return "Instructional";
        if (content.Contains('?') || content.Contains("what") || content.Contains("why"))
            return "Informational";
        if (content.Contains("buy") || content.Contains("purchase") || content.Contains("order"))
            return "Transactional";

        return "Descriptive";
    }

    private string AnalyzeSentiment(string content)
    {
        var positiveWords = new[] { "good", "great", "excellent", "positive", "success" };
        var negativeWords = new[] { "bad", "poor", "negative", "failure", "problem" };

        var positiveCount = positiveWords.Count(word => content.ToLowerInvariant().Contains(word));
        var negativeCount = negativeWords.Count(word => content.ToLowerInvariant().Contains(word));

        if (positiveCount > negativeCount) return "Positive";
        if (negativeCount > positiveCount) return "Negative";
        return "Neutral";
    }

    private string DetermineContentType(string content)
    {
        if (Regex.IsMatch(content, @"```|<code>"))
            return "Code";
        if (content.Contains('|') && content.Split('\n').Count(l => l.Contains('|')) > 2)
            return "Table";
        if (Regex.IsMatch(content, @"^\d+\.|^[-*+]\s", RegexOptions.Multiline))
            return "List";

        return "Prose";
    }

    private string AssessComplexity(string content)
    {
        var avgSentenceLength = CalculateAverageSentenceLength(content);
        var technicalTermDensity = (double)ExtractTechnicalTerms(content).Count / CountWords(content);

        if (avgSentenceLength > 25 || technicalTermDensity > 0.1)
            return "High";
        if (avgSentenceLength > 15 || technicalTermDensity > 0.05)
            return "Medium";

        return "Low";
    }

    private string InferAudience(string content, string complexity)
    {
        if (complexity == "High")
            return "Expert";
        if (complexity == "Medium")
            return "Professional";

        return "General";
    }

    private string GenerateChunkId(string? documentId, int chunkIndex)
    {
        return $"{documentId ?? "unknown"}_{chunkIndex}";
    }

    private string? DetermineParentChunk(DocumentChunk chunk)
    {
        // Simplified parent determination
        if (chunk.Index == 0)
            return null;

        return GenerateChunkId(chunk.Metadata?.FileName, 0);
    }

    private List<string> ExtractReferences(string content)
    {
        var references = new List<string>();

        // Extract figure references
        var figureRefs = Regex.Matches(content, @"(?:Figure|Fig\.?)\s+\d+");
        references.AddRange(figureRefs.Select(m => m.Value));

        // Extract section references
        var sectionRefs = Regex.Matches(content, @"(?:Section|Sec\.?)\s+\d+");
        references.AddRange(sectionRefs.Select(m => m.Value));

        return references.Distinct().ToList();
    }

    private List<string> FindRelatedChunks(DocumentChunk chunk)
    {
        // Would need corpus analysis in full implementation
        return new List<string>();
    }

    private double CalculateKeywordMatchPotential(ExtractedKeywords keywords)
    {
        var score = 0.0;

        if (keywords.TfIdfKeywords.Any())
            score += 0.4;
        if (keywords.NamedEntities.Any())
            score += 0.3;
        if (keywords.TechnicalTerms.Any())
            score += 0.2;
        if (keywords.KeyPhrases.Any())
            score += 0.1;

        return Math.Min(1.0, score);
    }

    private double CalculateSemanticRelevance(SemanticTags tags)
    {
        var score = 0.0;

        if (tags.Topics.Any())
            score += 0.3;
        if (!string.IsNullOrEmpty(tags.Domain) && tags.Domain != "General")
            score += 0.3;
        if (!string.IsNullOrEmpty(tags.Intent) && tags.Intent != "Descriptive")
            score += 0.2;
        if (tags.ComplexityLevel == "High")
            score += 0.2;

        return Math.Min(1.0, score);
    }

    private double CalculateStructuralImportance(HierarchicalMetadata metadata)
    {
        var score = 0.5; // Base score

        // Headers are important
        if (!string.IsNullOrEmpty(metadata.SectionLevel.SectionTitle))
            score += 0.2;

        // Early chunks often contain important info
        if (metadata.ChunkLevel.ChunkIndex < 3)
            score += 0.1;

        // High-level sections are important
        if (metadata.SectionLevel.SectionDepth <= 2)
            score += 0.2;

        return Math.Min(1.0, score);
    }

    private double CalculateInformationDensity(string content)
    {
        var words = CountWords(content);
        var uniqueWords = content.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .Distinct()
            .Count();

        if (words == 0) return 0;

        return (double)uniqueWords / words;
    }

    private double CalculateUniqueness(ExtractedKeywords keywords)
    {
        // Simplified uniqueness based on technical terms and entities
        var uniqueTerms = keywords.TechnicalTerms.Count + keywords.NamedEntities.Count;
        return Math.Min(1.0, uniqueTerms / 20.0);
    }

    private List<string> GenerateSuggestedQueries(EnrichedChunk enriched)
    {
        var queries = new List<string>();

        // Keyword-based queries
        if (enriched.ExtractedKeywords.TfIdfKeywords.Any())
        {
            var topKeywords = string.Join(" ", enriched.ExtractedKeywords.TfIdfKeywords.Take(3).Select(k => k.Keyword));
            queries.Add(topKeywords);
        }

        // Entity-based queries
        if (enriched.ExtractedKeywords.NamedEntities.Any())
        {
            queries.Add(enriched.ExtractedKeywords.NamedEntities.First());
        }

        // Intent-based queries
        if (enriched.SemanticTags.Intent == "Instructional")
        {
            queries.Add($"how to {enriched.ExtractedKeywords.TfIdfKeywords.FirstOrDefault()?.Keyword}");
        }

        return queries;
    }

    private string DeterminePreferredSearchType(EnrichedChunk enriched)
    {
        if (enriched.ExtractedKeywords.TechnicalTerms.Count > 5)
            return "Hybrid"; // Both keyword and semantic

        if (enriched.SemanticTags.ContentType == "Code")
            return "Keyword";

        return "Semantic";
    }

    private List<string> PredictQueryTypes(EnrichedChunk enriched)
    {
        var types = new List<string>();

        if (enriched.SemanticTags.Intent == "Instructional")
            types.Add("How-to");
        if (enriched.ExtractedKeywords.NamedEntities.Any())
            types.Add("Entity-lookup");
        if (enriched.SemanticTags.ContentType == "Code")
            types.Add("Code-search");

        if (!types.Any())
            types.Add("General");

        return types;
    }

    private string SelectOptimalEmbeddingModel(EnrichedChunk enriched)
    {
        if (enriched.SemanticTags.ContentType == "Code")
            return "code-embedding-002";

        if (enriched.SemanticTags.ComplexityLevel == "High")
            return "text-embedding-3-large";

        return "text-embedding-3-small";
    }

    private string DetectLanguage(string text)
    {
        // Simplified language detection
        if (Regex.IsMatch(text, @"[\u4e00-\u9fff]"))
            return "zh";
        if (Regex.IsMatch(text, @"[\u3040-\u309f\u30a0-\u30ff]"))
            return "ja";
        if (Regex.IsMatch(text, @"[\uac00-\ud7af]"))
            return "ko";

        return "en";
    }

    private bool IsStopWord(string word)
    {
        var stopWords = new HashSet<string>
        {
            "the", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with",
            "by", "from", "as", "is", "was", "are", "were", "been", "being", "have",
            "has", "had", "do", "does", "did", "will", "would", "could", "should"
        };

        return stopWords.Contains(word);
    }

    // Helper classes
    private class SectionInfo
    {
        public string Title { get; set; } = string.Empty;
        public int Number { get; set; }
        public int Depth { get; set; }
        public string? ParentSection { get; set; }
        public int SubsectionCount { get; set; }
    }
}

// Data models

public class EnrichmentOptions
{
    public int MaxKeywordsPerChunk { get; set; } = 20;
    public int MaxPhrasesPerChunk { get; set; } = 10;
    public bool EnableSemanticTagging { get; set; } = true;
    public bool EnableRelationshipMapping { get; set; } = true;
}

public class EnrichedChunk
{
    public DocumentChunk OriginalChunk { get; set; } = null!;
    public HierarchicalMetadata HierarchicalMetadata { get; set; } = null!;
    public ExtractedKeywords ExtractedKeywords { get; set; } = null!;
    public SemanticTags SemanticTags { get; set; } = null!;
    public ChunkRelationships ChunkRelationships { get; set; } = null!;
    public SearchScores SearchScores { get; set; } = null!;
    public SearchHints SearchHints { get; set; } = null!;
    public DateTime EnrichmentTimestamp { get; set; }
}

public class HierarchicalMetadata
{
    public DocumentLevelMetadata DocumentLevel { get; set; } = null!;
    public SectionLevelMetadata SectionLevel { get; set; } = null!;
    public ParagraphLevelMetadata ParagraphLevel { get; set; } = null!;
    public ChunkLevelMetadata ChunkLevel { get; set; } = null!;
}

public class DocumentLevelMetadata
{
    public string DocumentId { get; set; } = string.Empty;
    public string DocumentTitle { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public int TotalChunks { get; set; }
    public string DocumentLanguage { get; set; } = string.Empty;
}

public class SectionLevelMetadata
{
    public string SectionTitle { get; set; } = string.Empty;
    public int SectionNumber { get; set; }
    public int SectionDepth { get; set; }
    public string? ParentSection { get; set; }
    public int SubsectionCount { get; set; }
}

public class ParagraphLevelMetadata
{
    public int ParagraphIndex { get; set; }
    public int SentenceCount { get; set; }
    public int WordCount { get; set; }
    public double AverageSentenceLength { get; set; }
    public double ReadabilityScore { get; set; }
}

public class ChunkLevelMetadata
{
    public string ChunkId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
    public string ChunkStrategy { get; set; } = string.Empty;
    public double ChunkQuality { get; set; }
}

public class ExtractedKeywords
{
    public List<KeywordScore> TfIdfKeywords { get; set; } = new();
    public List<string> NamedEntities { get; set; } = new();
    public List<string> TechnicalTerms { get; set; } = new();
    public List<string> KeyPhrases { get; set; } = new();
}

public class KeywordScore
{
    public string Keyword { get; set; } = string.Empty;
    public double Score { get; set; }
}

public class SemanticTags
{
    public List<string> Topics { get; set; } = new();
    public string Domain { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public string Sentiment { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string ComplexityLevel { get; set; } = string.Empty;
    public string AudienceType { get; set; } = string.Empty;
}

public class ChunkRelationships
{
    public string ChunkId { get; set; } = string.Empty;
    public string? PreviousChunkId { get; set; }
    public string? NextChunkId { get; set; }
    public string? ParentChunkId { get; set; }
    public List<string> ChildChunkIds { get; set; } = new();
    public List<string> ReferencedChunks { get; set; } = new();
    public List<string> ReferencedByChunks { get; set; } = new();
    public List<string> SimilarChunks { get; set; } = new();
    public List<string> RelatedChunks { get; set; } = new();
}

public class SearchScores
{
    public double KeywordMatchScore { get; set; }
    public double SemanticRelevanceScore { get; set; }
    public double StructuralImportanceScore { get; set; }
    public double InformationDensityScore { get; set; }
    public double UniquenessScore { get; set; }
    public double OverallSearchQuality { get; set; }
}

public class SearchHints
{
    public List<string> SuggestedQueries { get; set; } = new();
    public IndexingRecommendations IndexingRecommendations { get; set; } = null!;
    public RetrievalHints RetrievalHints { get; set; } = null!;
}

public class IndexingRecommendations
{
    public string PrimaryIndex { get; set; } = string.Empty;
    public List<string> SecondaryIndices { get; set; } = new();
    public bool ShouldBoost { get; set; }
    public double BoostFactor { get; set; }
}

public class RetrievalHints
{
    public string PreferredSearchType { get; set; } = string.Empty;
    public List<string> ExpectedQueryTypes { get; set; } = new();
    public string OptimalEmbeddingModel { get; set; } = string.Empty;
    public bool RequiresContextExpansion { get; set; }
}
