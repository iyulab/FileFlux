using FileFlux.Domain;
using System.Text;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// Phase 11 T11-003: Hybrid Search Support
/// Preprocesses content for both keyword-based (BM25) and semantic search
/// </summary>
public class HybridSearchPreprocessor
{
    private static readonly Regex WordRegex = new(@"\b\w+\b", RegexOptions.Compiled);
    private static readonly Regex StemRegex = new(@"(?:ing|ed|er|est|ly|s)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Preprocess chunk for hybrid search
    /// </summary>
    public HybridSearchResult PreprocessForHybridSearch(DocumentChunk chunk, HybridSearchOptions options)
    {
        var result = new HybridSearchResult
        {
            OriginalChunk = chunk,
            ProcessingTimestamp = DateTime.UtcNow
        };

        // 1. BM25 preprocessing
        result.BM25Preprocessing = PreprocessForBM25(chunk.Content, options.BM25Options);

        // 2. Keyword index metadata
        result.KeywordIndexMetadata = GenerateKeywordIndexMetadata(result.BM25Preprocessing, options);

        // 3. Weight calculation information
        result.WeightCalculationInfo = CalculateWeightInformation(chunk, result, options);

        // 4. Reranking hints
        result.RerankingHints = GenerateRerankingHints(chunk, result, options);

        // 5. Combined search signals
        result.CombinedSearchSignals = GenerateCombinedSignals(result);

        return result;
    }

    /// <summary>
    /// Preprocess content for BM25 scoring
    /// </summary>
    private BM25Preprocessing PreprocessForBM25(string content, BM25Options options)
    {
        var preprocessing = new BM25Preprocessing
        {
            OriginalContent = content
        };

        // 1. Tokenization with various levels
        preprocessing.BasicTokens = BasicTokenization(content);
        preprocessing.ProcessedTokens = ProcessTokens(preprocessing.BasicTokens, options);

        // 2. Term frequency calculation
        preprocessing.TermFrequencies = CalculateTermFrequencies(preprocessing.ProcessedTokens);

        // 3. Document length normalization
        preprocessing.DocumentLength = preprocessing.ProcessedTokens.Count;
        preprocessing.UniqueTermCount = preprocessing.TermFrequencies.Count;

        // 4. Field-specific tokenization
        preprocessing.FieldTokens = ExtractFieldTokens(content);

        // 5. Position information
        preprocessing.PositionInformation = CalculatePositionInformation(preprocessing.BasicTokens);

        return preprocessing;
    }

    /// <summary>
    /// Generate keyword index metadata
    /// </summary>
    private KeywordIndexMetadata GenerateKeywordIndexMetadata(BM25Preprocessing bm25, HybridSearchOptions options)
    {
        var metadata = new KeywordIndexMetadata();

        // 1. Inverted index entries
        metadata.InvertedIndexEntries = GenerateInvertedIndexEntries(bm25);

        // 2. Term importance scores
        metadata.TermImportanceScores = CalculateTermImportanceScores(bm25);

        // 3. Field boosting information
        metadata.FieldBoosts = CalculateFieldBoosts(bm25);

        // 4. Query expansion candidates
        metadata.QueryExpansionCandidates = GenerateQueryExpansionCandidates(bm25);

        // 5. Synonym mappings
        metadata.SynonymMappings = GenerateSynonymMappings(bm25, options);

        return metadata;
    }

    /// <summary>
    /// Calculate weight information for hybrid scoring
    /// </summary>
    private WeightCalculationInfo CalculateWeightInformation(
        DocumentChunk chunk,
        HybridSearchResult result,
        HybridSearchOptions options)
    {
        var weightInfo = new WeightCalculationInfo();

        // 1. Keyword weight factors
        weightInfo.KeywordWeightFactors = CalculateKeywordWeightFactors(result.BM25Preprocessing);

        // 2. Semantic weight factors
        weightInfo.SemanticWeightFactors = CalculateSemanticWeightFactors(chunk);

        // 3. Content type adjustments
        weightInfo.ContentTypeAdjustments = DetermineContentTypeAdjustments(chunk.Content);

        // 4. Position-based weights
        weightInfo.PositionWeights = CalculatePositionWeights(chunk, result.BM25Preprocessing);

        // 5. Freshness factors
        weightInfo.FreshnessFactors = CalculateFreshnessFactors(chunk);

        // 6. Quality multipliers
        weightInfo.QualityMultipliers = CalculateQualityMultipliers(chunk);

        // 7. Recommended hybrid ratios
        weightInfo.RecommendedHybridRatio = CalculateOptimalHybridRatio(weightInfo);

        return weightInfo;
    }

    /// <summary>
    /// Generate reranking hints
    /// </summary>
    private RerankingHints GenerateRerankingHints(
        DocumentChunk chunk,
        HybridSearchResult result,
        HybridSearchOptions options)
    {
        var hints = new RerankingHints();

        // 1. Reranking features
        hints.RerankingFeatures = ExtractRerankingFeatures(chunk, result);

        // 2. Cross-encoder hints
        hints.CrossEncoderHints = GenerateCrossEncoderHints(chunk, result);

        // 3. Contextual signals
        hints.ContextualSignals = GenerateContextualSignals(chunk);

        // 4. User interaction predictions
        hints.UserInteractionPredictions = PredictUserInteractions(chunk, result);

        // 5. Quality indicators
        hints.QualityIndicators = ExtractQualityIndicators(chunk, result);

        return hints;
    }

    /// <summary>
    /// Generate combined search signals
    /// </summary>
    private CombinedSearchSignals GenerateCombinedSignals(HybridSearchResult result)
    {
        var signals = new CombinedSearchSignals();

        // 1. Multi-modal features
        signals.MultiModalFeatures = ExtractMultiModalFeatures(result);

        // 2. Cross-field correlations
        signals.CrossFieldCorrelations = CalculateCrossFieldCorrelations(result);

        // 3. Temporal signals
        signals.TemporalSignals = ExtractTemporalSignals(result);

        // 4. Authority signals
        signals.AuthoritySignals = CalculateAuthoritySignals(result);

        // 5. User context hints
        signals.UserContextHints = GenerateUserContextHints(result);

        return signals;
    }

    // Helper methods for BM25 preprocessing

    private List<string> BasicTokenization(string content)
    {
        var matches = WordRegex.Matches(content);
        return matches.Select(m => m.Value.ToLowerInvariant()).ToList();
    }

    private List<string> ProcessTokens(List<string> tokens, BM25Options options)
    {
        var processed = tokens.ToList();

        // Remove stop words
        if (options.RemoveStopWords)
        {
            processed = processed.Where(t => !IsStopWord(t)).ToList();
        }

        // Apply stemming
        if (options.ApplyStemming)
        {
            processed = processed.Select(ApplyStemming).ToList();
        }

        // Filter by minimum length
        processed = processed.Where(t => t.Length >= options.MinTokenLength).ToList();

        return processed;
    }

    private Dictionary<string, int> CalculateTermFrequencies(List<string> tokens)
    {
        var frequencies = new Dictionary<string, int>();
        foreach (var token in tokens)
        {
            frequencies[token] = frequencies.GetValueOrDefault(token, 0) + 1;
        }
        return frequencies;
    }

    private Dictionary<string, List<string>> ExtractFieldTokens(string content)
    {
        var fieldTokens = new Dictionary<string, List<string>>();

        // Extract title/header tokens
        var headerMatches = Regex.Matches(content, @"^#{1,6}\s+(.+)$", RegexOptions.Multiline);
        fieldTokens["headers"] = headerMatches
            .Select(m => m.Groups[1].Value)
            .SelectMany(header => BasicTokenization(header))
            .ToList();

        // Extract emphasized tokens (bold, italic)
        var boldMatches = Regex.Matches(content, @"\*\*([^*]+)\*\*");
        var italicMatches = Regex.Matches(content, @"\*([^*]+)\*");
        fieldTokens["emphasized"] = boldMatches.Concat(italicMatches)
            .Select(m => m.Groups[1].Value)
            .SelectMany(text => BasicTokenization(text))
            .ToList();

        // Extract code tokens
        var codeMatches = Regex.Matches(content, @"`([^`]+)`");
        fieldTokens["code"] = codeMatches
            .Select(m => m.Groups[1].Value)
            .SelectMany(code => BasicTokenization(code))
            .ToList();

        return fieldTokens;
    }

    private Dictionary<string, List<int>> CalculatePositionInformation(List<string> tokens)
    {
        var positions = new Dictionary<string, List<int>>();

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (!positions.ContainsKey(token))
            {
                positions[token] = new List<int>();
            }
            positions[token].Add(i);
        }

        return positions;
    }

