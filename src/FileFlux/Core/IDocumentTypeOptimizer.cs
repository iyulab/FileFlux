using FileFlux.Domain;

namespace FileFlux;

/// <summary>
/// Optimizes chunking parameters based on document type and characteristics.
/// Research-based optimal parameters for different document types.
/// </summary>
public interface IDocumentTypeOptimizer
{
    /// <summary>
    /// Analyzes document and determines its type.
    /// </summary>
    /// <param name="content">Document content to analyze</param>
    /// <param name="metadata">Document metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detected document type</returns>
    Task<DocumentTypeInfo> DetectDocumentTypeAsync(
        string content,
        DocumentMetadata? metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets optimal chunking options for the document type.
    /// </summary>
    /// <param name="documentType">The document type</param>
    /// <returns>Optimized chunking options</returns>
    ChunkingOptions GetOptimalOptions(DocumentTypeInfo documentType);

    /// <summary>
    /// Automatically detects type and returns optimal options.
    /// </summary>
    /// <param name="content">Document content</param>
    /// <param name="metadata">Document metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Optimized chunking options</returns>
    Task<ChunkingOptions> GetOptimalOptionsAsync(
        string content,
        DocumentMetadata? metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets performance metrics for different document types.
    /// </summary>
    /// <returns>Performance metrics by document type</returns>
    Dictionary<DocumentCategory, PerformanceMetrics> GetPerformanceMetrics();
}

/// <summary>
/// Information about detected document type.
/// </summary>
public class DocumentTypeInfo
{
    /// <summary>
    /// Primary category of the document.
    /// </summary>
    public DocumentCategory Category { get; set; }

    /// <summary>
    /// Specific sub-type if applicable.
    /// </summary>
    public string? SubType { get; set; }

    /// <summary>
    /// Confidence in the detection (0-1).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Detected language of the document.
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Document complexity score (0-1).
    /// </summary>
    public double ComplexityScore { get; set; }

    /// <summary>
    /// Average sentence length.
    /// </summary>
    public double AverageSentenceLength { get; set; }

    /// <summary>
    /// Detected structural elements.
    /// </summary>
    public List<DocumentStructuralElement> StructuralElements { get; set; } = new();

    /// <summary>
    /// Key characteristics of the document.
    /// </summary>
    public Dictionary<string, object> Characteristics { get; set; } = new();
}

/// <summary>
/// Document categories based on research.
/// </summary>
public enum DocumentCategory
{
    /// <summary>
    /// Technical documentation (500-800 tokens, 20-30% overlap)
    /// </summary>
    Technical,

    /// <summary>
    /// Legal documents (300-500 tokens, 15-25% overlap)
    /// </summary>
    Legal,

    /// <summary>
    /// Academic papers (200-400 tokens, 25-35% overlap)
    /// </summary>
    Academic,

    /// <summary>
    /// Financial documents (element-based, dynamic granularity)
    /// </summary>
    Financial,

    /// <summary>
    /// Medical/Healthcare documents
    /// </summary>
    Medical,

    /// <summary>
    /// Business documents (reports, proposals)
    /// </summary>
    Business,

    /// <summary>
    /// Creative content (articles, stories)
    /// </summary>
    Creative,

    /// <summary>
    /// General/Unknown type
    /// </summary>
    General
}

/// <summary>
/// Document structural elements detected during analysis.
/// </summary>
public class DocumentStructuralElement
{
    /// <summary>
    /// Type of structural element.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Count of this element type.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Average size of elements.
    /// </summary>
    public double AverageSize { get; set; }

    /// <summary>
    /// Importance weight (0-1).
    /// </summary>
    public double Importance { get; set; }
}

/// <summary>
/// Performance metrics for a document type.
/// </summary>
public class PerformanceMetrics
{
    /// <summary>
    /// Optimal token range (min-max).
    /// </summary>
    public (int Min, int Max) OptimalTokenRange { get; set; }

    /// <summary>
    /// Optimal overlap percentage range.
    /// </summary>
    public (double Min, double Max) OptimalOverlapRange { get; set; }

    /// <summary>
    /// Expected F1 score with optimal settings.
    /// </summary>
    public double ExpectedF1Score { get; set; }

    /// <summary>
    /// Expected retrieval accuracy.
    /// </summary>
    public double ExpectedAccuracy { get; set; }

    /// <summary>
    /// Processing speed factor (1.0 = baseline).
    /// </summary>
    public double ProcessingSpeedFactor { get; set; }

    /// <summary>
    /// Recommended chunking strategy.
    /// </summary>
    public string RecommendedStrategy { get; set; } = "Intelligent";

    /// <summary>
    /// Additional optimization hints.
    /// </summary>
    public List<string> OptimizationHints { get; set; } = new();
}