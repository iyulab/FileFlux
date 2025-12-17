namespace FileFlux.Core;

/// <summary>
/// Document refinement service interface.
/// Transforms RawContent into RefinedContent by cleaning, normalizing, and extracting structure.
/// </summary>
public interface IDocumentRefiner
{
    /// <summary>
    /// Refine extracted content to produce cleaned, structured output.
    /// </summary>
    /// <param name="raw">Raw extracted content from Stage 1</param>
    /// <param name="options">Refinement options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Refined content with structure analysis</returns>
    Task<RefinedContent> RefineAsync(
        RawContent raw,
        RefineOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refiner type identifier.
    /// </summary>
    string RefinerType { get; }

    /// <summary>
    /// Whether this refiner supports LLM-enhanced processing.
    /// </summary>
    bool SupportsLlm { get; }
}