    // Helper methods for keyword index metadata

    private List<InvertedIndexEntry> GenerateInvertedIndexEntries(BM25Preprocessing bm25)
    {
        var entries = new List<InvertedIndexEntry>();

        foreach (var termFreq in bm25.TermFrequencies)
        {
            var positions = bm25.PositionInformation.GetValueOrDefault(termFreq.Key, new List<int>());

            entries.Add(new InvertedIndexEntry
            {
                Term = termFreq.Key,
                TermFrequency = termFreq.Value,
                Positions = positions,
                FirstPosition = positions.Any() ? positions.Min() : -1,
                LastPosition = positions.Any() ? positions.Max() : -1,
                PositionSpread = positions.Any() ? positions.Max() - positions.Min() : 0
            });
        }

        return entries.OrderByDescending(e => e.TermFrequency).ToList();
    }

    private Dictionary<string, double> CalculateTermImportanceScores(BM25Preprocessing bm25)
    {
        var scores = new Dictionary<string, double>();
        var maxFreq = bm25.TermFrequencies.Values.DefaultIfEmpty(0).Max();

        foreach (var term in bm25.TermFrequencies)
        {
            var tfNorm = (double)term.Value / maxFreq;
            var positions = bm25.PositionInformation.GetValueOrDefault(term.Key, new List<int>());

            // Position boost (earlier = more important)
            var positionBoost = positions.Any() ? 1.0 / (1.0 + positions.Min() / 100.0) : 1.0;

            // Length penalty (very short or very long terms are less important)
            var lengthBoost = CalculateLengthBoost(term.Key);

            scores[term.Key] = tfNorm * positionBoost * lengthBoost;
        }

        return scores;
    }

