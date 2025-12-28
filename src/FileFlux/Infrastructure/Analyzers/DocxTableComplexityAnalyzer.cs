using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace FileFlux.Infrastructure.Analyzers;

/// <summary>
/// Analyzes DOCX table complexity to detect merged cells, nested tables, and other complex structures.
/// This analyzer provides warnings and complexity metrics for tables that may lose structure when converted to Markdown.
/// </summary>
/// <remarks>
/// Phase 1 implementation for DOCX complex table handling roadmap.
/// Detects: Merged cells (rowspan/colspan), nested tables, irregular row structures.
/// </remarks>
public class DocxTableComplexityAnalyzer
{
    /// <summary>
    /// Options for table complexity analysis.
    /// </summary>
    public class Options
    {
        /// <summary>
        /// Whether to analyze nested tables. Default: true.
        /// </summary>
        public bool DetectNestedTables { get; set; } = true;

        /// <summary>
        /// Whether to analyze merged cells. Default: true.
        /// </summary>
        public bool DetectMergedCells { get; set; } = true;

        /// <summary>
        /// Whether to generate warnings for complex tables. Default: true.
        /// </summary>
        public bool GenerateWarnings { get; set; } = true;

        /// <summary>
        /// Complexity score threshold above which tables are flagged as complex.
        /// Range: 0.0-1.0. Default: 0.3.
        /// </summary>
        public double ComplexityThreshold { get; set; } = 0.3;
    }

    private readonly Options _options;

    /// <summary>
    /// Creates a new analyzer with default options.
    /// </summary>
    public DocxTableComplexityAnalyzer() : this(new Options()) { }

