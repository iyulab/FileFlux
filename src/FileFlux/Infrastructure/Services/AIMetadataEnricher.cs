using FileFlux.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;

namespace FileFlux.Infrastructure.Services;

/// <summary>
/// AI-powered metadata enricher with fallback support.
/// Implements IMetadataEnricher with caching and error resilience.
/// </summary>
public partial class AIMetadataEnricher : IMetadataEnricher
{
    private readonly IDocumentAnalysisService? _llmService;
    private readonly RuleBasedMetadataExtractor _fallbackExtractor;
    private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;
    private readonly ILogger<AIMetadataEnricher> _logger;

    private static readonly MemoryCacheEntryOptions _cacheOptions = new MemoryCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
        Size = 1
    };

    public AIMetadataEnricher(
        RuleBasedMetadataExtractor fallbackExtractor,
        Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
        IDocumentAnalysisService? llmService = null,
        ILogger<AIMetadataEnricher>? logger = null)
    {
        _fallbackExtractor = fallbackExtractor ?? throw new ArgumentNullException(nameof(fallbackExtractor));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _llmService = llmService; // Optional
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AIMetadataEnricher>.Instance;
    }

    /// <summary>
    /// Extract metadata with AI (falls back to rule-based if AI unavailable).
    /// </summary>
    public async Task<IDictionary<string, object>> EnrichAsync(
        string content,
        MetadataSchema schema,
        MetadataEnrichmentOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MetadataEnrichmentOptions();

        // 1. No AI service → rule-based extraction
        if (_llmService == null)
        {
            LogAiServiceNotAvailable(_logger);
            return await _fallbackExtractor.ExtractAsync(content, schema, cancellationToken);
        }

        var retries = 0;
        Exception? lastException = null;

        while (retries <= options.MaxRetries)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(options.TimeoutMs);

                // 2. Extract with AI
                var metadata = await ExtractWithAIAsync(content, schema, options, cts.Token);

                // 3. Validate confidence
                var confidence = Convert.ToDouble(metadata["confidence"], CultureInfo.InvariantCulture);
                if (confidence < options.MinConfidence)
                {
                    LogLowConfidence(_logger, confidence, options.MinConfidence);

                    // 4. Merge with rule-based
                    var fallback = await _fallbackExtractor.ExtractAsync(content, schema, cancellationToken);
                    return MergeMetadata(metadata, fallback);
                }

                return metadata;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                LogEnrichmentTimeout(_logger, retries + 1, options.MaxRetries + 1);
                lastException = new TimeoutException("Metadata enrichment timed out");
            }
            catch (Exception ex)
            {
                LogEnrichmentAttemptFailed(_logger, ex, retries + 1, options.MaxRetries + 1);
                lastException = ex;
            }

            retries++;
            if (retries <= options.MaxRetries)
            {
                await Task.Delay(options.RetryDelayMs * retries, cancellationToken); // Exponential backoff
            }
        }

        // 5. All retries failed → fallback
        LogAllAttemptsFailed(_logger, lastException);

        if (options.ContinueOnEnrichmentFailure)
        {
            return await _fallbackExtractor.ExtractAsync(content, schema, cancellationToken);
        }
        else
        {
            throw new InvalidOperationException("Metadata enrichment failed after all retries", lastException);
        }
    }

    /// <summary>
    /// Extract metadata with caching.
    /// </summary>
    public async Task<IDictionary<string, object>> EnrichWithCacheAsync(
        string content,
        string cacheKey,
        MetadataSchema schema,
        MetadataEnrichmentOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Check cache
        if (_cache.TryGetValue(cacheKey, out object? cachedObj) && cachedObj is IDictionary<string, object> cached)
        {
            LogMetadataCacheHit(_logger, cacheKey);
            return cached;
        }

        // 2. Cache miss → extract
        LogMetadataCacheMiss(_logger, cacheKey);
        var metadata = await EnrichAsync(content, schema, options, cancellationToken);

        // 3. Store in cache
        _cache.Set(cacheKey, metadata, _cacheOptions);

        return metadata;
    }

    /// <summary>
    /// Batch metadata extraction (token optimized).
    /// </summary>
    public async Task<IReadOnlyList<EnrichedMetadataResult>> EnrichBatchAsync(
        IReadOnlyList<BatchMetadataRequest> requests,
        MetadataSchema schema,
        MetadataEnrichmentOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<EnrichedMetadataResult>();

        // 1. Check cache for all requests
        var uncachedRequests = new List<BatchMetadataRequest>();
        foreach (var request in requests)
        {
            if (request.CacheKey != null &&
                _cache.TryGetValue(request.CacheKey, out object? cachedObj) &&
                cachedObj is IDictionary<string, object> cached)
            {
                var confidence = cached.TryGetValue("confidence", out var confVal) ? Convert.ToDouble(confVal, CultureInfo.InvariantCulture) : 0.0;
                var method = cached.TryGetValue("extractionMethod", out var methodVal) ? methodVal?.ToString() : "cached";

                results.Add(new EnrichedMetadataResult
                {
                    DocumentId = request.DocumentId,
                    Metadata = cached,
                    Confidence = confidence,
                    FromCache = true,
                    ExtractionMethod = method ?? "cached"
                });
            }
            else
            {
                uncachedRequests.Add(request);
            }
        }

        if (uncachedRequests.Count == 0)
        {
            LogAllRequestsFromCache(_logger, requests.Count);
            return results;
        }

        LogProcessingRequests(_logger, uncachedRequests.Count, requests.Count);

        // 2. Process uncached requests individually
        // TODO: Implement true batch processing with single LLM call
        foreach (var request in uncachedRequests)
        {
            try
            {
                var metadata = await EnrichAsync(request.Content, schema, options, cancellationToken);

                var confidence = metadata.TryGetValue("confidence", out var confVal) ? Convert.ToDouble(confVal, CultureInfo.InvariantCulture) : 0.0;
                var method = metadata.TryGetValue("extractionMethod", out var methodVal) ? methodVal?.ToString() : "ai";

                results.Add(new EnrichedMetadataResult
                {
                    DocumentId = request.DocumentId,
                    Metadata = metadata,
                    Confidence = confidence,
                    FromCache = false,
                    ExtractionMethod = method ?? "ai"
                });

                // Cache result
                if (request.CacheKey != null)
                {
                    _cache.Set(request.CacheKey, metadata, _cacheOptions);
                }
            }
            catch (Exception ex)
            {
                LogDocumentProcessingFailed(_logger, ex, request.DocumentId);

                // Add empty result on failure
                results.Add(new EnrichedMetadataResult
                {
                    DocumentId = request.DocumentId,
                    Metadata = new Dictionary<string, object>
                    {
                        ["error"] = ex.Message,
                        ["extractionMethod"] = "failed"
                    },
                    Confidence = 0.0,
                    FromCache = false,
                    ExtractionMethod = "failed"
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Generate cache key from file path and schema.
    /// </summary>
    public string GenerateCacheKey(string filePath, MetadataSchema schema)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = sha256.ComputeHash(stream);
        var fileHash = Convert.ToBase64String(hashBytes);

        return $"metadata:{schema}:{fileHash}";
    }

    /// <summary>
    /// Extract metadata using AI service.
    /// </summary>
    private async Task<Dictionary<string, object>> ExtractWithAIAsync(
        string content,
        MetadataSchema schema,
        MetadataEnrichmentOptions options,
        CancellationToken cancellationToken)
    {
        // Truncate content based on strategy
        var truncated = TruncateContent(content, options.ExtractionStrategy);

        // Build prompt
        var systemPrompt = options.CustomPrompt ?? GetSchemaPrompt(schema);
        var userMessage = $"Extract metadata from this document:\n\n{truncated}";

        // Call LLM
        LogCallingLlm(_logger, schema, truncated.Length);

        var response = await _llmService!.GenerateAsync(
            systemPrompt + "\n\n" + userMessage,
            cancellationToken);

        // Parse JSON response
        var metadata = ParseLLMResponse(response);
        metadata["extractionMethod"] = "ai";

        return metadata;
    }

    /// <summary>
    /// Truncate content based on extraction strategy.
    /// </summary>
    private static string TruncateContent(string content, MetadataExtractionStrategy strategy)
    {
        var maxChars = strategy switch
        {
            MetadataExtractionStrategy.Fast => 2000,
            MetadataExtractionStrategy.Smart => 4000,
            MetadataExtractionStrategy.Deep => 8000,
            _ => 4000
        };

        if (content.Length <= maxChars)
            return content;

        return string.Concat(content.AsSpan(0, maxChars), "\n\n[... content truncated for metadata extraction ...]");
    }

    /// <summary>
    /// Get system prompt for schema.
    /// </summary>
    private static string GetSchemaPrompt(MetadataSchema schema)
    {
        return schema switch
        {
            MetadataSchema.General => GetGeneralPrompt(),
            MetadataSchema.ProductManual => GetProductManualPrompt(),
            MetadataSchema.TechnicalDoc => GetTechnicalDocPrompt(),
            MetadataSchema.Custom => GetGeneralPrompt(),
            _ => GetGeneralPrompt()
        };
    }

    private static string GetGeneralPrompt() => @"You are a document metadata extraction specialist.

Extract the following from the document:
- topics: Main topics (3-5 items)
- keywords: Important searchable terms (5-10 items)
- description: One-sentence summary (max 200 chars)
- documentType: manual | guide | tutorial | reference | article | note
- language: Primary language code (en, ko, ja, zh, etc.)
- categories: Document categories (if applicable)

Return JSON with confidence scores (0.0-1.0) for each field.

Example:
{
  ""topics"": [""JavaScript"", ""Async Programming""],
  ""keywords"": [""async"", ""await"", ""promises"", ""callbacks""],
  ""description"": ""Comprehensive guide to JavaScript asynchronous programming patterns"",
  ""documentType"": ""tutorial"",
  ""language"": ""en"",
  ""categories"": [""Programming"", ""JavaScript""],
  ""confidence"": 0.91
}";

    private static string GetProductManualPrompt() => @"You are a product manual metadata specialist.

Extract product information:
REQUIRED:
- productName: Full product name
- company: Manufacturer/company name
- version: Product or software version
- topics: Main topics covered
- keywords: Searchable product terms

OPTIONAL:
- releaseDate: Release or publication date
- model: Product model number
- categories: Product categories

Also extract: description, documentType, language

Return JSON with confidence score.

Example:
{
  ""productName"": ""iPhone 15 Pro"",
  ""company"": ""Apple"",
  ""version"": ""iOS 17.2"",
  ""model"": ""A2848"",
  ""releaseDate"": ""2023-09-22"",
  ""topics"": [""Camera Features"", ""Battery Management""],
  ""keywords"": [""iphone"", ""pro"", ""camera"", ""battery""],
  ""description"": ""User manual for iPhone 15 Pro smartphone"",
  ""documentType"": ""manual"",
  ""language"": ""en"",
  ""confidence"": 0.93
}";

    private static string GetTechnicalDocPrompt() => @"You are a technical documentation analyzer.

Extract technical information:
REQUIRED:
- topics: Technical topics covered (3-5)
- libraries: Libraries/packages mentioned (with version if available)
- frameworks: Frameworks used
- technologies: Technologies/languages
- keywords: Technical searchable terms (5-10)
- description: Technical summary

OPTIONAL:
- categories: Technology categories

Also extract: documentType, language

Return JSON with confidence score.

Example:
{
  ""topics"": [""React Hooks"", ""State Management""],
  ""libraries"": [""react@18.2.0"", ""@tanstack/react-query@4.0.0""],
  ""frameworks"": [""React""],
  ""technologies"": [""JavaScript"", ""TypeScript""],
  ""keywords"": [""hooks"", ""useState"", ""useEffect""],
  ""description"": ""Advanced guide to React Hooks patterns"",
  ""documentType"": ""tutorial"",
  ""language"": ""en"",
  ""confidence"": 0.93
}";

    /// <summary>
    /// Parse LLM JSON response.
    /// </summary>
    private Dictionary<string, object> ParseLLMResponse(string response)
    {
        try
        {
            // Extract JSON block
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}') + 1;

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonString = response.Substring(jsonStart, jsonEnd - jsonStart);
                var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonString);

                if (parsed == null)
                    throw new InvalidOperationException("Failed to parse JSON");

                // Convert JsonElement to object
                var metadata = new Dictionary<string, object>();
                foreach (var (key, value) in parsed)
                {
                    metadata[key] = value.ValueKind switch
                    {
                        JsonValueKind.String => value.GetString() ?? "",
                        JsonValueKind.Number => value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Array => value.EnumerateArray()
                            .Select(e => e.GetString() ?? "")
                            .Where(s => !string.IsNullOrEmpty(s))
                            .ToArray(),
                        _ => value.ToString()
                    };
                }

                return metadata;
            }
        }
        catch (Exception ex)
        {
            LogParseLlmResponseFailed(_logger, ex);
        }

        // Parsing failed → return minimal metadata
        return new Dictionary<string, object>
        {
            ["confidence"] = 0.5,
            ["extractionMethod"] = "ai-parse-failed"
        };
    }

    /// <summary>
    /// Merge AI and rule-based metadata (hybrid mode).
    /// </summary>
    private Dictionary<string, object> MergeMetadata(
        Dictionary<string, object> aiMetadata,
        IDictionary<string, object> ruleMetadata)
    {
        var merged = new Dictionary<string, object>(aiMetadata);

        // Add rule-based fields if missing in AI
        foreach (var (key, value) in ruleMetadata)
        {
            if (!merged.TryGetValue(key, out var existing) || existing == null)
            {
                merged[key] = value;
            }
        }

        // Update confidence (average of both)
        var aiConf = aiMetadata.TryGetValue("confidence", out var aiConfVal) ? Convert.ToDouble(aiConfVal, CultureInfo.InvariantCulture) : 0.0;
        var ruleConf = ruleMetadata.TryGetValue("confidence", out var ruleConfVal) ? Convert.ToDouble(ruleConfVal, CultureInfo.InvariantCulture) : 0.0;
        merged["confidence"] = (aiConf + ruleConf) / 2.0;
        merged["extractionMethod"] = "hybrid";

        LogMergedMetadata(_logger, aiConf, ruleConf);

        return merged;
    }

    #region LoggerMessage

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI service not available, using rule-based extraction")]
    private static partial void LogAiServiceNotAvailable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Low confidence {Confidence} < {MinConfidence}, merging with rule-based")]
    private static partial void LogLowConfidence(ILogger logger, double confidence, double minConfidence);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Metadata enrichment timeout (attempt {Attempt}/{Max})")]
    private static partial void LogEnrichmentTimeout(ILogger logger, int attempt, int max);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Metadata enrichment failed (attempt {Attempt}/{Max})")]
    private static partial void LogEnrichmentAttemptFailed(ILogger logger, Exception ex, int attempt, int max);

    [LoggerMessage(Level = LogLevel.Error, Message = "All AI extraction attempts failed, using rule-based fallback")]
    private static partial void LogAllAttemptsFailed(ILogger logger, Exception? ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Metadata cache hit for {CacheKey}")]
    private static partial void LogMetadataCacheHit(ILogger logger, string cacheKey);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Metadata cache miss for {CacheKey}")]
    private static partial void LogMetadataCacheMiss(ILogger logger, string cacheKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "All {Count} requests served from cache")]
    private static partial void LogAllRequestsFromCache(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processing {Uncached}/{Total} requests (rest from cache)")]
    private static partial void LogProcessingRequests(ILogger logger, int uncached, int total);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process document {DocumentId}")]
    private static partial void LogDocumentProcessingFailed(ILogger logger, Exception ex, string documentId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Calling LLM for metadata extraction (schema: {Schema}, chars: {Length})")]
    private static partial void LogCallingLlm(ILogger logger, MetadataSchema schema, int length);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to parse LLM response, using fallback")]
    private static partial void LogParseLlmResponseFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Merged AI (confidence: {AI}) and rule-based (confidence: {Rule}) metadata")]
    private static partial void LogMergedMetadata(ILogger logger, double ai, double rule);

    #endregion
}