    private Dictionary<string, double> CalculateFieldBoosts(BM25Preprocessing bm25)
    {
        var boosts = new Dictionary<string, double>
        {
            ["headers"] = 2.0,      // Headers are very important
            ["emphasized"] = 1.5,   // Bold/italic text is important  
            ["code"] = 1.2,        // Code has moderate importance
            ["body"] = 1.0         // Regular body text baseline
        };

        return boosts;
    }

    private List<string> GenerateQueryExpansionCandidates(BM25Preprocessing bm25)
    {
        var candidates = new List<string>();

        // Add high-frequency terms as expansion candidates
        var highFreqTerms = bm25.TermFrequencies
            .Where(tf => tf.Value >= 2)
            .OrderByDescending(tf => tf.Value)
            .Take(10)
            .Select(tf => tf.Key);

        candidates.AddRange(highFreqTerms);

        // Add terms that appear in multiple fields
        foreach (var fieldTokens in bm25.FieldTokens)
        {
            var commonTerms = fieldTokens.Value
                .GroupBy(t => t)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);

            candidates.AddRange(commonTerms);
        }

        return candidates.Distinct().Take(20).ToList();
    }

    private Dictionary<string, List<string>> GenerateSynonymMappings(BM25Preprocessing bm25, HybridSearchOptions options)
    {
        var synonyms = new Dictionary<string, List<string>>();

        // Add simple morphological variants
        foreach (var term in bm25.TermFrequencies.Keys)
        {
            var variants = GenerateMorphologicalVariants(term);
            if (variants.Any())
            {
                synonyms[term] = variants;
            }
        }

        return synonyms;
    }

    // Helper methods for weight calculation

    private KeywordWeightFactors CalculateKeywordWeightFactors(BM25Preprocessing bm25)
    {
        return new KeywordWeightFactors
        {
            TermFrequencyWeight = 0.6,
            PositionWeight = 0.2,
            FieldWeight = 0.15,
            LengthNormalizationWeight = 0.05,
            HighestScoringTerm = bm25.TermFrequencies.OrderByDescending(tf => tf.Value).FirstOrDefault().Key ?? "",
            AverageTermFrequency = bm25.TermFrequencies.Values.DefaultIfEmpty(0).Average()
        };
    }

    private SemanticWeightFactors CalculateSemanticWeightFactors(DocumentChunk chunk)
    {
        return new SemanticWeightFactors
        {
            SemanticDensityWeight = 0.4,
            CoherenceWeight = 0.3,
            TopicalRelevanceWeight = 0.2,
            ContextualWeight = 0.1,
            EstimatedSemanticDensity = EstimateSemanticDensity(chunk.Content),
            CoherenceScore = chunk.Props.ContainsKey("SemanticCoherence")
                ? Convert.ToDouble(chunk.Props["SemanticCoherence"])
                : 0.5
        };
    }

    private ContentTypeAdjustments DetermineContentTypeAdjustments(string content)
    {
        var adjustments = new ContentTypeAdjustments();

        if (IsCodeContent(content))
        {
            adjustments.KeywordBoost = 1.3;
            adjustments.SemanticBoost = 0.8;
            adjustments.ContentType = "Code";
        }
        else if (IsTechnicalContent(content))
        {
            adjustments.KeywordBoost = 1.1;
            adjustments.SemanticBoost = 1.2;
            adjustments.ContentType = "Technical";
        }
        else if (IsNarrativeContent(content))
        {
            adjustments.KeywordBoost = 0.9;
            adjustments.SemanticBoost = 1.3;
            adjustments.ContentType = "Narrative";
        }
        else
        {
            adjustments.KeywordBoost = 1.0;
            adjustments.SemanticBoost = 1.0;
            adjustments.ContentType = "General";
        }

        return adjustments;
    }

    private PositionWeights CalculatePositionWeights(DocumentChunk chunk, BM25Preprocessing bm25)
    {
        return new PositionWeights
        {
            TitleWeight = 2.0,
            FirstParagraphWeight = 1.5,
            LastParagraphWeight = 1.2,
            MiddleWeight = 1.0,
            ChunkPosition = chunk.Index,
            RelativePosition = chunk.Index / Math.Max(1.0, chunk.Metadata?.CustomProperties?.GetValueOrDefault("TotalChunks", 1) as int? ?? 1)
        };
    }

    private FreshnessFactors CalculateFreshnessFactors(DocumentChunk chunk)
    {
        var createdHoursAgo = (DateTime.UtcNow - chunk.CreatedAt).TotalHours;

        return new FreshnessFactors
        {
            RecencyBoost = Math.Max(0.5, 1.0 - (createdHoursAgo / (24 * 30))), // Decay over 30 days
            CreatedAt = chunk.CreatedAt,
            AgeInHours = createdHoursAgo,
            IsRecent = createdHoursAgo < 24
        };
    }

    private QualityMultipliers CalculateQualityMultipliers(DocumentChunk chunk)
    {
        return new QualityMultipliers
        {
            QualityScore = chunk.Quality,
            QualityBoost = Math.Max(0.5, Math.Min(2.0, chunk.Quality * 1.5)),
            HasHighQuality = chunk.Quality > 0.8,
            ConfidenceLevel = chunk.Quality > 0.6 ? "High" : chunk.Quality > 0.4 ? "Medium" : "Low"
        };
    }

    private HybridRatio CalculateOptimalHybridRatio(WeightCalculationInfo weightInfo)
    {
        var keywordStrength = (weightInfo.KeywordWeightFactors.AverageTermFrequency / 10.0) +
                            (weightInfo.ContentTypeAdjustments.KeywordBoost - 1.0);
        var semanticStrength = weightInfo.SemanticWeightFactors.EstimatedSemanticDensity +
                             (weightInfo.ContentTypeAdjustments.SemanticBoost - 1.0);

        var total = keywordStrength + semanticStrength;
        if (total == 0) total = 1;

        return new HybridRatio
        {
            KeywordRatio = keywordStrength / total,
            SemanticRatio = semanticStrength / total,
            RecommendedStrategy = keywordStrength > semanticStrength ? "Keyword-Heavy" : "Semantic-Heavy",
            ConfidenceScore = Math.Abs(keywordStrength - semanticStrength) / total
        };
    }

    // Additional helper methods

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

    private string ApplyStemming(string word)
    {
        // Very simplified stemming
        return StemRegex.Replace(word, "");
    }

    private double CalculateLengthBoost(string term)
    {
        if (term.Length < 3) return 0.5;
        if (term.Length > 15) return 0.7;
        return 1.0;
    }

    private List<string> GenerateMorphologicalVariants(string term)
    {
        var variants = new List<string>();

        // Add plural/singular variants
        if (term.EndsWith("s", StringComparison.Ordinal) && term.Length > 3)
        {
            variants.Add(term.Substring(0, term.Length - 1));
        }
        else
        {
            variants.Add(term + "s");
        }

        // Add -ing, -ed variants
        if (term.Length > 4)
        {
            variants.Add(term + "ing");
            variants.Add(term + "ed");
        }

        return variants.Where(v => v != term).ToList();
    }

    private double EstimateSemanticDensity(string content)
    {
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var uniqueWords = words.Select(w => w.ToLowerInvariant()).Distinct().Count();

        if (words.Length == 0) return 0;
        return (double)uniqueWords / words.Length;
    }

    private bool IsCodeContent(string content)
    {
        return content.Contains("function") || content.Contains("class") ||
               content.Contains('{') && content.Contains('}') ||
               content.Contains("def ") || content.Contains("import ");
    }

    private bool IsTechnicalContent(string content)
    {
        var technicalTerms = new[] { "algorithm", "system", "database", "server", "API", "protocol" };
        return technicalTerms.Count(term => content.ToLowerInvariant().Contains(term)) >= 2;
    }

    private bool IsNarrativeContent(string content)
    {
        return content.Contains(" story") || content.Contains(" narrative") ||
               content.Contains("once upon") || content.Contains("first person");
    }

    // Placeholder implementations for complex features
    private List<RerankingFeature> ExtractRerankingFeatures(DocumentChunk chunk, HybridSearchResult result)
    {
        return new List<RerankingFeature>
        {
            new() { FeatureName = "ChunkQuality", FeatureValue = chunk.Quality },
            new() { FeatureName = "TermDensity", FeatureValue = result.BM25Preprocessing.TermFrequencies.Count / (double)result.BM25Preprocessing.DocumentLength }
        };
    }

    private CrossEncoderHints GenerateCrossEncoderHints(DocumentChunk chunk, HybridSearchResult result)
    {
        return new CrossEncoderHints
        {
            RecommendedModel = "cross-encoder/ms-marco-MiniLM-L-6-v2",
            MaxSequenceLength = 512,
            ExpectedLatency = "50ms",
            BatchSize = 32
        };
    }

    private List<ContextualSignal> GenerateContextualSignals(DocumentChunk chunk)
    {
        return new List<ContextualSignal>
        {
            new() { SignalType = "ChunkPosition", SignalValue = chunk.Index.ToString() },
            new() { SignalType = "DocumentType", SignalValue = chunk.Metadata?.FileType ?? "Unknown" }
        };
    }

    private UserInteractionPredictions PredictUserInteractions(DocumentChunk chunk, HybridSearchResult result)
    {
        return new UserInteractionPredictions
        {
            ClickProbability = Math.Min(0.95, chunk.Quality + 0.1),
            DwellTimePrediction = chunk.Content.Length / 20.0, // seconds
            BounceRatePrediction = 1.0 - chunk.Quality,
            SatisfactionScore = chunk.Quality
        };
    }

    private List<QualityIndicator> ExtractQualityIndicators(DocumentChunk chunk, HybridSearchResult result)
    {
        return new List<QualityIndicator>
        {
            new() { IndicatorName = "Readability", Value = 0.7 },
            new() { IndicatorName = "Completeness", Value = chunk.Quality },
            new() { IndicatorName = "Coherence", Value = chunk.Props.TryGetValue("ContextualScores", out var scores) && scores is Dictionary<string, double> dict ? dict.GetValueOrDefault("SemanticCoherence", 0.5) : 0.5 }
        };
    }

    private MultiModalFeatures ExtractMultiModalFeatures(HybridSearchResult result)
    {
        return new MultiModalFeatures
        {
            HasImages = false, // Would be determined from chunk analysis
            HasTables = result.BM25Preprocessing.OriginalContent.Contains('|'),
            HasCode = IsCodeContent(result.BM25Preprocessing.OriginalContent),
            HasMath = result.BM25Preprocessing.OriginalContent.Contains('$') // LaTeX math
        };
    }

    private Dictionary<string, double> CalculateCrossFieldCorrelations(HybridSearchResult result)
    {
        var correlations = new Dictionary<string, double>();

        // Calculate correlation between different field types
        var headerTerms = result.BM25Preprocessing.FieldTokens.GetValueOrDefault("headers", new List<string>());
        var bodyTerms = result.BM25Preprocessing.ProcessedTokens;

        if (headerTerms.Any() && bodyTerms.Any())
        {
            var commonTerms = headerTerms.Intersect(bodyTerms).Count();
            var totalTerms = headerTerms.Union(bodyTerms).Count();
            correlations["header_body"] = totalTerms > 0 ? (double)commonTerms / totalTerms : 0;
        }

        return correlations;
    }

    private TemporalSignals ExtractTemporalSignals(HybridSearchResult result)
    {
        return new TemporalSignals
        {
            ProcessingTimestamp = result.ProcessingTimestamp,
            HasTimestamps = Regex.IsMatch(result.BM25Preprocessing.OriginalContent, @"\d{4}-\d{2}-\d{2}"),
            HasDates = Regex.IsMatch(result.BM25Preprocessing.OriginalContent, @"\b(?:January|February|March|April|May|June|July|August|September|October|November|December)\b"),
            TimeReferences = ExtractTimeReferences(result.BM25Preprocessing.OriginalContent)
        };
    }

    private AuthoritySignals CalculateAuthoritySignals(HybridSearchResult result)
    {
        return new AuthoritySignals
        {
            SourceAuthority = 0.8, // Would be calculated based on source metadata
            ContentAuthority = result.OriginalChunk.Quality,
            CitationCount = ExtractCitationCount(result.BM25Preprocessing.OriginalContent),
            ExpertiseIndicators = ExtractExpertiseIndicators(result.BM25Preprocessing.OriginalContent)
        };
    }

    private UserContextHints GenerateUserContextHints(HybridSearchResult result)
    {
        return new UserContextHints
        {
            SuggestedUserTypes = new List<string> { "General", "Technical" },
            ExpectedSkillLevel = result.WeightCalculationInfo.ContentTypeAdjustments.ContentType == "Technical" ? "Advanced" : "Intermediate",
            RecommendedPresentationStyle = "Structured",
            ContextualRelevanceFactors = new Dictionary<string, double>
            {
                ["domain_expertise"] = 0.7,
                ["task_alignment"] = 0.8,
                ["information_depth"] = result.OriginalChunk.Quality
            }
        };
    }

    private List<string> ExtractTimeReferences(string content)
    {
        var timePatterns = new[]
        {
            @"\b\d{1,2}:\d{2}\b", // Time
            @"\b\d{4}\b", // Year
            @"\b(?:yesterday|today|tomorrow)\b", // Relative time
            @"\b(?:morning|afternoon|evening|night)\b" // Time of day
        };

        var references = new List<string>();
        foreach (var pattern in timePatterns)
        {
            var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);
            references.AddRange(matches.Select(m => m.Value));
        }

        return references.Distinct().ToList();
    }

    private int ExtractCitationCount(string content)
    {
        // Count citation-like patterns
        var citationPatterns = new[]
        {
            @"\[\d+\]", // [1], [2], etc.
            @"\(\w+\s+\d{4}\)", // (Author 2023)
            @"et\s+al\.", // et al.
        };

        return citationPatterns.Sum(pattern => Regex.Matches(content, pattern).Count);
    }

    private List<string> ExtractExpertiseIndicators(string content)
    {
        var indicators = new List<string>();

        // Look for technical jargon
        var technicalTerms = Regex.Matches(content, @"\b[A-Z]{2,}\b"); // Acronyms
        indicators.AddRange(technicalTerms.Select(m => m.Value));

        // Look for professional language
        if (content.Contains("methodology") || content.Contains("framework") || content.Contains("implementation"))
        {
            indicators.Add("Professional Language");
        }

        return indicators.Distinct().ToList();
    }
}

