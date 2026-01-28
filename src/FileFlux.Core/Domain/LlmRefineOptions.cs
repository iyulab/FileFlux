namespace FileFlux.Core;

/// <summary>
/// Options for LLM-based document refinement.
/// Controls what improvements the LLM should make.
/// </summary>
public class LlmRefineOptions
{
    /// <summary>
    /// Enable noise removal (ads, legal notices, irrelevant content).
    /// Default: true
    /// </summary>
    public bool RemoveNoise { get; set; } = true;

    /// <summary>
    /// Restore broken sentences (PDF line breaks).
    /// Default: true
    /// </summary>
    public bool RestoreSentences { get; set; } = true;

    /// <summary>
    /// Restructure document sections (merge/split, fix heading levels).
    /// Default: true
    /// </summary>
    public bool RestructureSections { get; set; } = true;

    /// <summary>
    /// Correct OCR errors.
    /// Default: true
    /// </summary>
    public bool CorrectOcrErrors { get; set; } = true;

    /// <summary>
    /// Merge semantically duplicate content.
    /// Default: true
    /// </summary>
    public bool MergeDuplicates { get; set; } = true;

    /// <summary>
    /// Preserve original formatting where possible.
    /// Default: true
    /// </summary>
    public bool PreserveFormatting { get; set; } = true;

    /// <summary>
    /// Target language for refinement (null = auto-detect).
    /// </summary>
    public string? TargetLanguage { get; set; }

    /// <summary>
    /// Maximum tokens to use for LLM (0 = no limit).
    /// Default: 0
    /// </summary>
    public int MaxTokens { get; set; } = 0;

    /// <summary>
    /// LLM temperature (0.0 - 1.0).
    /// Lower = more deterministic.
    /// Default: 0.3
    /// </summary>
    public double Temperature { get; set; } = 0.3;

    /// <summary>
    /// Custom instructions for LLM.
    /// </summary>
    public string? CustomInstructions { get; set; }

    /// <summary>
    /// Document type hint for better refinement.
    /// </summary>
    public DocumentTypeHint DocumentType { get; set; } = DocumentTypeHint.Auto;

    /// <summary>
    /// Enable verbose logging of LLM operations.
    /// Default: false
    /// </summary>
    public bool VerboseLogging { get; set; } = false;

    // ========================================
    // Factory Methods
    // ========================================

    /// <summary>
    /// Default options with all improvements enabled.
    /// </summary>
    public static LlmRefineOptions Default => new();

    /// <summary>
    /// Conservative options - minimal changes.
    /// </summary>
    public static LlmRefineOptions Conservative => new()
    {
        RemoveNoise = false,
        RestructureSections = false,
        MergeDuplicates = false,
        Temperature = 0.1
    };

    /// <summary>
    /// Aggressive options - maximum improvements.
    /// </summary>
    public static LlmRefineOptions Aggressive => new()
    {
        RemoveNoise = true,
        RestoreSentences = true,
        RestructureSections = true,
        CorrectOcrErrors = true,
        MergeDuplicates = true,
        PreserveFormatting = false,
        Temperature = 0.5
    };

    /// <summary>
    /// Options optimized for PDF documents.
    /// </summary>
    public static LlmRefineOptions ForPdf => new()
    {
        RestoreSentences = true,
        CorrectOcrErrors = true,
        DocumentType = DocumentTypeHint.Pdf
    };

    /// <summary>
    /// Options optimized for scanned documents (OCR focus).
    /// </summary>
    public static LlmRefineOptions ForOcr => new()
    {
        CorrectOcrErrors = true,
        RestoreSentences = true,
        RemoveNoise = true,
        DocumentType = DocumentTypeHint.ScannedDocument
    };

    /// <summary>
    /// Disabled - skip LLM refinement entirely.
    /// </summary>
    public static LlmRefineOptions Disabled => new()
    {
        RemoveNoise = false,
        RestoreSentences = false,
        RestructureSections = false,
        CorrectOcrErrors = false,
        MergeDuplicates = false
    };

    /// <summary>
    /// Check if any improvement is enabled.
    /// </summary>
    public bool HasAnyImprovementEnabled =>
        RemoveNoise || RestoreSentences || RestructureSections ||
        CorrectOcrErrors || MergeDuplicates;
}

/// <summary>
/// Document type hint for LLM refinement.
/// </summary>
public enum DocumentTypeHint
{
    /// <summary>Auto-detect document type.</summary>
    Auto = 0,

    /// <summary>General document.</summary>
    General = 1,

    /// <summary>Technical documentation.</summary>
    Technical = 2,

    /// <summary>Legal document.</summary>
    Legal = 3,

    /// <summary>Academic paper.</summary>
    Academic = 4,

    /// <summary>PDF document (may have line break issues).</summary>
    Pdf = 5,

    /// <summary>Scanned document (may have OCR errors).</summary>
    ScannedDocument = 6,

    /// <summary>Web content.</summary>
    WebContent = 7,

    /// <summary>Email or correspondence.</summary>
    Email = 8,

    /// <summary>Spreadsheet data.</summary>
    Spreadsheet = 9,

    /// <summary>Presentation slides.</summary>
    Presentation = 10
}
