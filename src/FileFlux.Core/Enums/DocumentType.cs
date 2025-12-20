namespace FileFlux.Core;

/// <summary>
/// Supported document types for FileFlux processing.
/// </summary>
public enum DocumentType
{
    /// <summary>
    /// Unknown or unsupported document type.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// PDF document (.pdf).
    /// </summary>
    Pdf,

    /// <summary>
    /// Microsoft Word document (.docx).
    /// </summary>
    Word,

    /// <summary>
    /// Microsoft Excel spreadsheet (.xlsx).
    /// </summary>
    Excel,

    /// <summary>
    /// Microsoft PowerPoint presentation (.pptx).
    /// </summary>
    PowerPoint,

    /// <summary>
    /// Markdown document (.md).
    /// </summary>
    Markdown,

    /// <summary>
    /// HTML document (.html, .htm).
    /// </summary>
    Html,

    /// <summary>
    /// Plain text file (.txt).
    /// </summary>
    Text,

    /// <summary>
    /// JSON file (.json).
    /// </summary>
    Json,

    /// <summary>
    /// CSV file (.csv).
    /// </summary>
    Csv,

    /// <summary>
    /// HWP document (.hwp, .hwpx) - Korean word processor format.
    /// </summary>
    Hwp
}

/// <summary>
/// Extension methods for DocumentType.
/// </summary>
public static class DocumentTypeExtensions
{
    /// <summary>
    /// Gets the file extensions associated with a document type.
    /// </summary>
    public static IReadOnlyList<string> GetExtensions(this DocumentType type) => type switch
    {
        DocumentType.Pdf => [".pdf"],
        DocumentType.Word => [".docx"],
        DocumentType.Excel => [".xlsx"],
        DocumentType.PowerPoint => [".pptx"],
        DocumentType.Markdown => [".md", ".markdown"],
        DocumentType.Html => [".html", ".htm"],
        DocumentType.Text => [".txt"],
        DocumentType.Json => [".json"],
        DocumentType.Csv => [".csv"],
        DocumentType.Hwp => [".hwp", ".hwpx"],
        _ => []
    };

    /// <summary>
    /// Gets the document type from a file extension.
    /// </summary>
    public static DocumentType FromExtension(string extension)
    {
        var ext = extension.ToLowerInvariant();
        if (!ext.StartsWith('.'))
            ext = "." + ext;

        return ext switch
        {
            ".pdf" => DocumentType.Pdf,
            ".docx" => DocumentType.Word,
            ".xlsx" => DocumentType.Excel,
            ".pptx" => DocumentType.PowerPoint,
            ".md" or ".markdown" => DocumentType.Markdown,
            ".html" or ".htm" => DocumentType.Html,
            ".txt" => DocumentType.Text,
            ".json" => DocumentType.Json,
            ".csv" => DocumentType.Csv,
            ".hwp" or ".hwpx" => DocumentType.Hwp,
            _ => DocumentType.Unknown
        };
    }

    /// <summary>
    /// Gets the document type from a file path.
    /// </summary>
    public static DocumentType FromFilePath(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return FromExtension(extension);
    }
}