// Data models and supporting classes

public class HybridSearchOptions
{
    public BM25Options BM25Options { get; set; } = new();
    public bool EnableSynonymMapping { get; set; } = true;
    public bool EnableQueryExpansion { get; set; } = true;
    public double DefaultKeywordWeight { get; set; } = 0.6;
    public double DefaultSemanticWeight { get; set; } = 0.4;
}

public class BM25Options
{
    public bool RemoveStopWords { get; set; } = true;
    public bool ApplyStemming { get; set; } = false;
    public int MinTokenLength { get; set; } = 2;
    public double K1 { get; set; } = 1.2; // Term frequency saturation parameter
    public double B { get; set; } = 0.75; // Length normalization parameter
}

public class HybridSearchResult
{
    public DocumentChunk OriginalChunk { get; set; } = null!;
    public BM25Preprocessing BM25Preprocessing { get; set; } = null!;
    public KeywordIndexMetadata KeywordIndexMetadata { get; set; } = null!;
    public WeightCalculationInfo WeightCalculationInfo { get; set; } = null!;
    public RerankingHints RerankingHints { get; set; } = null!;
    public CombinedSearchSignals CombinedSearchSignals { get; set; } = null!;
    public DateTime ProcessingTimestamp { get; set; }
}

public class BM25Preprocessing
{
    public string OriginalContent { get; set; } = string.Empty;
    public List<string> BasicTokens { get; set; } = new();
    public List<string> ProcessedTokens { get; set; } = new();
    public Dictionary<string, int> TermFrequencies { get; set; } = new();
    public int DocumentLength { get; set; }
    public int UniqueTermCount { get; set; }
    public Dictionary<string, List<string>> FieldTokens { get; set; } = new();
    public Dictionary<string, List<int>> PositionInformation { get; set; } = new();
}

