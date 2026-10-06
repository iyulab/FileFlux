using System.Text;
using System.Text.Json;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Reads the tables out of a parser's <c>ToJson</c> output (Unpdf, Undoc, Unhwp) as <see cref="TableData"/>.
/// </summary>
/// <remarks>
/// <para>
/// The parsers expose tables only as JSON; their Markdown flattens them. The shapes differ in naming
/// (<c>row_span</c>/<c>rowspan</c>, header flags on rows, cells, or the table) and in one structural detail that decides
/// column placement: a merged cell is recorded once, on the cell that owns it — the positions a vertical merge covers
/// in the rows below have no cell (Unpdf, Unhwp, and Undoc from 0.14.0 for every format). A row's cells are therefore
/// placed left to right, skipping the positions merges from above still cover, which lands each value in its own column.
/// </para>
/// </remarks>
internal static class ParserTableJson
{
    /// <summary>A table with where the parser found it.</summary>
    internal sealed record Located(TableData Table, int? PageNumber, int? SectionIndex, string? SectionName);

    /// <summary>One Undoc section (an xlsx sheet, for instance) as its ordered content: tables and text blocks.</summary>
    internal sealed record Section(int Index, string? Name, IReadOnlyList<object> Items);

    /// <summary>
    /// Every table in <paramref name="json"/>, in document order, with the page (Unpdf <c>pages[].number</c>) or
    /// section (Undoc/Unhwp <c>sections[]</c>) it sits in. <paramref name="layoutInferred"/> marks tables a parser
    /// inferred from page layout rather than read from document structure (PDF).
    /// </summary>
    internal static IReadOnlyList<Located> ReadTables(string json, bool layoutInferred)
    {
        using var doc = JsonDocument.Parse(json);
        var found = new List<Located>();
        Walk(doc.RootElement, page: null, sectionIndex: null, sectionName: null, found, layoutInferred);
        for (var i = 0; i < found.Count; i++)
            found[i].Table.Order = i;
        return found;
    }

    /// <summary>
    /// Undoc sections with their content in order — a <see cref="TableData"/> for each table, a <see cref="string"/>
    /// for each text block. Used where FileFlux writes the text itself rather than taking the parser's Markdown.
    /// </summary>
    internal static IReadOnlyList<Section> ReadSections(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var sections = new List<Section>();
        if (!doc.RootElement.TryGetProperty("sections", out var sectionArray) || sectionArray.ValueKind != JsonValueKind.Array)
            return sections;

        var order = 0;
        var index = 0;
        foreach (var section in sectionArray.EnumerateArray())
        {
            var name = section.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            var items = new List<object>();
            if (section.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in content.EnumerateArray())
                {
                    if (IsTable(item))
                    {
                        var table = ToTable(item, layoutInferred: false);
                        table.Order = order++;
                        table.PageNumber = index + 1;
                        if (name is not null)
                            table.Props["section_name"] = name;
                        items.Add(table);
                    }
                    else if (BlockText(item) is { Length: > 0 } text)
                    {
                        items.Add(text);
                    }
                }
            }

            sections.Add(new Section(index, name, items));
            index++;
        }

