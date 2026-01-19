namespace FileFlux.Core;

/// <summary>
/// Options for document refining/cleaning stage.
/// Controls document-level refinement (headers, footers, page numbers, structure).
/// Default settings are optimized for RAG pipelines with token optimization.
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
    /// Valid values: "None", "Light", "Standard", "ForWebContent", "ForPdfContent",
    /// "ForTokenOptimization", "ForAggressiveTokenOptimization"
    /// Default: "ForTokenOptimization" (RAG-optimized with token reduction).
    /// </summary>
    public string TextRefinementPreset { get; set; } = "ForTokenOptimization";

    // ========================================
    // Factory Methods for Common Scenarios
    // ========================================

    /// <summary>
    /// Default options optimized for RAG pipelines.
    /// Includes token optimization, Markdown conversion, and structure preservation.
    /// </summary>
    public static RefiningOptions Default => new();

    /// <summary>
    /// Options optimized for PDF documents.
    /// </summary>
    public static RefiningOptions ForPdfDocument => new()
    {
        TextRefinementPreset = "ForPdfContent"
    };

    /// <summary>
    /// Options optimized for web-scraped content.
    /// </summary>
    public static RefiningOptions ForWebContent => new()
    {
        RemovePageNumbers = false,
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
        RestructureHeadings = false,
        TextRefinementPreset = "None"
    };

    /// <summary>
    /// Options with image processing enabled.
    /// Extracts text from embedded images using IImageToTextService.
    /// </summary>
    public static RefiningOptions WithImageProcessing => new()
    {
        ProcessImagesToText = true
    };

    /// <summary>
    /// Options for aggressive token optimization.
    /// Maximizes token reduction with ASCII art removal, Base64 data removal, etc.
    /// </summary>
    public static RefiningOptions Aggressive => new()
    {
        TextRefinementPreset = "ForAggressiveTokenOptimization"
    };
}