public class KeywordIndexMetadata
{
    public List<InvertedIndexEntry> InvertedIndexEntries { get; set; } = new();
    public Dictionary<string, double> TermImportanceScores { get; set; } = new();
    public Dictionary<string, double> FieldBoosts { get; set; } = new();
    public List<string> QueryExpansionCandidates { get; set; } = new();
    public Dictionary<string, List<string>> SynonymMappings { get; set; } = new();
}

public class InvertedIndexEntry
{
    public string Term { get; set; } = string.Empty;
    public int TermFrequency { get; set; }
    public List<int> Positions { get; set; } = new();
    public int FirstPosition { get; set; }
    public int LastPosition { get; set; }
    public int PositionSpread { get; set; }
}

public class WeightCalculationInfo
{
    public KeywordWeightFactors KeywordWeightFactors { get; set; } = null!;
    public SemanticWeightFactors SemanticWeightFactors { get; set; } = null!;
    public ContentTypeAdjustments ContentTypeAdjustments { get; set; } = null!;
    public PositionWeights PositionWeights { get; set; } = null!;
    public FreshnessFactors FreshnessFactors { get; set; } = null!;
    public QualityMultipliers QualityMultipliers { get; set; } = null!;
    public HybridRatio RecommendedHybridRatio { get; set; } = null!;
}

