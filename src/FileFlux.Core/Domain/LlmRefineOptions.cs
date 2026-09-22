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
    /// Correct OCR errors. This is the AI OCR-correction switch — the former <c>RefiningOptions.UseAIForOCRCorrection</c>
    /// was read by nothing and was removed in 0.25.0.
    /// Default: true
    /// </summary>
    public bool CorrectOcrErrors { get; set; } = true;

    /// <summary>
    /// Merge semantically duplicate content.
    /// Default: true
    /// </summary>
    public bool MergeDuplicates { get; set; } = true;

    /// <summary>
    /// Preserve original formatting where possible (default: true). Becomes a rule in the noise-removal, OCR-correction
    /// and restructuring prompts: keep line breaks, spacing and markdown markers exactly — or, when false, allow them to
    /// be normalized for readability. Before 0.24.2 this member was read by nothing.
    /// </summary>
    public bool PreserveFormatting { get; set; } = true;

    /// <summary>
    /// Language the refined text is written in (null = the text's own language). Becomes a rule in every refinement
    /// prompt since 0.25.0 — before that the library's own refiner read it by nothing, while the FluxIndex
    /// <c>ILlmRefiner</c> implementation already did.
    /// </summary>
    public string? TargetLanguage { get; set; }

    /// <summary>
    /// Maximum tokens each refinement call may generate; null (or 0) = the analysis service's own default.
    /// Reaches <c>IDocumentAnalysisService.GenerateAsync(prompt, GenerationSettings, ct)</c> on every call the refiner
    /// makes (since 0.25.0 — before that it was read by nothing and the service's literal, 1000 for the
    /// OpenAI-compatible service, applied). Same shape as <c>ParsingOptions.MaxTokens</c>.
    /// Default: null
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// LLM temperature (0.0 - 1.0) for every refinement call. Lower = more deterministic.
    /// Reaches the analysis service since 0.25.0 — before that it was read by nothing and the service's literal
    /// (0.7) applied, so this default now makes refinement more deterministic than it effectively was.
    /// Default: 0.3
    /// </summary>
    public double Temperature { get; set; } = 0.3;

    /// <summary>
    /// Extra instructions appended as a rule to every refinement prompt (since 0.25.0; see <see cref="TargetLanguage"/>).
    /// </summary>
    public string? CustomInstructions { get; set; }

    /// <summary>
    /// Document type hint. Anything but <see cref="DocumentTypeHint.Auto"/> becomes a rule in every refinement prompt
    /// (since 0.25.0; see <see cref="TargetLanguage"/>). The <c>ForPdf</c>/<c>ForOcr</c> presets set it.
    /// </summary>
    public DocumentTypeHint DocumentType { get; set; } = DocumentTypeHint.Auto;

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
