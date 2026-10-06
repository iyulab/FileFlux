using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Conversion;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Readers carry the parsers' tables as <see cref="TableData"/>: the parsers expose table structure only in their JSON,
/// and their Markdown flattens it (Undoc pads a vertical merge twice — undoc #792). One renderer
/// (<see cref="TableMarkdown"/>) writes every table, and refinement never writes a table into the text a second time.
/// </summary>
public class ParserTableTests
{
    private static readonly string VerticalMergeFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "vertical-merge.xlsx");

    // ---- grid expansion: the two placeholder conventions --------------------------------------------------------

    [Fact]
    public void ReadTables_UndocStyle_PlaceholderUnderAVerticalMerge_KeepsEveryValueInItsColumn()
    {
        // Undoc: the row under a row_span carries an empty placeholder at the covered position.
        const string json = """
            {"sections":[{"name":"S","content":[{"type":"Table","rows":[
              {"is_header":true,"cells":[{"content":[{"runs":[{"text":"Group"}]}],"is_header":true},{"content":[{"runs":[{"text":"Item"}]}],"is_header":true}]},
              {"cells":[{"content":[{"runs":[{"text":"A"}]}],"row_span":2},{"content":[{"runs":[{"text":"x"}]}]}]},
              {"cells":[{"content":[]},{"content":[{"runs":[{"text":"y"}]}]}]}
            ]}]}]}
            """;

        var table = ParserTableJson.ReadTables(json, layoutInferred: false).Single().Table;

        Assert.Equal([["Group", "Item"], ["A", "x"], ["", "y"]], table.Cells);
        var merge = Assert.Single(table.MergedCells);
        Assert.Equal((1, 2, 0, 0), (merge.StartRow, merge.EndRow, merge.StartCol, merge.EndCol));
        Assert.Equal("S", table.Props["section_name"]);
        Assert.Equal(TableDetectionMethod.Structured, table.DetectionMethod);
    }

    [Fact]
    public void ReadTables_NoPlaceholderConvention_SkipsThePositionAMergeStillCovers()
    {
        // Unpdf/Unhwp: no placeholder; the row under a rowspan is one cell short.
        const string json = """
            {"pages":[{"number":3,"elements":[{"type":"table","header_rows":1,"caption":"Totals","rows":[
              {"is_header":true,"cells":[{"content":[{"content":[{"type":"text","text":"Group"}]}],"rowspan":1,"colspan":1},{"content":[{"content":[{"type":"text","text":"Item"}]}],"rowspan":1,"colspan":1}]},
              {"is_header":false,"cells":[{"content":[{"content":[{"type":"text","text":"A"}]}],"rowspan":2,"colspan":1},{"content":[{"content":[{"type":"text","text":"x"}]}],"rowspan":1,"colspan":1}]},
              {"is_header":false,"cells":[{"content":[{"content":[{"type":"text","text":"y"}]}],"rowspan":1,"colspan":1}]}
            ]}]}]}
            """;

        var located = ParserTableJson.ReadTables(json, layoutInferred: true).Single();

        Assert.Equal([["Group", "Item"], ["A", "x"], ["", "y"]], located.Table.Cells);
        Assert.Equal(3, located.Table.PageNumber);
        Assert.Equal("Totals", located.Table.Caption);
        // A layout-inferred table says its header is the parser's default, not a detection.
        Assert.Equal(TableDetectionMethod.Heuristic, located.Table.DetectionMethod);
        Assert.True(located.Table.Confidence < 1.0);
    }

    [Fact]
    public void ReadTables_ColumnSpan_AnchorsAtItsStartColumn()
    {
        const string json = """
            {"sections":[{"content":[{"type":"Table","rows":[
              {"cells":[{"content":[{"runs":[{"text":"Basic"}]}],"col_span":2,"is_header":true},{"content":[{"runs":[{"text":"Contact"}]}],"is_header":true}]},
              {"cells":[{"content":[{"runs":[{"text":"No"}]}],"is_header":true},{"content":[{"runs":[{"text":"Name"}]}],"is_header":true},{"content":[{"runs":[{"text":"Mail"}]}],"is_header":true}]},
              {"cells":[{"content":[{"runs":[{"text":"1"}]}]},{"content":[{"runs":[{"text":"Kim"}]}]},{"content":[{"runs":[{"text":"k@x"}]}]}]}
            ]}]}]}
            """;

        var table = ParserTableJson.ReadTables(json, layoutInferred: false).Single().Table;

        Assert.Equal(["Basic", "", "Contact"], table.Cells[0]);
        Assert.Equal(2, table.Props["header_rows"]);
    }

    // ---- the renderer -----------------------------------------------------------------------------------------------

    [Fact]
    public void Render_DelimiterIsAsWideAsTheGrid_AndCellsAreEscaped()
    {
        var markdown = TableMarkdown.Render(new TableData
        {
            Cells = [["a", "b|c", "d"], ["line1\nline2", "", "e"]],
        });

        var lines = markdown.Split('\n');
        Assert.Equal("| a | b\\|c | d |", lines[0]);
        Assert.Equal("| --- | --- | --- |", lines[1]);
        Assert.Equal("| line1<br>line2 |  | e |", lines[2]);
    }

    [Fact]
    public void Render_NoHeader_GetsAnEmptyHeaderRowNotInventedNames()
    {
        var markdown = TableMarkdown.Render(new TableData { Cells = [["1", "2"]], HasHeader = false });

        Assert.Equal("|  |  |\n| --- | --- |\n| 1 | 2 |", markdown);
    }

    // ---- xlsx: text written from the JSON (#778) ------------------------------------------------------------------

    [Fact]
    public async Task Xlsx_VerticalMerge_EveryRowHasTheSheetsColumnCount_AndTheTableIsCarried()
    {
        var content = await new ExcelDocumentReader().ExtractAsync(VerticalMergeFixture, cancellationToken: TestContext.Current.CancellationToken);

        var tableLines = content.Text.Split('\n').Where(l => l.StartsWith('|')).ToList();
        Assert.Equal(5, tableLines.Count); // header, delimiter, 3 data rows
        Assert.All(tableLines, line => Assert.Equal(3, line.Count(ch => ch == '|') - 1));
        Assert.Contains("|  | y | 2. upgrade |", tableLines);
        Assert.Contains("## Procedure", content.Text, StringComparison.Ordinal);

        var table = Assert.Single(content.Tables);
        Assert.Equal("2. upgrade", table.Cells[2][2]);
        Assert.Single(table.MergedCells);
        var span = Assert.Single(content.Spans);
        Assert.Equal(1, span.Page);
        Assert.Equal((0, content.Text.Length), (span.Start, span.End));
    }

    [Fact]
    public async Task Xlsx_ReadAsync_ReportsTheRealSheetNames()
    {
        var result = await new ExcelDocumentReader().ReadAsync(VerticalMergeFixture, TestContext.Current.CancellationToken);

        Assert.Equal("Procedure", Assert.Single(result.Pages).Props["sheet_name"]);
    }

    // ---- refinement never writes a carried table a second time ------------------------------------------------------

    [Fact]
    public async Task Refine_TablesCarriedNextToTheText_AppearOnceInTheRefinedText()
    {
        var content = await new ExcelDocumentReader().ExtractAsync(VerticalMergeFixture, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(content.Tables);
        var refiner = new DocumentRefiner(new MarkdownConverter(), logger: NullLogger<DocumentRefiner>.Instance);

        var refined = await refiner.RefineAsync(content, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, CountOccurrences(refined.Text, "2. upgrade"));
        Assert.Equal(1, CountOccurrences(refined.Text, "3. verify"));
    }

    // ---- chunks keep what the table-aware chunker recorded -------------------------------------------------------

    [Fact]
    public async Task Process_TablePieces_CarryTheChunkersTableKeys()
    {
        await using var processor = DocumentProcessorFactoryBuilder.CreateDefault().Build().Create(VerticalMergeFixture);

        await processor.ProcessAsync(new ProcessingOptions
        {
            Chunking = new ChunkingOptions { Strategy = "Token", MaxChunkSize = 200, OverlapSize = 20 },
            IncludeLlmRefine = false,
        }, TestContext.Current.CancellationToken);

        var table = Assert.Single(processor.Result.Chunks!, c => c.Props.ContainsKey("table"));
        Assert.Equal(true, table.Props["table"]);
        Assert.Equal(0, table.Props["table_index"]);
        Assert.Equal(1, table.Props["table_pieces"]);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
