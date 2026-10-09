namespace FileFlux.Core;

/// <summary>
/// Document metadata enrichment service interface.
/// Supports both AI-based and rule-based implementations.
/// </summary>
public interface IMetadataEnricher
{
    /// <summary>
    /// Extract metadata from document content.
    /// </summary>
    /// <param name="content">Document content to analyze</param>
    /// <param name="schema">Metadata schema to apply</param>
    /// <param name="options">Enrichment options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Extracted metadata as key-value pairs</returns>
    Task<IDictionary<string, object>> EnrichAsync(
        string content,
        MetadataSchema schema,
        MetadataEnrichmentOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extract metadata with caching support.
    /// </summary>
    /// <param name="content">Document content to analyze</param>
    /// <param name="cacheKey">Cache key naming the content; the schema and the options that change the result are added to it</param>
    /// <param name="schema">Metadata schema to apply</param>
    /// <param name="options">Enrichment options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Extracted metadata (from cache or new extraction)</returns>
    Task<IDictionary<string, object>> EnrichWithCacheAsync(
        string content,
        string cacheKey,
        MetadataSchema schema,
        MetadataEnrichmentOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extract metadata for multiple documents in batch (token optimized).
    /// </summary>
    /// <param name="requests">Batch of metadata extraction requests</param>
    /// <param name="schema">Metadata schema to apply</param>
    /// <param name="options">Enrichment options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of enriched metadata results</returns>
    Task<IReadOnlyList<EnrichedMetadataResult>> EnrichBatchAsync(
        IReadOnlyList<BatchMetadataRequest> requests,
        MetadataSchema schema,
        MetadataEnrichmentOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate cache key based on file hash.
    /// </summary>
    /// <param name="filePath">File path</param>
    /// <param name="schema">Metadata schema</param>
    /// <returns>Cache key</returns>
    string GenerateCacheKey(string filePath, MetadataSchema schema);
}

/// <summary>
/// Metadata extraction schema.
/// </summary>
public enum MetadataSchema
{
    /// <summary>
    /// General document: topics, keywords, description, documentType
    /// </summary>
    General,

    /// <summary>
    /// Product manual: product name, company, version, release date
    /// </summary>
    ProductManual,

    /// <summary>
    /// Technical documentation: topics, libraries, frameworks, keywords
    /// </summary>
    TechnicalDoc,

    /// <summary>
    /// Custom schema with user-defined prompt
    /// </summary>
    Custom
}

/// <summary>
/// How much of the text goes into the extraction prompt. The text is cut at a fixed length: the first N characters are
/// sent and the rest is left out (no sampling of later parts). <see cref="MetadataEnrichmentOptions.MaxTokens"/>, when
/// set, replaces the strategy's length.
/// </summary>
public enum MetadataExtractionStrategy
{
    /// <summary>
    /// The first 2,000 characters.
    /// </summary>
    Fast,

    /// <summary>
    /// The first 4,000 characters (default).
    /// </summary>
    Smart,

    /// <summary>
    /// The first 8,000 characters.
    /// </summary>
    Deep
}

/// <summary>
/// Metadata enrichment options.
/// </summary>
public class MetadataEnrichmentOptions
{
    /// <summary>
    /// How much of the text goes into the prompt (see <see cref="MetadataExtractionStrategy"/>).
    /// </summary>
    public MetadataExtractionStrategy ExtractionStrategy { get; set; } = MetadataExtractionStrategy.Smart;

    /// <summary>
    /// Token budget for the content sampled into the extraction prompt (null = the strategy's default:
    /// Fast 2000 / Smart 4000 / Deep 8000 characters). Converted to characters at roughly four per token — a sampling
    /// bound, not a tokenizer. Before 0.24.2 this member was read by nothing.
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Minimum confidence threshold (0.0 - 1.0). A reply whose <c>confidence</c> is below it is merged with the
    /// rule-based result. A reply without a numeric <c>confidence</c> counts as 0.5.
    /// </summary>
    public double MinConfidence { get; set; } = 0.6;

    /// <summary>
    /// Continue document processing if metadata enrichment fails.
    /// </summary>
    public bool ContinueOnEnrichmentFailure { get; set; } = true;

    /// <summary>
    /// Extra attempts after a failed or timed-out model call (0 = one call only).
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// Wait in milliseconds before the first retry; it doubles for each further retry (exponential backoff: 1 s, 2 s,
    /// 4 s ... for the default 1000).
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Timeout in milliseconds.
    /// </summary>
    public int TimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Custom extraction prompt (overrides schema).
    /// </summary>
    public string? CustomPrompt { get; set; }
}

/// <summary>
/// Batch metadata extraction request.
/// </summary>
public class BatchMetadataRequest
{
    /// <summary>
    /// Unique document identifier.
    /// </summary>
    public string DocumentId { get; set; } = "";

    /// <summary>
    /// Document content to analyze.
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// Cache key (optional, for cache lookup).
    /// </summary>
    public string? CacheKey { get; set; }
}

/// <summary>
/// Enriched metadata result.
/// </summary>
public class EnrichedMetadataResult
{
    /// <summary>
    /// Document identifier.
    /// </summary>
    public string DocumentId { get; set; } = "";

    /// <summary>
    /// Extracted metadata.
    /// </summary>
    public IDictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// Confidence score (0.0 - 1.0).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Whether result was retrieved from cache.
    /// </summary>
    public bool FromCache { get; set; }

    /// <summary>
    /// Extraction method used (AI, RuleBased, Hybrid).
    /// </summary>
    public string ExtractionMethod { get; set; } = "";
}