public class KeywordWeightFactors
{
    public double TermFrequencyWeight { get; set; }
    public double PositionWeight { get; set; }
    public double FieldWeight { get; set; }
    public double LengthNormalizationWeight { get; set; }
    public string HighestScoringTerm { get; set; } = string.Empty;
    public double AverageTermFrequency { get; set; }
}

public class SemanticWeightFactors
{
    public double SemanticDensityWeight { get; set; }
    public double CoherenceWeight { get; set; }
    public double TopicalRelevanceWeight { get; set; }
    public double ContextualWeight { get; set; }
    public double EstimatedSemanticDensity { get; set; }
    public double CoherenceScore { get; set; }
}

public class ContentTypeAdjustments
{
    public double KeywordBoost { get; set; }
    public double SemanticBoost { get; set; }
    public string ContentType { get; set; } = string.Empty;
}

public class PositionWeights
{
    public double TitleWeight { get; set; }
    public double FirstParagraphWeight { get; set; }
    public double LastParagraphWeight { get; set; }
    public double MiddleWeight { get; set; }
    public int ChunkPosition { get; set; }
    public double RelativePosition { get; set; }
}

public class FreshnessFactors
{
    public double RecencyBoost { get; set; }
    public DateTime CreatedAt { get; set; }
    public double AgeInHours { get; set; }
    public bool IsRecent { get; set; }
}