    /// <summary>
    /// Creates a new analyzer with custom options.
    /// </summary>
    public DocxTableComplexityAnalyzer(Options options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Analyzes a DOCX table and returns a complexity result.
    /// </summary>
    /// <param name="table">The OpenXML Table element to analyze.</param>
    /// <returns>Analysis result with complexity metrics and warnings.</returns>
    public TableAnalysisResult Analyze(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var result = new TableAnalysisResult();

        // Count rows and calculate basic stats
        var rows = table.Elements<TableRow>().ToList();
        result.RowCount = rows.Count;

        if (rows.Count == 0)
        {
            return result;
        }

        // Analyze each row for cells and spans
        var rowCellCounts = new List<int>();
        var totalCells = 0;

        foreach (var row in rows)
        {
            var cells = row.Elements<TableCell>().ToList();
            var logicalCellCount = 0;

            foreach (var cell in cells)
            {
                totalCells++;
                logicalCellCount++;

                // Detect horizontal merge (colspan)
                if (_options.DetectMergedCells)
                {
                    var gridSpan = cell.TableCellProperties?.GridSpan?.Val;
                    if (gridSpan != null && gridSpan.Value > 1)
                    {
                        result.HasHorizontalMerge = true;
                        result.HorizontalMergeCells++;
                        logicalCellCount += (int)gridSpan.Value - 1;
                    }

                    // Detect vertical merge (rowspan)
                    var vMerge = cell.TableCellProperties?.VerticalMerge;
                    if (vMerge != null)
                    {
                        // Val="restart" means start of merged cell, no val or "continue" means continuation
                        if (vMerge.Val == null || vMerge.Val.Value == MergedCellValues.Continue)
                        {
                            result.HasVerticalMerge = true;
                            result.VerticalMergeCells++;
                        }
                        else if (vMerge.Val.Value == MergedCellValues.Restart)
                        {
                            result.HasVerticalMerge = true;
                        }
                    }
                }

                // Detect nested tables
                if (_options.DetectNestedTables)
                {
                    var nestedTables = cell.Descendants<Table>().ToList();
                    if (nestedTables.Count > 0)
                    {
                        result.HasNestedTables = true;
                        result.NestedTableCount += nestedTables.Count;
                    }
                }
            }

            rowCellCounts.Add(logicalCellCount);
        }

        // Calculate column count from max logical cells
        result.ColumnCount = rowCellCounts.Max();
        result.TotalCells = totalCells;

        // Detect irregular row structure
        var uniqueCellCounts = rowCellCounts.Distinct().Count();
        result.HasIrregularRows = uniqueCellCounts > 1 && !result.HasHorizontalMerge && !result.HasVerticalMerge;

        // Calculate complexity score
        result.ComplexityScore = CalculateComplexityScore(result);
        result.ComplexityLevel = DetermineComplexityLevel(result.ComplexityScore);

        // Generate warnings if enabled
        if (_options.GenerateWarnings)
        {
            result.Warnings = GenerateWarnings(result);
        }

        return result;
    }

    /// <summary>
    /// Analyzes all tables in a document body.
    /// </summary>
    /// <param name="body">The document body containing tables.</param>
    /// <returns>List of analysis results for each table.</returns>
    public List<TableAnalysisResult> AnalyzeAll(Body body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var results = new List<TableAnalysisResult>();
        var tables = body.Elements<Table>().ToList();

        for (int i = 0; i < tables.Count; i++)
        {
            var result = Analyze(tables[i]);
            result.TableIndex = i + 1;
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Calculates complexity score based on detected features.
    /// </summary>
    private static double CalculateComplexityScore(TableAnalysisResult result)
    {
        double score = 0.0;

        // Horizontal merge adds complexity
        if (result.HasHorizontalMerge)
        {
            score += 0.25 + (result.HorizontalMergeCells * 0.02);
        }

        // Vertical merge adds more complexity (harder to represent in Markdown)
        if (result.HasVerticalMerge)
        {
            score += 0.30 + (result.VerticalMergeCells * 0.03);
        }

        // Nested tables are very complex
        if (result.HasNestedTables)
        {
            score += 0.40 + (result.NestedTableCount * 0.10);
        }

        // Irregular rows add some complexity
        if (result.HasIrregularRows)
        {
            score += 0.10;
        }

        // Large tables are more prone to issues
        if (result.RowCount > 20)
        {
            score += 0.05;
        }

        if (result.ColumnCount > 8)
        {
            score += 0.05;
        }

        return Math.Min(1.0, score);
    }

    /// <summary>
    /// Determines complexity level from score.
    /// </summary>
    private static TableComplexityLevel DetermineComplexityLevel(double score)
    {
        return score switch
        {
            < 0.1 => TableComplexityLevel.Simple,
            < 0.3 => TableComplexityLevel.Low,
            < 0.5 => TableComplexityLevel.Medium,
            < 0.7 => TableComplexityLevel.High,
            _ => TableComplexityLevel.VeryHigh
        };
    }

    /// <summary>
    /// Generates warning messages for complex tables.
    /// </summary>
    private List<string> GenerateWarnings(TableAnalysisResult result)
    {
        var warnings = new List<string>();

        if (result.HasVerticalMerge)
        {
            warnings.Add($"Table contains {result.VerticalMergeCells} vertically merged cells (rowspan). Markdown tables do not support rowspan - content will be flattened.");
        }

        if (result.HasHorizontalMerge)
        {
            warnings.Add($"Table contains {result.HorizontalMergeCells} horizontally merged cells (colspan). Markdown tables do not support colspan - cells will be split.");
        }

        if (result.HasNestedTables)
        {
            warnings.Add($"Table contains {result.NestedTableCount} nested table(s). Nested tables will be flattened or may lose structure.");
        }

        if (result.HasIrregularRows)
        {
            warnings.Add("Table has irregular row structure (varying cell counts). This may cause alignment issues in Markdown.");
        }

        if (result.ComplexityScore >= _options.ComplexityThreshold)
        {
            warnings.Add($"Table complexity score ({result.ComplexityScore:F2}) exceeds threshold ({_options.ComplexityThreshold:F2}). Consider alternative representation (list format or HTML comment).");
        }

        return warnings;
    }
}

/// <summary>
/// Result of table complexity analysis.
/// </summary>
public class TableAnalysisResult
{
    /// <summary>
    /// Index of this table in the document (1-based).
    /// </summary>
    public int TableIndex { get; set; }

    /// <summary>
    /// Number of rows in the table.
    /// </summary>
    public int RowCount { get; set; }

    /// <summary>
    /// Number of columns (maximum logical cell count across rows).
    /// </summary>
    public int ColumnCount { get; set; }

    /// <summary>
    /// Total physical cell count.
    /// </summary>
    public int TotalCells { get; set; }

    /// <summary>
    /// Whether the table has horizontally merged cells (colspan).
    /// </summary>
    public bool HasHorizontalMerge { get; set; }

    /// <summary>
    /// Number of horizontally merged cells.
    /// </summary>
    public int HorizontalMergeCells { get; set; }

    /// <summary>
    /// Whether the table has vertically merged cells (rowspan).
    /// </summary>
    public bool HasVerticalMerge { get; set; }

    /// <summary>
    /// Number of vertically merged cells.
    /// </summary>
    public int VerticalMergeCells { get; set; }

    /// <summary>
    /// Whether the table contains nested tables.
    /// </summary>
    public bool HasNestedTables { get; set; }

    /// <summary>
    /// Number of nested tables.
    /// </summary>
    public int NestedTableCount { get; set; }

    /// <summary>
    /// Whether the table has irregular row structure.
    /// </summary>
    public bool HasIrregularRows { get; set; }

    /// <summary>
    /// Calculated complexity score (0.0 to 1.0).
    /// </summary>
    public double ComplexityScore { get; set; }

    /// <summary>
    /// Determined complexity level.
    /// </summary>
    public TableComplexityLevel ComplexityLevel { get; set; }

    /// <summary>
    /// Warning messages for this table.
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Whether this table has any complexity that may cause issues in Markdown.
    /// </summary>
    public bool HasComplexity => HasHorizontalMerge || HasVerticalMerge || HasNestedTables || HasIrregularRows;

    /// <summary>
    /// Whether this table would be filtered with current settings.
    /// </summary>
    public bool RequiresSpecialHandling => ComplexityLevel >= TableComplexityLevel.Medium;
}

/// <summary>
/// Table complexity levels.
/// </summary>
public enum TableComplexityLevel
{
    /// <summary>
    /// Simple table - no merged cells, no nested tables.
    /// </summary>
    Simple,

    /// <summary>
    /// Low complexity - minor issues, Markdown should work well.
    /// </summary>
    Low,

    /// <summary>
    /// Medium complexity - some structure may be lost in Markdown.
    /// </summary>
    Medium,

    /// <summary>
    /// High complexity - significant structure loss expected.
    /// </summary>
    High,

    /// <summary>
    /// Very high complexity - consider alternative representation.
    /// </summary>
    VeryHigh
}