        return sections;
    }

    /// <summary>
    /// The tables of a parsed document as <see cref="TableData"/>, for a reader whose text comes from the parser's
    /// Markdown. A failure to produce or read the JSON costs the structured view only, never the text: it is recorded in
    /// <paramref name="warnings"/> and an empty list is returned.
    /// </summary>
    internal static List<TableData> TryReadTables(Func<string> toJson, bool layoutInferred, List<string> warnings)
    {
        try
        {
            return ReadTables(toJson(), layoutInferred).Select(located => located.Table).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Table structure unavailable: {ex.Message}");
            return [];
        }
    }

    private static void Walk(JsonElement e, int? page, int? sectionIndex, string? sectionName, List<Located> found, bool layoutInferred)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                if (IsTable(e))
                {
                    var table = ToTable(e, layoutInferred);
                    if (page is { } p)
                        table.PageNumber = p;
                    else if (sectionIndex is { } s)
                        table.PageNumber = s + 1;
                    if (sectionName is not null)
                        table.Props["section_name"] = sectionName;
                    found.Add(new Located(table, page, sectionIndex, sectionName));
                    return; // nested tables inside cells stay part of their cell text
                }

                foreach (var property in e.EnumerateObject())
                {
                    if (property.NameEquals("pages") && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        var i = 0;
                        foreach (var pageElement in property.Value.EnumerateArray())
                        {
                            i++;
                            var number = pageElement.TryGetProperty("number", out var num) && num.TryGetInt32(out var parsed) ? parsed : i;
                            Walk(pageElement, number, sectionIndex, sectionName, found, layoutInferred);
                        }
                    }
                    else if (property.NameEquals("sections") && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        var i = 0;
                        foreach (var sectionElement in property.Value.EnumerateArray())
                        {
                            var name = sectionElement.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                            Walk(sectionElement, page, i, name, found, layoutInferred);
                            i++;
                        }
                    }
                    else
                    {
                        Walk(property.Value, page, sectionIndex, sectionName, found, layoutInferred);
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    Walk(item, page, sectionIndex, sectionName, found, layoutInferred);
                break;
        }
    }

    private static bool IsTable(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object
        && e.TryGetProperty("rows", out var rows)
        && rows.ValueKind == JsonValueKind.Array
        && rows.EnumerateArray().All(r => r.ValueKind == JsonValueKind.Object && r.TryGetProperty("cells", out var c) && c.ValueKind == JsonValueKind.Array);

    private static TableData ToTable(JsonElement table, bool layoutInferred)
    {
        var rows = table.GetProperty("rows").EnumerateArray().ToList();
        var cellsPerRow = rows.Select(r => r.GetProperty("cells").EnumerateArray().ToList()).ToList();
        var width = cellsPerRow.Count == 0 ? 0 : cellsPerRow.Max(cells => cells.Sum(c => Span(c, "col_span", "colspan")));

        var grid = new string[rows.Count][];
        var merged = new List<MergedCell>();
        var coveredUntil = new int[width]; // last row index a vertical merge from above still covers, per column; -1 = free
        Array.Fill(coveredUntil, -1);

        for (var r = 0; r < rows.Count; r++)
        {
            grid[r] = Enumerable.Repeat(string.Empty, width).ToArray();
            var cells = cellsPerRow[r];
            var col = 0;

            foreach (var cell in cells)
            {
                while (col < width && coveredUntil[col] >= r)
                    col++;

                if (col >= width)
                    break;

                var rowSpan = Math.Min(Span(cell, "row_span", "rowspan"), rows.Count - r);
                var colSpan = Math.Min(Span(cell, "col_span", "colspan"), width - col);
                var text = CellText(cell);
                grid[r][col] = text;

                if (rowSpan > 1 || colSpan > 1)
                {
                    merged.Add(new MergedCell
                    {
                        StartRow = r,
                        EndRow = r + rowSpan - 1,
                        StartCol = col,
                        EndCol = col + colSpan - 1,
                        Content = text,
                    });
                    for (var c = col; c < col + colSpan; c++)
                        coveredUntil[c] = Math.Max(coveredUntil[c], r + rowSpan - 1);
                }

                col += colSpan;
            }
        }

        var headerRows = HeaderRowCount(table, rows);
        var result = new TableData
        {
            Cells = grid,
            HasHeader = headerRows > 0,
            MergedCells = merged,
            DetectionMethod = layoutInferred ? TableDetectionMethod.Heuristic : TableDetectionMethod.Structured,
            // A layout-inferred table's header flag is the parser's default, not a detection (Unpdf marks the first
            // row of every table as header), so its confidence says so.
            Confidence = layoutInferred ? 0.5 : 1.0,
            Caption = table.TryGetProperty("caption", out var caption) && caption.ValueKind == JsonValueKind.String
                ? caption.GetString()
                : null,
        };
        result.Props["header_rows"] = headerRows;
        return result;
    }

    private static int HeaderRowCount(JsonElement table, List<JsonElement> rows)
    {
        var flagged = 0;
        foreach (var row in rows)
        {
            var rowFlag = row.TryGetProperty("is_header", out var f) && f.ValueKind == JsonValueKind.True;
            var cellsFlag = row.GetProperty("cells").EnumerateArray()
                .Any(c => c.TryGetProperty("is_header", out var cf) && cf.ValueKind == JsonValueKind.True);
            if (!rowFlag && !cellsFlag)
                break;
            flagged++;
        }

        if (flagged > 0)
            return flagged;

        if (table.TryGetProperty("header_rows", out var hr) && hr.TryGetInt32(out var declared) && declared > 0)
            return Math.Min(declared, rows.Count);

        return table.TryGetProperty("has_header", out var hh) && hh.ValueKind == JsonValueKind.True && rows.Count > 0 ? 1 : 0;
    }

    private static int Span(JsonElement cell, string snake, string flat)
    {
        if (cell.TryGetProperty(snake, out var a) && a.TryGetInt32(out var x) && x > 0)
            return x;
        if (cell.TryGetProperty(flat, out var b) && b.TryGetInt32(out var y) && y > 0)
            return y;
        return 1;
    }

    /// <summary>Paragraphs of a cell joined by newlines; the text of each is every <c>text</c> run inside it.</summary>
    private static string CellText(JsonElement cell)
    {
        if (cell.TryGetProperty("text", out var direct) && direct.ValueKind == JsonValueKind.String)
            return direct.GetString()!.Trim();

        if (!cell.TryGetProperty("content", out var content))
            return string.Empty;

        if (content.ValueKind == JsonValueKind.String)
            return content.GetString()!.Trim();

        if (content.ValueKind != JsonValueKind.Array)
            return InlineText(content).Trim();

        var paragraphs = content.EnumerateArray()
            .Where(block => !IsTable(block))
            .Select(block => InlineText(block).Trim())
            .Where(text => text.Length > 0);
        return string.Join("\n", paragraphs);
    }

    private static string? BlockText(JsonElement block)
    {
        var text = InlineText(block).Trim();
        return text.Length == 0 ? null : text;
    }

    private static string InlineText(JsonElement e)
    {
        var sb = new StringBuilder();
        Collect(e, sb);
        return sb.ToString();

        static void Collect(JsonElement e, StringBuilder sb)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    if (e.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                        sb.Append(text.GetString());
                    foreach (var property in e.EnumerateObject())
                    {
                        if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                            && !property.NameEquals("style"))
                            Collect(property.Value, sb);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in e.EnumerateArray())
                        Collect(item, sb);
                    break;
            }
        }
    }
}
