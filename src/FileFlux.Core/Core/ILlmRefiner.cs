namespace FileFlux.Core;

/// <summary>
/// LLM-based document refinement service interface.
/// Transforms RefinedContent (from rule-based refiner) into LlmRefinedContent
/// by applying LLM-powered improvements.
/// </summary>
/// <remarks>
/// This is Stage 2.5 in the pipeline:
/// Extract → Refine (rules) → LLM-Refine → Chunk → Enrich
///
/// LLM refinement is optional and will be gracefully skipped if:
/// - No IDocumentAnalysisService is registered
/// - Options indicate refinement should be skipped
/// - LLM service is unavailable
/// </remarks>
public interface ILlmRefiner
{
    /// <summary>
    /// Refine content using LLM to improve quality.
    /// </summary>
    /// <param name="refined">Rule-based refined content from Stage 2</param>
    /// <param name="options">LLM refinement options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>LLM-refined content with enhanced quality</returns>
    Task<LlmRefinedContent> RefineAsync(
        RefinedContent refined,
        LlmRefineOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refiner type identifier.
    /// </summary>
    string RefinerType { get; }

    /// <summary>
    /// Whether LLM service is available for refinement.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Name of the LLM model being used (if available).
    /// </summary>
    string? ModelName { get; }
}
