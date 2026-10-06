using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// LegacyExcelDocumentReader unit tests — BIFF (.xls) extraction to markdown tables.
/// Fixture: Fixtures/legacy-korean.xls (BIFF8, NPOI-generated) — Korean sheet name
/// ("견적서") and cell values, numeric/date/pipe-escape cases, plus an empty sheet.
/// Pins the Korean legacy-document acceptance criterion from a consumer field report
/// (2023-2026 era .xls quotations failing with "No reader found").
/// </summary>
public class LegacyExcelDocumentReaderTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-korean.xls");

    private readonly LegacyExcelDocumentReader _reader = new();

    [Fact]
    public void ReaderType_ShouldReturnLegacyExcelReader()
    {
        Assert.Equal("LegacyExcelReader", _reader.ReaderType);
    }

    [Theory]
    [InlineData("report.xls", true)]
    [InlineData("REPORT.XLS", true)]
    [InlineData("report.xlsx", false)]
    [InlineData("report.csv", false)]
    public void CanRead_ShouldMatchOnlyXls(string fileName, bool expected)
    {
        Assert.Equal(expected, _reader.CanRead(fileName));
    }

    [Fact]
    public async Task ReadAsync_ShouldReportSheetsAsPages()
    {
        var result = await _reader.ReadAsync(FixturePath, TestContext.Current.CancellationToken);

        Assert.Equal("LegacyExcelReader", result.ReaderType);
        Assert.Equal(2, result.Pages.Count);
        Assert.Equal("견적서", result.Pages[0].Props["sheet_name"]);
        Assert.Equal("빈시트", result.Pages[1].Props["sheet_name"]);
    }

    [Fact]
    public async Task ExtractAsync_ShouldSerializeKoreanWorkbookAsMarkdownTable()
    {
        var content = await _reader.ExtractAsync(FixturePath, cancellationToken: TestContext.Current.CancellationToken);

        // Sheet heading + header row + data preserved (Korean acceptance criterion)
        Assert.Contains("## 견적서", content.Text);
        Assert.Contains("| 품목 | 수량 | 단가 | 납기일 |", content.Text);
        Assert.Contains("공조기 FW410", content.Text);
        Assert.Contains("| 2 |", content.Text);
        Assert.Contains("1250000.5", content.Text);
        Assert.Contains("2026-03-26", content.Text);

        // Pipe character must be escaped to keep the markdown table intact
        Assert.Contains(@"설치\|시공 비용", content.Text);

        // Hints follow the Excel reader contract
        Assert.Equal(2, content.Hints["worksheet_count"]);
        Assert.Equal(true, content.Hints["has_tables"]);
        Assert.Equal("exceldatareader", content.Hints["conversion_method"]);
    }

    [Fact]
    public async Task ExtractAsync_ReturnsEachSheetAsATableAndASpan()
    {
        var content = await _reader.ExtractAsync(FixturePath, cancellationToken: TestContext.Current.CancellationToken);

        // The empty sheet yields neither a table nor a span; the data sheet yields one of each.
        var table = Assert.Single(content.Tables);
        Assert.Equal(["품목", "수량", "단가", "납기일"], table.Cells[0]);
        Assert.Contains(table.Cells, row => row.Contains("공조기 FW410"));
        Assert.Contains(table.Cells, row => row.Contains("설치|시공 비용")); // data keeps the pipe; only the text escapes it
        Assert.True(table.HasHeader);
        Assert.Equal(1, table.PageNumber);
        Assert.Equal("견적서", table.Props["section_name"]);
        Assert.Equal(TableDetectionMethod.Structured, table.DetectionMethod);

        // The table's text block is inside the sheet's span, so a consumer can tie the two together.
        var span = Assert.Single(content.Spans);
        Assert.Equal(1, span.Page);
        var sheetText = content.Text[span.Start..span.End];
        Assert.StartsWith("## 견적서", sheetText, StringComparison.Ordinal);
        Assert.Contains(TableMarkdown.Render(table), sheetText, StringComparison.Ordinal);
    }

    [Fact]
    public void ToTable_TrimsEmptyEdgesAndKeepsMergedRanges()
    {
        var rows = new List<string[]>
        {
            "그룹,항목,,".Split(','),
            "A,x,,".Split(','),
            ",y,,".Split(','),
            ",,,".Split(','),
        };

        var table = LegacyExcelDocumentReader.ToTable(rows, [(FromRow: 1, FromColumn: 0, ToRow: 2, ToColumn: 0)], sheetNumber: 3, sheetName: "S", order: 0)!;

        Assert.Equal([["그룹", "항목"], ["A", "x"], ["", "y"]], table.Cells);
        var merged = Assert.Single(table.MergedCells);
        Assert.Equal((1, 2, 0, 0, "A"), (merged.StartRow, merged.EndRow, merged.StartCol, merged.EndCol, merged.Content));
        Assert.Equal(3, table.PageNumber);
        Assert.Null(LegacyExcelDocumentReader.ToTable([", ".Split(',')], [], 1, "empty", 0));
    }

    [Fact]
    public async Task ExtractAsync_ShouldSkipEmptySheetsWithWarning()
    {
        var content = await _reader.ExtractAsync(FixturePath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("## 빈시트", content.Text);
        Assert.Contains(content.Warnings, w => w.Contains("1 of 2 worksheet"));
    }

    [Fact]
    public async Task ExtractAsync_FromStream_ShouldMatchFileExtraction()
    {
        var fromFile = await _reader.ExtractAsync(FixturePath, cancellationToken: TestContext.Current.CancellationToken);

        await using var stream = File.OpenRead(FixturePath);
        var fromStream = await _reader.ExtractAsync(stream, "legacy-korean.xls", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(fromFile.Text, fromStream.Text);
    }

    [Fact]
    public async Task ExtractAsync_NonBiffPayload_ShouldThrowDocumentProcessingException()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"not-biff-{Guid.NewGuid():N}.xls");
        await File.WriteAllTextAsync(tempPath, "this is not a BIFF workbook", TestContext.Current.CancellationToken);

        try
        {
            await Assert.ThrowsAsync<DocumentProcessingException>(
                () => _reader.ExtractAsync(tempPath, cancellationToken: TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
