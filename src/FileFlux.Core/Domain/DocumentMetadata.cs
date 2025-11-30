namespace FileFlux.Core;

/// <summary>
/// Document metadata information
/// </summary>
public class DocumentMetadata
{
    /// <summary>
    /// File name
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// File type (PDF, DOCX, etc.)
    /// </summary>
    public string FileType { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Document title
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Document author
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Document creation date
    /// </summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// Document modification date
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// Document processing date
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Document language (ISO 639-1 code)
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Language detection confidence (0.0 - 1.0)
    /// </summary>
    public double LanguageConfidence { get; set; } = 0.0;

    /// <summary>
    /// Total page count
    /// </summary>
    public int PageCount { get; set; }

    /// <summary>
    /// Total word count
    /// </summary>
    public int WordCount { get; set; }

    /// <summary>
    /// Custom properties
    /// </summary>
    public Dictionary<string, object> CustomProperties { get; } = new();
}
