namespace FileFlux.Core;

/// <summary>
/// Parsed document content domain model
/// </summary>
public class DocumentContent
{
    /// <summary>
    /// Extracted text content
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Document metadata
    /// </summary>
    public DocumentMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Document structure information
    /// </summary>
    public Dictionary<string, object> StructureInfo { get; set; } = new();

    /// <summary>
    /// Document section hierarchy (for HeadingPath extraction)
    /// </summary>
    public List<ContentSection> Sections { get; set; } = new();

    /// <summary>
    /// Page ranges for PDF documents
    /// Key: page number (1-based), Value: (start char, end char)
    /// </summary>
    public Dictionary<int, (int Start, int End)> PageRanges { get; set; } = new();

    /// <summary>
    /// Extracted images
    /// </summary>
    public List<ImageInfo> Images { get; set; } = new();

    /// <summary>
    /// Extracted tables
    /// </summary>
    public List<TableInfo> Tables { get; set; } = new();
}

/// <summary>
/// Image information
/// </summary>
public class ImageInfo
{
    public string Id { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public int Position { get; set; }
    public Dictionary<string, object> Properties { get; } = new();
}

/// <summary>
/// Table information
/// </summary>
public class TableInfo
{
    public string Id { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public int Position { get; set; }
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public string Data { get; set; } = string.Empty;
    public List<string> ColumnHeaders { get; set; } = new();
}

/// <summary>
/// Document section information (for HeadingPath extraction)
/// </summary>
public class ContentSection
{
    public string Title { get; set; } = string.Empty;
    public int Level { get; set; }
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
    public List<ContentSection> Children { get; set; } = new();
}