public class QualityMultipliers
{
    public double QualityScore { get; set; }
    public double QualityBoost { get; set; }
    public bool HasHighQuality { get; set; }
    public string ConfidenceLevel { get; set; } = string.Empty;
}

public class HybridRatio
{
    public double KeywordRatio { get; set; }
    public double SemanticRatio { get; set; }
    public string RecommendedStrategy { get; set; } = string.Empty;
    public double ConfidenceScore { get; set; }
}

public class RerankingHints
{
    public List<RerankingFeature> RerankingFeatures { get; set; } = new();
    public CrossEncoderHints CrossEncoderHints { get; set; } = null!;
    public List<ContextualSignal> ContextualSignals { get; set; } = new();
    public UserInteractionPredictions UserInteractionPredictions { get; set; } = null!;
    public List<QualityIndicator> QualityIndicators { get; set; } = new();
}

public class RerankingFeature
{
    public string FeatureName { get; set; } = string.Empty;
    public double FeatureValue { get; set; }
}

public class CrossEncoderHints
{
    public string RecommendedModel { get; set; } = string.Empty;
    public int MaxSequenceLength { get; set; }
    public string ExpectedLatency { get; set; } = string.Empty;
    public int BatchSize { get; set; }
}

public class ContextualSignal
{
    public string SignalType { get; set; } = string.Empty;
    public string SignalValue { get; set; } = string.Empty;
}

