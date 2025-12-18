namespace FileFlux.Core;

/// <summary>
/// Options for document refining/cleaning stage.
/// Controls document-level refinement (headers, footers, page numbers, structure).
/// Text-level refinement preset can be specified via TextRefinementPreset.
/// </summary>
public class RefiningOptions
{
    /// <summary>
    /// Remove headers and footers from pages (default: true)
    /// </summary>
    public bool RemoveHeadersFooters { get; set; } = true;

    /// <summary>
    /// Remove page numbers (default: true)
    /// </summary>
    public bool RemovePageNumbers { get; set; } = true;

    /// <summary>
    /// Clean excessive whitespace (default: true)
    /// </summary>
    public bool CleanWhitespace { get; set; } = true;

    /// <summary>
    /// Restructure headings hierarchy (default: true)
    /// </summary>
    public bool RestructureHeadings { get; set; } = true;

    /// <summary>
    /// Use AI for OCR correction (default: false)
    /// </summary>
    public bool UseAIForOCRCorrection { get; set; }

    /// <summary>
    /// Use AI for image/table descriptions (default: false)
    /// </summary>
    public bool UseAIForDescriptions { get; set; }

    /// <summary>
    /// Convert content to structured Markdown format (default: true).
    /// Uses IMarkdownConverter to preserve document structure
    /// (headings, tables, lists, code blocks) in Markdown format.
    /// Set to false only when raw text output is preferred.
    /// </summary>
    public bool ConvertToMarkdown { get; set; } = true;

    /// <summary>
    /// Process embedded images to text using IImageToTextService (default: false).
    /// When enabled, extracts text from images and replaces image placeholders
    /// with the extracted content in the document.
    /// </summary>
    public bool ProcessImagesToText { get; set; }

    /// <summary>
    /// Custom refining parameters
    /// </summary>
    public Dictionary<string, object> Extra { get; set; } = new();

    /// <summary>
    /// Text-level refinement preset name for FluxCurator integration.
    /// Valid values: "None", "Light", "Standard", "ForWebContent", "ForKorean", "ForPdfContent",
    /// "ForTokenOptimization", "ForAggressiveTokenOptimization"
    /// Default: "Standard" (token optimization included per minimum-config principle).
    /// </summary>
    public string TextRefinementPreset { get; set; } = "Standard";

    // ========================================
    // Factory Methods for Common Scenarios
    // ========================================

    /// <summary>
    /// Default options for general document processing.
    /// </summary>
    public static RefiningOptions Default => new();

    /// <summary>
    /// Options optimized for Korean web content (bulletin boards, blogs).
    /// Uses FluxCurator's ForKorean preset with Korean-specific patterns.
    /// </summary>
    public static RefiningOptions ForKoreanWebContent => new()
    {
        RemoveHeadersFooters = true,
        RemovePageNumbers = true,
        CleanWhitespace = true,
        RestructureHeadings = true,
        TextRefinementPreset = "ForKorean"
    };

    /// <summary>
    /// Options optimized for PDF documents.
    /// </summary>
    public static RefiningOptions ForPdfDocument => new()
    {
        RemoveHeadersFooters = true,
        RemovePageNumbers = true,
        CleanWhitespace = true,
        RestructureHeadings = true,
        ConvertToMarkdown = true,
        TextRefinementPreset = "ForPdfContent"
    };

    /// <summary>
    /// Options optimized for web-scraped content.
    /// </summary>
    public static RefiningOptions ForWebContent => new()
    {
        RemoveHeadersFooters = true,
        RemovePageNumbers = false,
        CleanWhitespace = true,
        RestructureHeadings = false,
        TextRefinementPreset = "ForWebContent"
    };

    /// <summary>
    /// Minimal refinement - only essential cleanup.
    /// </summary>
    public static RefiningOptions Minimal => new()
    {
        RemoveHeadersFooters = false,
        RemovePageNumbers = false,
        CleanWhitespace = true,
        RestructureHeadings = false,
        TextRefinementPreset = "None"
    };

    /// <summary>
    /// Options optimized for RAG pipelines with full content transformation.
    /// Enables Markdown conversion and token optimization for better quality.
    /// </summary>
    public static RefiningOptions ForRAG => new()
    {
        RemoveHeadersFooters = true,
        RemovePageNumbers = true,
        CleanWhitespace = true,
        RestructureHeadings = true,
        ConvertToMarkdown = true,
        ProcessImagesToText = false,
        TextRefinementPreset = "ForTokenOptimization"
    };

    /// <summary>
    /// Options optimized for RAG pipelines with image processing.
    /// Enables Markdown conversion, image-to-text extraction, and token optimization.
    /// </summary>
    public static RefiningOptions ForRAGWithImages => new()
    {
        RemoveHeadersFooters = true,
        RemovePageNumbers = true,
        CleanWhitespace = true,
        RestructureHeadings = true,
        ConvertToMarkdown = true,
        ProcessImagesToText = true,
        TextRefinementPreset = "ForTokenOptimization"
    };

    /// <summary>
    /// Options for aggressive token optimization (web scraping, PDF conversion).
    /// Maximizes token reduction with ASCII art removal, Base64 data removal, etc.
    /// </summary>
    public static RefiningOptions ForAggressiveTokenOptimization => new()
    {
        RemoveHeadersFooters = true,
        RemovePageNumbers = true,
        CleanWhitespace = true,
        RestructureHeadings = true,
        ConvertToMarkdown = true,
        ProcessImagesToText = false,
        TextRefinementPreset = "ForAggressiveTokenOptimization"
    };
}
