namespace FileFlux.Core;

/// <summary>
/// RAG-optimized chunking configuration options (minimal settings for best quality)
/// </summary>
public class ChunkingOptions
{
    /// <summary>
    /// Chunking strategy (default: Auto - automatically selects optimal strategy after document analysis)
    /// </summary>
    public string Strategy { get; set; } = ChunkingStrategies.Auto;

    /// <summary>
    /// Maximum chunk size (default: 1024 tokens - RAG optimal size)
    /// </summary>
    public int MaxChunkSize { get; set; } = 1024;

    /// <summary>
    /// Overlap size between chunks (default: 128 tokens - context preservation)
    /// </summary>
    public int OverlapSize { get; set; } = 128;

    /// <summary>
    /// Minimum chunk size (default: 200 chars - ensures meaningful search units)
    /// Chunks smaller than this are auto-merged with adjacent chunks
    /// </summary>
    public int MinChunkSize { get; set; } = 200;

    /// <summary>
    /// Preserve paragraph boundaries (for semantic chunking)
    /// </summary>
    public bool PreserveParagraphs { get; set; } = true;

    /// <summary>
    /// Preserve sentence boundaries (for semantic chunking)
    /// </summary>
    public bool PreserveSentences { get; set; } = true;

    /// <summary>
    /// Maximum heading level for hierarchical chunking (1-6)
    /// </summary>
    public int MaxHeadingLevel { get; set; } = 3;

    /// <summary>
    /// Custom properties for advanced settings
    /// </summary>
    public Dictionary<string, object> CustomProperties { get; } = new();

    /// <summary>
    /// Strategy-specific options (used by Auto strategy)
    /// </summary>
    public Dictionary<string, object> StrategyOptions { get; } = new();

    /// <summary>
    /// Separate document header (title, copyright, etc.) from body content
    /// </summary>
    public bool SeparateDocumentHeader { get; set; } = true;

    /// <summary>
    /// Maximum number of paragraphs to consider as header (default: 5)
    /// </summary>
    public int MaxHeaderParagraphs { get; set; } = 5;

    /// <summary>
    /// Maximum length of header paragraphs (default: 200 chars)
    /// </summary>
    public int MaxHeaderParagraphLength { get; set; } = 200;

    /// <summary>
    /// Recognize Korean section markers (default: true)
    /// </summary>
    public bool RecognizeKoreanSectionMarkers { get; set; } = true;

    /// <summary>
    /// Remove duplicate content in overlap regions (default: true)
    /// </summary>
    public bool DeduplicateOverlaps { get; set; } = true;

    /// <summary>
    /// ISO 639-1 language code for text segmentation
    /// </summary>
    public string? LanguageCode { get; set; } = "auto";

    /// <summary>
    /// Enable automatic post-processing to balance chunk sizes (default: true)
    /// When enabled, undersized chunks are merged and oversized chunks are split.
    /// This helps achieve consistent chunk sizes for better RAG retrieval quality.
    /// </summary>
    public bool EnableChunkBalancing { get; set; } = true;

    /// <summary>
    /// Enable conditional enrichment based on chunk quality assessment (default: false)
    /// When enabled, chunks are pre-assessed and may skip unnecessary LLM enrichment operations.
    /// This can reduce LLM API costs by 30-50% for well-structured documents.
    /// </summary>
    public bool EnableConditionalEnrichment { get; set; } = false;

    /// <summary>
    /// Quality score threshold above which enrichment is skipped (0.0 - 1.0)
    /// Chunks with OverallScore >= this value skip summarization and keyword extraction.
    /// Only used when EnableConditionalEnrichment is true.
    /// Default: 0.8
    /// </summary>
    public float ConditionalEnrichmentThreshold { get; set; } = 0.8f;

    /// <summary>
    /// Minimum content length (characters) to enable summarization (default: 500)
    /// Chunks shorter than this skip summarization even if otherwise recommended.
    /// Only used when EnableConditionalEnrichment is true.
    /// </summary>
    public int MinSummarizationLength { get; set; } = 500;

    /// <summary>
    /// Refining options to apply before chunking (default: enabled with Markdown conversion).
    /// The Refine stage is executed between Parse and Chunk stages.
    /// Set to null to disable refining, or use RefiningOptions.ForRAGWithImages for image processing.
    /// </summary>
    public RefiningOptions? RefiningOptions { get; set; } = new();
}

/// <summary>
/// Chunking strategies (delegated to FluxCurator)
/// </summary>
public static class ChunkingStrategies
{
    /// <summary>Auto-select best strategy based on document analysis</summary>
    public const string Auto = nameof(Auto);

    /// <summary>Sentence-based chunking</summary>
    public const string Sentence = nameof(Sentence);

    /// <summary>Paragraph-based chunking</summary>
    public const string Paragraph = nameof(Paragraph);

    /// <summary>Token-based chunking (fixed size)</summary>
    public const string Token = nameof(Token);

    /// <summary>Semantic chunking with embeddings (requires embedder)</summary>
    public const string Semantic = nameof(Semantic);

    /// <summary>Hierarchical structure-aware chunking</summary>
    public const string Hierarchical = nameof(Hierarchical);

    /// <summary>All available strategies</summary>
    public static readonly string[] All = [Auto, Sentence, Paragraph, Token, Semantic, Hierarchical];
}
