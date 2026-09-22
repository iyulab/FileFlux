namespace FileFlux.Core;

/// <summary>
/// Options for document parsing stage
/// </summary>
public class ParsingOptions
{
    /// <summary>
    /// Use LLM for parsing (default: true)
    /// </summary>
    public bool UseLlm { get; set; } = true;

    /// <summary>
    /// LLM model to use (null = use default)
    /// </summary>
    public string? LlmModel { get; set; }

    /// <summary>
    /// Maximum tokens the LLM structuring call may generate; null = the analysis service's own default.
    /// <c>DocumentProcessor</c> copies it into <c>DocumentParsingOptions.MaxTokens</c>, which reaches
    /// <c>IDocumentAnalysisService.GenerateAsync(prompt, GenerationSettings, ct)</c> (since 0.25.0 — before that it was
    /// read by nothing).
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Temperature for the LLM structuring call (0.0 - 1.0). Copied into <c>DocumentParsingOptions.Temperature</c> by
    /// <c>DocumentProcessor</c> and passed to the analysis service since 0.25.0 — before that it was read by nothing and
    /// the service's literal (0.7) applied.
    /// Default: 0.3
    /// </summary>
    public float Temperature { get; set; } = 0.3f;

    /// <summary>
    /// Custom parsing parameters
    /// </summary>
    public Dictionary<string, object> Extra { get; set; } = new();
}
