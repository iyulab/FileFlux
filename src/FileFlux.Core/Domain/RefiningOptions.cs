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
    /// Custom refining parameters
    /// </summary>
    public Dictionary<string, object> Extra { get; set; } = new();

    /// <summary>
    /// Text-level refinement preset name for FluxCurator integration.
    /// Valid values: "None", "Light", "Standard", "ForWebContent", "ForKorean", "ForPdfContent"
    /// Default: "Light" (minimal cleanup preserving structure).
    /// </summary>
    public string TextRefinementPreset { get; set; } = "Light";

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
}
