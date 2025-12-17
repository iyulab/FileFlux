namespace FileFlux.Core;

/// <summary>
/// Document enrichment service interface.
/// Enriches DocumentChunks with LLM-generated summaries, keywords, contextual text,
/// and builds inter-chunk relationship graph.
/// </summary>
/// <remarks>
/// Stage 4 of the 4-stage pipeline: Extract → Refine → Chunk → Enrich
/// Integrates with FluxImprover services for LLM-powered enrichment.
/// </remarks>
public interface IDocumentEnricher
{
    /// <summary>
    /// Enrich chunks with summaries, keywords, and build relationship graph.
    /// </summary>
    /// <param name="chunks">Chunks from Stage 3</param>
    /// <param name="refined">Refined content for document context</param>
    /// <param name="options">Enrichment options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Enrichment result containing enhanced chunks and graph</returns>
    Task<EnrichmentResult> EnrichAsync(
        IReadOnlyList<DocumentChunk> chunks,
        RefinedContent refined,
        EnrichOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream enriched chunks as they are processed.
    /// Useful for large documents where you want progressive results.
    /// </summary>
    /// <param name="chunks">Chunks from Stage 3</param>
    /// <param name="refined">Refined content for document context</param>
    /// <param name="options">Enrichment options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of enriched chunks</returns>
    IAsyncEnumerable<EnrichedDocumentChunk> EnrichStreamAsync(
        IReadOnlyList<DocumentChunk> chunks,
        RefinedContent refined,
        EnrichOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Build relationship graph from chunks.
    /// Can be called separately after enrichment if graph is needed later.
    /// </summary>
    /// <param name="chunks">Enriched chunks</param>
    /// <param name="options">Graph building options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Document graph with nodes and edges</returns>
    Task<DocumentGraph> BuildGraphAsync(
        IReadOnlyList<EnrichedDocumentChunk> chunks,
        GraphBuildOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enricher type identifier.
    /// </summary>
    string EnricherType { get; }

    /// <summary>
    /// Whether this enricher has LLM services available.
    /// </summary>
    bool HasLlmSupport { get; }

    /// <summary>
    /// Whether contextual enrichment (Anthropic pattern) is available.
    /// </summary>
    bool SupportsContextualEnrichment { get; }
}

/// <summary>
/// Result of document enrichment stage.
/// </summary>
public class EnrichmentResult
{
    /// <summary>
    /// Enriched chunks with summaries, keywords, and contextual text.
    /// </summary>
    public required IReadOnlyList<EnrichedDocumentChunk> Chunks { get; init; }

    /// <summary>
    /// Inter-chunk relationship graph.
    /// </summary>
    public DocumentGraph? Graph { get; init; }

    /// <summary>
    /// Enrichment statistics.
    /// </summary>
    public EnrichmentStats Stats { get; init; } = new();

    /// <summary>
    /// Whether all requested enrichments were successful.
    /// </summary>
    public bool Success => Stats.FailedChunks == 0;

    /// <summary>
    /// Total chunks that were enriched.
    /// </summary>
    public int ChunkCount => Chunks.Count;
}

/// <summary>
/// Statistics from enrichment process.
/// </summary>
public class EnrichmentStats
{
    /// <summary>
    /// Number of chunks successfully enriched.
    /// </summary>
    public int EnrichedChunks { get; init; }

    /// <summary>
    /// Number of chunks that failed enrichment.
    /// </summary>
    public int FailedChunks { get; init; }

    /// <summary>
    /// Number of summaries generated.
    /// </summary>
    public int SummariesGenerated { get; init; }

    /// <summary>
    /// Number of keyword extractions performed.
    /// </summary>
    public int KeywordsExtracted { get; init; }

    /// <summary>
    /// Number of contextual texts added.
    /// </summary>
    public int ContextualTextsAdded { get; init; }

    /// <summary>
    /// Number of graph nodes created.
    /// </summary>
    public int GraphNodes { get; init; }

    /// <summary>
    /// Number of graph edges created.
    /// </summary>
    public int GraphEdges { get; init; }

    /// <summary>
    /// Total LLM calls made.
    /// </summary>
    public int LlmCalls { get; init; }

    /// <summary>
    /// Total estimated tokens used.
    /// </summary>
    public int TokensUsed { get; init; }

    /// <summary>
    /// Processing duration.
    /// </summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Enriched document chunk with LLM-generated metadata.
/// </summary>
public class EnrichedDocumentChunk
{
    /// <summary>
    /// Original chunk data.
    /// </summary>
    public required DocumentChunk Chunk { get; init; }

    /// <summary>
    /// LLM-generated summary of chunk content.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Extracted keywords with relevance scores.
    /// </summary>
    public IReadOnlyDictionary<string, double>? Keywords { get; init; }

    /// <summary>
    /// Contextual text from Anthropic's Contextual Retrieval pattern.
    /// Prepended to chunk text for better retrieval.
    /// </summary>
    public string? ContextualText { get; init; }

    /// <summary>
    /// List of entity mentions found in chunk.
    /// </summary>
    public IReadOnlyList<string>? Entities { get; init; }

    /// <summary>
    /// Chunk topics/categories.
    /// </summary>
    public IReadOnlyList<string>? Topics { get; init; }

    /// <summary>
    /// Whether this chunk was successfully enriched.
    /// </summary>
    public bool IsEnriched => Summary != null || Keywords != null || ContextualText != null;

    /// <summary>
    /// Text to use for embedding/search (contextual + original).
    /// </summary>
    public string SearchableText =>
        string.IsNullOrEmpty(ContextualText)
            ? Chunk.Content
            : $"{ContextualText}\n\n{Chunk.Content}";

    /// <summary>
    /// Get keyword list (without scores).
    /// </summary>
    public IReadOnlyList<string> KeywordList =>
        Keywords?.Keys.ToList() ?? [];
}

/// <summary>
/// Options for building the document graph.
/// </summary>
public class GraphBuildOptions
{
    /// <summary>
    /// Include sequential edges (chunk order).
    /// </summary>
    public bool IncludeSequentialEdges { get; init; } = true;

    /// <summary>
    /// Include hierarchical edges (section structure).
    /// </summary>
    public bool IncludeHierarchicalEdges { get; init; } = true;

    /// <summary>
    /// Use LLM to discover semantic relationships.
    /// </summary>
    public bool DiscoverSemanticRelationships { get; init; } = true;

    /// <summary>
    /// Minimum confidence for relationship edges.
    /// </summary>
    public double MinRelationshipConfidence { get; init; } = 0.5;

    /// <summary>
    /// Maximum edges per chunk (to prevent graph explosion).
    /// </summary>
    public int MaxEdgesPerChunk { get; init; } = 10;

    /// <summary>
    /// Include shared entity edges.
    /// </summary>
    public bool IncludeSharedEntityEdges { get; init; } = true;

    /// <summary>
    /// Include cross-reference edges.
    /// </summary>
    public bool IncludeReferenceEdges { get; init; } = true;

    /// <summary>
    /// Default graph build options.
    /// </summary>
    public static GraphBuildOptions Default { get; } = new();

    /// <summary>
    /// Minimal graph (structure only, no LLM).
    /// </summary>
    public static GraphBuildOptions Minimal { get; } = new()
    {
        IncludeSequentialEdges = true,
        IncludeHierarchicalEdges = true,
        DiscoverSemanticRelationships = false,
        IncludeSharedEntityEdges = false,
        IncludeReferenceEdges = false
    };

    /// <summary>
    /// Full graph with all relationship types.
    /// </summary>
    public static GraphBuildOptions Full { get; } = new()
    {
        IncludeSequentialEdges = true,
        IncludeHierarchicalEdges = true,
        DiscoverSemanticRelationships = true,
        IncludeSharedEntityEdges = true,
        IncludeReferenceEdges = true,
        MinRelationshipConfidence = 0.3,
        MaxEdgesPerChunk = 20
    };
}