public class UserInteractionPredictions
{
    public double ClickProbability { get; set; }
    public double DwellTimePrediction { get; set; }
    public double BounceRatePrediction { get; set; }
    public double SatisfactionScore { get; set; }
}

public class QualityIndicator
{
    public string IndicatorName { get; set; } = string.Empty;
    public double Value { get; set; }
}

public class CombinedSearchSignals
{
    public MultiModalFeatures MultiModalFeatures { get; set; } = null!;
    public Dictionary<string, double> CrossFieldCorrelations { get; set; } = new();
    public TemporalSignals TemporalSignals { get; set; } = null!;
    public AuthoritySignals AuthoritySignals { get; set; } = null!;
    public UserContextHints UserContextHints { get; set; } = null!;
}

public class MultiModalFeatures
{
    public bool HasImages { get; set; }
    public bool HasTables { get; set; }
    public bool HasCode { get; set; }
    public bool HasMath { get; set; }
}

public class TemporalSignals
{
    public DateTime ProcessingTimestamp { get; set; }
    public bool HasTimestamps { get; set; }
    public bool HasDates { get; set; }
    public List<string> TimeReferences { get; set; } = new();
}

public class AuthoritySignals
{
    public double SourceAuthority { get; set; }
    public double ContentAuthority { get; set; }
    public int CitationCount { get; set; }
    public List<string> ExpertiseIndicators { get; set; } = new();
}

public class UserContextHints
{
    public List<string> SuggestedUserTypes { get; set; } = new();
    public string ExpectedSkillLevel { get; set; } = string.Empty;
    public string RecommendedPresentationStyle { get; set; } = string.Empty;
    public Dictionary<string, double> ContextualRelevanceFactors { get; set; } = new();
}
