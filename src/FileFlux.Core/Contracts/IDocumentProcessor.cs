namespace FileFlux.Core;

/// <summary>
/// Stateful document processor for 4-stage pipeline.
/// Each instance processes a single document.
/// </summary>
/// <remarks>
/// Pipeline stages:
/// 1. Extract: File → RawContent
/// 2. Refine: RawContent → RefinedContent (with StructuredElements)
/// 3. Chunk: RefinedContent → DocumentChunks
/// 4. Enrich: Chunks → Graph (LLM-powered, optional)
///
/// Usage:
/// <code>
/// await using var processor = factory.Create(filePath);
/// await processor.ExtractAsync();
/// await processor.RefineAsync();
/// await processor.ChunkAsync();
/// await processor.EnrichAsync(); // optional
/// var result = processor.Result;
/// </code>
/// </remarks>
public interface IDocumentProcessor : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Processing result containing all stage outputs.
    /// </summary>
    ProcessingResult Result { get; }

    /// <summary>
    /// Source file path.
    /// </summary>
    string FilePath { get; }

    /// <summary>
    /// Current processing state.
    /// </summary>
    ProcessorState State { get; }

    // ========================================
    // Stage 1: Extract (File → RawContent)
    // ========================================

    /// <summary>
    /// Extract raw content from document.
    /// Populates Result.Raw.
    /// </summary>
    Task ExtractAsync(CancellationToken cancellationToken = default);

    // ========================================
    // Stage 2: Refine (RawContent → RefinedContent)
    // ========================================

    /// <summary>
    /// Refine raw content with structure analysis.
    /// Extracts structured elements (tables, code, lists).
    /// Requires Extract to be completed first (auto-runs if needed).
    /// Populates Result.Refined.
    /// </summary>
    Task RefineAsync(
        RefineOptions? options = null,
        CancellationToken cancellationToken = default);

    // ========================================
    // Stage 3: Chunk (RefinedContent → Chunks)
    // ========================================

    /// <summary>
    /// Create document chunks from refined content.
    /// Requires Refine to be completed first (auto-runs if needed).
    /// Populates Result.Chunks.
    /// </summary>
    Task ChunkAsync(
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream document chunks for large documents.
    /// Requires Refine to be completed first (auto-runs if needed).
    /// Populates Result.Chunks after enumeration completes.
    /// </summary>
    IAsyncEnumerable<DocumentChunk> ChunkStreamAsync(
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);

    // ========================================
    // Stage 4: Enrich (Chunks → Graph)
    // ========================================

    /// <summary>
    /// Enrich chunks with LLM-powered analysis and build relationship graph.
    /// Requires Chunk to be completed first (auto-runs if needed).
    /// Populates Result.Graph.
    /// </summary>
    /// <remarks>
    /// This stage is optional and requires LLM services.
    /// Skip if no enrichment is needed or LLM is unavailable.
    /// </remarks>
    Task EnrichAsync(
        EnrichOptions? options = null,
        CancellationToken cancellationToken = default);

    // ========================================
    // Convenience Methods
    // ========================================

    /// <summary>
    /// Run complete pipeline (Extract → Refine → Chunk).
    /// Does not include Enrich stage (call separately if needed).
    /// </summary>
    Task ProcessAsync(
        ProcessingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream chunks through complete pipeline.
    /// Does not include Enrich stage (call separately if needed).
    /// </summary>
    IAsyncEnumerable<DocumentChunk> ProcessStreamAsync(
        ProcessingOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Processor state enumeration.
/// </summary>
public enum ProcessorState
{
    /// <summary>
    /// Initial state, no processing done.
    /// </summary>
    Created,

    /// <summary>
    /// Extract stage completed.
    /// </summary>
    Extracted,

    /// <summary>
    /// Refine stage completed.
    /// </summary>
    Refined,

    /// <summary>
    /// Chunk stage completed.
    /// </summary>
    Chunked,

    /// <summary>
    /// Enrich stage completed (full processing done).
    /// </summary>
    Enriched,

    /// <summary>
    /// Processing failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Processor disposed.
    /// </summary>
    Disposed
}

/// <summary>
/// Factory for creating document processors.
/// </summary>
public interface IDocumentProcessorFactory
{
    /// <summary>
    /// Create processor for file path.
    /// </summary>
    IDocumentProcessor Create(string filePath);

    /// <summary>
    /// Create processor for stream with explicit format.
    /// </summary>
    IDocumentProcessor Create(Stream stream, string extension);

    /// <summary>
    /// Create processor for byte array with explicit format.
    /// </summary>
    IDocumentProcessor Create(byte[] content, string extension, string? fileName = null);
}

/// <summary>
/// Combined processing options for full pipeline.
/// </summary>
public class ProcessingOptions
{
    /// <summary>
    /// Options for Refine stage.
    /// </summary>
    public RefineOptions? Refine { get; init; }

    /// <summary>
    /// Options for Chunk stage.
    /// </summary>
    public ChunkingOptions? Chunking { get; init; }

    /// <summary>
    /// Whether to include Enrich stage in processing.
    /// Default is false (Enrich must be called separately).
    /// </summary>
    public bool IncludeEnrich { get; init; }

    /// <summary>
    /// Options for Enrich stage (if IncludeEnrich is true).
    /// </summary>
    public EnrichOptions? Enrich { get; init; }

    /// <summary>
    /// Default processing options.
    /// </summary>
    public static ProcessingOptions Default { get; } = new();
}

/// <summary>
/// Options for document refinement (Stage 2).
/// Transforms RawContent into RefinedContent by cleaning, normalizing, and converting to markdown.
/// </summary>
public class RefineOptions
{
    /// <summary>
    /// Use LLM for enhanced processing (set internally when ITextCompletionService is injected).
    /// When LLM is available, it's always used for table/image processing.
    /// </summary>
    public bool UseLlm { get; set; }

    /// <summary>
    /// Extract structured elements (tables, code, lists) into StructuredElement objects.
    /// </summary>
    public bool ExtractStructures { get; set; } = true;

    /// <summary>
    /// Clean noise (headers, footers, page numbers).
    /// </summary>
    public bool CleanNoise { get; set; } = true;

    /// <summary>
    /// Convert tables to markdown format.
    /// Tables are converted from RawContent.Tables using rule-based or LLM approach.
    /// </summary>
    public bool ConvertTablesToMarkdown { get; set; } = true;

    /// <summary>
    /// Convert text blocks to markdown format.
    /// Blocks are converted from RawContent.Blocks with heading/list detection.
    /// </summary>
    public bool ConvertBlocksToMarkdown { get; set; } = true;

    /// <summary>
    /// Build hierarchical sections from headings.
    /// </summary>
    public bool BuildSections { get; set; } = true;

    /// <summary>
    /// Remove excessive whitespace.
    /// </summary>
    public bool NormalizeWhitespace { get; set; } = true;

    /// <summary>
    /// Normalize markdown structure (heading hierarchy, lists, annotations).
    /// Fixes issues like H1 → H5 jumps, demotes annotation-like headings to plain text.
    /// This is a format-agnostic post-processing step applied after markdown conversion.
    /// </summary>
    public bool NormalizeMarkdownStructure { get; set; } = true;

    /// <summary>
    /// Process images (generate captions with LLM if available).
    /// </summary>
    public bool ProcessImages { get; set; } = false;

    /// <summary>
    /// LLM model to use (if UseLlm is true).
    /// </summary>
    public string? LlmModel { get; set; }

    /// <summary>
    /// Maximum tokens for LLM processing per request.
    /// </summary>
    public int? MaxLlmTokens { get; set; }

    /// <summary>
    /// Default refinement options.
    /// </summary>
    public static RefineOptions Default => new();

    /// <summary>
    /// Minimal refinement (fast, rule-based only).
    /// </summary>
    public static RefineOptions Minimal => new()
    {
        ExtractStructures = false,
        BuildSections = false,
        ProcessImages = false
    };

    /// <summary>
    /// Full refinement with all features.
    /// </summary>
    public static RefineOptions Full => new()
    {
        ExtractStructures = true,
        CleanNoise = true,
        ConvertTablesToMarkdown = true,
        ConvertBlocksToMarkdown = true,
        BuildSections = true,
        NormalizeWhitespace = true,
        ProcessImages = true
    };

    /// <summary>
    /// RAG-optimized refinement.
    /// </summary>
    public static RefineOptions ForRAG => new()
    {
        UseLlm = false,
        ExtractStructures = true,
        CleanNoise = true,
        ConvertTablesToMarkdown = true,
        ConvertBlocksToMarkdown = true,
        BuildSections = true,
        NormalizeWhitespace = true
    };

    /// <summary>
    /// Full LLM-enhanced refinement.
    /// </summary>
    public static RefineOptions WithLlm => new()
    {
        UseLlm = true,
        ExtractStructures = true,
        CleanNoise = true,
        ConvertTablesToMarkdown = true,
        ConvertBlocksToMarkdown = true,
        BuildSections = true,
        NormalizeWhitespace = true
    };
}

/// <summary>
/// Options for Enrich stage.
/// </summary>
public class EnrichOptions
{
    /// <summary>
    /// Whether to generate chunk summaries.
    /// </summary>
    public bool GenerateSummaries { get; init; } = true;

    /// <summary>
    /// Whether to extract keywords from chunks.
    /// </summary>
    public bool ExtractKeywords { get; init; } = true;

    /// <summary>
    /// Whether to build inter-chunk relationship graph.
    /// </summary>
    public bool BuildGraph { get; init; } = true;

    /// <summary>
    /// Whether to add contextual text to chunks.
    /// </summary>
    public bool AddContextualText { get; init; } = true;

    /// <summary>
    /// Maximum concurrent LLM calls for enrichment.
    /// </summary>
    public int MaxConcurrency { get; init; } = 5;

    /// <summary>
    /// Default enrich options.
    /// </summary>
    public static EnrichOptions Default { get; } = new();
}
