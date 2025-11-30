namespace FileFlux.Core;

/// <summary>
/// Interface for evaluating relevance of image text to document content.
/// Evaluates whether text extracted from images in a document is relevant to the actual document content.
/// </summary>
public interface IImageRelevanceEvaluator
{
    /// <summary>
    /// Evaluates whether text extracted from an image is relevant to the document.
    /// </summary>
    /// <param name="imageText">Text extracted from image</param>
    /// <param name="documentContext">Document context information</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Image relevance evaluation result</returns>
    Task<ImageRelevanceResult> EvaluateAsync(
        string imageText,
        DocumentContext documentContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch evaluates multiple image texts.
    /// </summary>
    /// <param name="imageTexts">List of texts extracted from images</param>
    /// <param name="documentContext">Document context information</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of image relevance evaluation results</returns>
    Task<IEnumerable<ImageRelevanceResult>> EvaluateBatchAsync(
        IEnumerable<string> imageTexts,
        DocumentContext documentContext,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Document context information
/// </summary>
public class DocumentContext
{
    /// <summary>
    /// Document title
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Full document text or summary
    /// </summary>
    public string DocumentText { get; set; } = string.Empty;

    /// <summary>
    /// Document type (PDF, DOCX, PPTX, etc.)
    /// </summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>
    /// Page number where image is located (if applicable)
    /// </summary>
    public int? PageNumber { get; set; }

    /// <summary>
    /// Surrounding text (text before and after image within certain range)
    /// </summary>
    public string? SurroundingText { get; set; }

    /// <summary>
    /// List of document keywords
    /// </summary>
    public List<string> Keywords { get; set; } = new();

    /// <summary>
    /// Document metadata
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Image relevance evaluation result
/// </summary>
public class ImageRelevanceResult
{
    /// <summary>
    /// Whether the image is relevant
    /// </summary>
    public bool IsRelevant { get; set; }

    /// <summary>
    /// Relevance score (0.0 ~ 1.0)
    /// </summary>
    public double RelevanceScore { get; set; }

    /// <summary>
    /// Relevance category
    /// </summary>
    public RelevanceCategory Category { get; set; }

    /// <summary>
    /// Explanation for the evaluation
    /// </summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>
    /// Processed text (refined or summarized version)
    /// </summary>
    public string? ProcessedText { get; set; }

    /// <summary>
    /// Inclusion recommendation
    /// </summary>
    public InclusionRecommendation Recommendation { get; set; }

    /// <summary>
    /// Extracted key points
    /// </summary>
    public List<string> ExtractedKeyPoints { get; set; } = new();

    /// <summary>
    /// Evaluation metadata
    /// </summary>
    public Dictionary<string, object> EvaluationMetadata { get; set; } = new();
}

/// <summary>
/// Relevance category
/// </summary>
public enum RelevanceCategory
{
    /// <summary>
    /// Core content (charts, diagrams, key information)
    /// </summary>
    CoreContent,

    /// <summary>
    /// Supplementary information (captions, labels, additional explanations)
    /// </summary>
    SupplementaryInfo,

    /// <summary>
    /// Decorative elements (logos, icons, backgrounds)
    /// </summary>
    Decorative,

    /// <summary>
    /// Not relevant (ads, watermarks, page numbers)
    /// </summary>
    Irrelevant,

    /// <summary>
    /// Cannot determine
    /// </summary>
    Uncertain
}

/// <summary>
/// Inclusion recommendation
/// </summary>
public enum InclusionRecommendation
{
    /// <summary>
    /// Must include (core information)
    /// </summary>
    MustInclude,

    /// <summary>
    /// Should include (useful information)
    /// </summary>
    ShouldInclude,

    /// <summary>
    /// Optional include (supplementary information)
    /// </summary>
    OptionalInclude,

    /// <summary>
    /// Should exclude (low relevance)
    /// </summary>
    ShouldExclude,

    /// <summary>
    /// Must exclude (irrelevant information)
    /// </summary>
    MustExclude
}
