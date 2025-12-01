namespace FileFlux.Core;

/// <summary>
/// Options for document refining/cleaning stage
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
}
