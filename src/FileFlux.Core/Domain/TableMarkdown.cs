using System.Text;

namespace FileFlux.Core;

/// <summary>
/// Renders a <see cref="TableData"/> as a GitHub-flavoured Markdown table. The one renderer every FileFlux path uses,
/// so a table reads the same whichever reader or processor produced it.
/// </summary>
/// <remarks>
/// <para>
/// The delimiter row is as wide as the grid, so the output is always a valid GFM table. The first row is the Markdown
/// header row when <see cref="TableData.HasHeader"/> is true. A header of several rows (a merged group row above column
/// labels — declared by the parser in <c>Props["header_rows"]</c>, or a top row with a horizontally merged cell) is written as one header row whose cells join each column's labels top to
/// bottom (<c>group / label</c>, a merged label repeated over the columns it spans), so every column is named in the one
/// row a table chunk repeats. A table without a header gets an empty header row rather than invented column names.
/// </para>
/// <para>
/// GFM has no spans: a merged cell's text sits in its first (top-left) position and the positions it covers stay empty,
/// so values to its right keep their columns. <see cref="TableData.MergedCells"/> carries the spans for a consumer that
/// needs them.
/// </para>
/// </remarks>
public static class TableMarkdown
{
    /// <summary>
    /// Renders <paramref name="table"/> as a GFM table, without a trailing newline. Returns an empty string for a table
    /// with no rows or no columns.
    /// </summary>
    public static string Render(TableData table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var columns = table.Cells.Length == 0 ? 0 : table.Cells.Max(row => row?.Length ?? 0);
        if (columns == 0)
            return string.Empty;

        var sb = new StringBuilder();
        var firstBodyRow = 0;

        var headerRows = table.HasHeader ? Math.Clamp(DeclaredHeaderRows(table), 1, table.Cells.Length) : 0;
        if (headerRows > 1)
        {
            AppendRow(sb, CombinedHeader(table, headerRows, columns), columns);
            firstBodyRow = headerRows;
        }
        else if (table.HasHeader)
        {
            AppendRow(sb, table.Cells[0], columns);
            firstBodyRow = 1;
        }
        else
        {
            AppendRow(sb, [], columns);
        }

        sb.Append('\n').Append('|');
        for (var c = 0; c < columns; c++)
        {
            var alignment = table.ColumnAlignments is { } alignments && c < alignments.Length ? alignments[c] : (TextAlignment?)null;
            sb.Append(alignment switch
            {
                TextAlignment.Left => " :--- |",
                TextAlignment.Right => " ---: |",
                TextAlignment.Center => " :---: |",
                _ => " --- |",
            });
        }

        for (var r = firstBodyRow; r < table.Cells.Length; r++)
        {
            sb.Append('\n');
            AppendRow(sb, table.Cells[r] ?? [], columns);
        }

        return sb.ToString();
    }

    /// <summary>
    /// The rows that make up the header: as many as the parser declared (<c>Props["header_rows"]</c>), and at least every
    /// top row that holds a horizontally merged cell — a label spanning several columns is a group label over the row
    /// below it, so that row is a header row too. At least one row is left as body.
    /// </summary>
    private static int DeclaredHeaderRows(TableData table)
    {
        var declared = table.Props.TryGetValue("header_rows", out var value) && value is int rows ? rows : 1;
        var grouped = 0;
        while (grouped < table.Cells.Length - 1 &&
               table.MergedCells.Any(m => m.StartRow == grouped && m.EndRow == grouped && m.EndCol > m.StartCol))
            grouped++;

        return Math.Max(declared, grouped == 0 ? 1 : Math.Min(grouped + 1, table.Cells.Length - 1));
    }

    /// <summary>
    /// One label per column from the first <paramref name="headerRows"/> rows: the labels above each other joined with
    /// <c> / </c>, a merged cell's label counted in every column (and row) it covers, a label repeated by a vertical merge
    /// written once.
    /// </summary>
    private static string[] CombinedHeader(TableData table, int headerRows, int columns)
    {
        var combined = new string[columns];
        for (var c = 0; c < columns; c++)
        {
            var parts = new List<string>();
            for (var r = 0; r < headerRows; r++)
            {
                var label = LabelAt(table, r, c);
                if (label.Length > 0 && (parts.Count == 0 || parts[^1] != label))
                    parts.Add(label);
            }

            combined[c] = string.Join(" / ", parts);
        }

        return combined;
    }

    private static string LabelAt(TableData table, int row, int col)
    {
        var cells = table.Cells[row];
        var own = cells is not null && col < cells.Length ? cells[col]?.Trim() ?? string.Empty : string.Empty;
        if (own.Length > 0)
            return own;

        foreach (var merged in table.MergedCells)
        {
            if (row < merged.StartRow || row > merged.EndRow || col < merged.StartCol || col > merged.EndCol)
                continue;

            var anchor = table.Cells[merged.StartRow];
            var text = anchor is not null && merged.StartCol < anchor.Length ? anchor[merged.StartCol]?.Trim() : null;
            return string.IsNullOrEmpty(text) ? merged.Content.Trim() : text;
        }

        return string.Empty;
    }

    private static void AppendRow(StringBuilder sb, string[] cells, int columns)
    {
        sb.Append('|');
        for (var c = 0; c < columns; c++)
        {
            var text = c < cells.Length ? Escape(cells[c]) : string.Empty;
            sb.Append(text.Length == 0 ? "  |" : $" {text} |");
        }
    }

    private static string Escape(string? cell)
    {
        if (string.IsNullOrEmpty(cell))
            return string.Empty;

        return cell
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Trim()
            .Replace("\n", "<br>", StringComparison.Ordinal);
    }
}
