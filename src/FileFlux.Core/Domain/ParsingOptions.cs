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
    /// Maximum tokens for LLM calls
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Temperature for LLM calls (0.0 - 1.0)
    /// </summary>
    public float Temperature { get; set; } = 0.3f;

    /// <summary>
    /// Custom parsing parameters
    /// </summary>
    public Dictionary<string, object> Extra { get; set; } = new();
}
