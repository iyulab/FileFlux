namespace FileFlux.Core;

/// <summary>
/// Raw table data extracted from document before markdown conversion.
/// Contains cell data, structure information, and quality metrics.
/// </summary>
public class TableData
{
    /// <summary>
    /// Unique table ID.
    /// </summary>
    public string Id { get; init; } = $"table_{Guid.NewGuid():N}";

    /// <summary>
    /// Cell data as 2D array [row][column].
    /// </summary>
    public string[][] Cells { get; set; } = [];

    /// <summary>
    /// Number of rows.
    /// </summary>
    public int RowCount => Cells.Length;

    /// <summary>
    /// Number of columns.
    /// </summary>
    public int ColumnCount => Cells.Length > 0 ? Cells[0].Length : 0;

    /// <summary>
    /// Whether first row is header.
    /// </summary>
    public bool HasHeader { get; set; } = true;

    /// <summary>
    /// Column headers (if HasHeader is true).
    /// </summary>
    public string[]? Headers => HasHeader && Cells.Length > 0 ? Cells[0] : null;

    /// <summary>
    /// Merged cell information.
    /// </summary>
    public List<MergedCell> MergedCells { get; set; } = [];

    /// <summary>
    /// Column width hints (for alignment).
    /// </summary>
    public int[]? ColumnWidths { get; set; }

    /// <summary>
    /// Column alignments.
    /// </summary>
    public TextAlignment[]? ColumnAlignments { get; set; }

    /// <summary>
    /// Table detection confidence score (0.0 - 1.0).
    /// </summary>
    public double Confidence { get; set; } = 1.0;

    /// <summary>
    /// Detection method used.
    /// </summary>
    public TableDetectionMethod DetectionMethod { get; set; } = TableDetectionMethod.Structured;

    /// <summary>
    /// Page number where table appears (1-based).
    /// </summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// Bounding box location in document.
    /// </summary>
    public BoundingBox? Location { get; set; }

    /// <summary>
    /// Order in document (for sorting).
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Caption or title for the table.
    /// </summary>
    public string? Caption { get; set; }

    /// <summary>
    /// Plain text fallback (for low confidence tables).
    /// </summary>
    public string? PlainTextFallback { get; set; }

    /// <summary>
    /// Additional properties.
    /// </summary>
    public Dictionary<string, object> Props { get; set; } = [];

    /// <summary>
    /// Whether table has valid structure.
    /// </summary>
    public bool IsValid => RowCount >= 1 && ColumnCount >= 1;

    /// <summary>
    /// Whether table needs LLM assistance for conversion.
    /// </summary>
    public bool NeedsLlmAssist => Confidence < 0.7 || MergedCells.Count > 0;

    /// <summary>
    /// Get cell value at specified position.
    /// </summary>
    public string? GetCell(int row, int col)
    {
        if (row < 0 || row >= RowCount || col < 0 || col >= ColumnCount)
            return null;
        return Cells[row][col];
    }

    /// <summary>
    /// Get data rows (excluding header if present).
    /// </summary>
    public IEnumerable<string[]> GetDataRows()
    {
        var startIndex = HasHeader ? 1 : 0;
        for (int i = startIndex; i < Cells.Length; i++)
        {
            yield return Cells[i];
        }
    }
}

/// <summary>
/// Merged cell information.
/// </summary>
public class MergedCell
{
    /// <summary>
    /// Start row index (0-based).
    /// </summary>
    public int StartRow { get; set; }

    /// <summary>
    /// End row index (0-based, inclusive).
    /// </summary>
    public int EndRow { get; set; }

    /// <summary>
    /// Start column index (0-based).
    /// </summary>
    public int StartCol { get; set; }

    /// <summary>
    /// End column index (0-based, inclusive).
    /// </summary>
    public int EndCol { get; set; }

    /// <summary>
    /// Merged cell content.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Row span count.
    /// </summary>
    public int RowSpan => EndRow - StartRow + 1;

    /// <summary>
    /// Column span count.
    /// </summary>
    public int ColSpan => EndCol - StartCol + 1;
}

/// <summary>
/// Table detection method enumeration.
/// </summary>
public enum TableDetectionMethod
{
    /// <summary>
    /// Table from structured document (DOCX, XLSX).
    /// </summary>
    Structured,

    /// <summary>
    /// Table detected by grid lines (PDF).
    /// </summary>
    GridLines,

    /// <summary>
    /// Table detected by whitespace gaps (PDF).
    /// </summary>
    WhitespaceGaps,

    /// <summary>
    /// Table detected by text alignment patterns.
    /// </summary>
    AlignmentPattern,

    /// <summary>
    /// Table detected by heuristic analysis.
    /// </summary>
    Heuristic,

    /// <summary>
    /// Table detected/corrected by LLM.
    /// </summary>
    LlmAssisted
}
