namespace FileFlux.Domain;

/// <summary>
/// Provides document-level context for chunk enrichment and processing.
/// Contains information about the document's type, domain, and structure
/// that applies to all chunks within the document.
/// </summary>
public class DocumentContext
{
    /// <summary>
    /// Gets or sets the document type (e.g., PDF, Word, Markdown).
    /// </summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the document domain (e.g., Technical, Business, Academic, General).
    /// Used for domain-specific keyword extraction and topic scoring.
    /// </summary>
    public string DocumentDomain { get; set; } = "General";

    /// <summary>
    /// Gets or sets the document-wide keywords extracted from the full content.
    /// </summary>
    public List<string> GlobalKeywords { get; set; } = [];

    /// <summary>
    /// Gets or sets additional structural information about the document.
    /// Can include heading hierarchy, section counts, and other structural metrics.
    /// </summary>
    public Dictionary<string, object> StructureInfo { get; set; } = [];

    /// <summary>
    /// Gets or sets the document metadata (title, author, dates, etc.).
    /// </summary>
    public DocumentMetadata Metadata { get; set; } = new();
}
