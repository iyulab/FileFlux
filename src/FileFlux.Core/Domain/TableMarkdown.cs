using System.Text;

namespace FileFlux.Core;

/// <summary>
/// Renders a <see cref="TableData"/> as a GitHub-flavoured Markdown table. The one renderer every FileFlux path uses,
/// so a table reads the same whichever reader or processor produced it.
/// </summary>
/// <remarks>
/// <para>
/// The delimiter row is as wide as the grid, so the output is always a valid GFM table. The first row is the Markdown
/// header row when <see cref="TableData.HasHeader"/> is true; further header rows (a merged group row above column
/// labels) follow as ordinary rows, in order, so every label keeps its column. A table without a header gets an empty
/// header row rather than invented column names.
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

        if (table.HasHeader)
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
