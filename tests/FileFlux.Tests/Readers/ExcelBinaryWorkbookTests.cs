using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Binary Excel workbooks (<c>.xls</c>) are read by <see cref="ExcelDocumentReader"/>, through the same parser and into
/// the same layout as <c>.xlsx</c>: each sheet a <c>## name</c> heading followed by its cells as one table.
///
/// <para>
/// Fixtures, one per BIFF generation that reaches users:
/// - <c>legacy-korean.xls</c> — BIFF8 (Excel 97-2003, NPOI-generated): Korean sheet name ("견적서") and cells,
///   numeric/date/pipe-escape cases, plus an empty sheet ("빈시트"). Pins the Korean legacy-document acceptance
///   criterion from a consumer field report (2023-2026 era .xls quotations failing with "No reader found").
/// - <c>legacy-biff5.xls</c> — BIFF5 (Excel 5.0/95, a compound file with a <c>Book</c> stream).
/// - <c>legacy-biff4.xls</c> — BIFF4 (Excel 4.0): a bare record stream, no compound file around it.
/// The last two are Apache POI test data (see <c>Fixtures/NOTICE-apache-poi.txt</c>).
/// </para>
/// </summary>
public class ExcelBinaryWorkbookTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static readonly string KoreanFixture = Fixture("legacy-korean.xls");
    private static readonly string Biff5Fixture = Fixture("legacy-biff5.xls");
    private static readonly string Biff4Fixture = Fixture("legacy-biff4.xls");

    private readonly ExcelDocumentReader _reader = new();

    [Fact]
    public async Task ReadAsync_ShouldReportSheetsAsPages()
    {
        var result = await _reader.ReadAsync(KoreanFixture, TestContext.Current.CancellationToken);

        Assert.Equal("ExcelReader", result.ReaderType);
        Assert.Equal(".xls", result.File.Extension);
        Assert.Equal(2, result.Pages.Count);
        Assert.Equal("견적서", result.Pages[0].Props["sheet_name"]);
        Assert.Equal("빈시트", result.Pages[1].Props["sheet_name"]);
    }

    [Fact]
    public async Task ExtractAsync_ShouldSerializeKoreanWorkbookAsMarkdownTable()
    {
        var content = await _reader.ExtractAsync(KoreanFixture, cancellationToken: TestContext.Current.CancellationToken);

        // Sheet heading + header row + data preserved (Korean acceptance criterion)
        Assert.Contains("## 견적서", content.Text);
        Assert.Contains("| 품목 | 수량 | 단가 | 납기일 |", content.Text);
        Assert.Contains("공조기 FW410", content.Text);
        Assert.Contains("| 2 |", content.Text);
        Assert.Contains("1250000.5", content.Text);
        Assert.Contains("2026-03-26", content.Text);

        // Pipe character must be escaped to keep the markdown table intact
        Assert.Contains(@"설치\|시공 비용", content.Text);

        // The container that was parsed, and the Excel reader's hints
        Assert.Equal(".xls", content.File.Extension);
        Assert.Equal("ExcelReader", content.ReaderType);
        Assert.Equal(2, content.Hints["worksheet_count"]);
        Assert.Equal(true, content.Hints["has_tables"]);
        Assert.Equal("undoc_native", content.Hints["conversion_method"]);
    }

    [Fact]
    public async Task ExtractAsync_ReturnsEachSheetAsASpan_AndTheDataSheetAsATable()
    {
        var content = await _reader.ExtractAsync(KoreanFixture, cancellationToken: TestContext.Current.CancellationToken);

        // The data sheet yields a table; the empty sheet none.
        var table = Assert.Single(content.Tables);
        Assert.Equal(["품목", "수량", "단가", "납기일"], table.Cells[0]);
        Assert.Contains(table.Cells, row => row.Contains("공조기 FW410"));
        Assert.Contains(table.Cells, row => row.Contains("설치|시공 비용")); // data keeps the pipe; only the text escapes it
        Assert.True(table.HasHeader);
        Assert.Equal(1, table.PageNumber);
        Assert.Equal("견적서", table.Props["section_name"]);
        Assert.Equal(TableDetectionMethod.Structured, table.DetectionMethod);

        // Every sheet is a span, as in an .xlsx; the table's text block is inside its sheet's span, so a consumer can
        // tie the two together.
        Assert.Equal([1, 2], content.Spans.Select(span => span.Page ?? 0).ToArray());
        var sheetText = content.Text[content.Spans[0].Start..content.Spans[0].End];
        Assert.StartsWith("## 견적서", sheetText, StringComparison.Ordinal);
        Assert.Contains(TableMarkdown.Render(table), sheetText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_AnEmptySheetKeepsItsHeading_AsInAnXlsx()
    {
        var content = await _reader.ExtractAsync(KoreanFixture, cancellationToken: TestContext.Current.CancellationToken);

        var emptySheet = content.Text[content.Spans[1].Start..content.Spans[1].End];
        Assert.Equal("## 빈시트", emptySheet);
    }

    [Fact]
    public async Task ExtractAsync_FromStream_ShouldMatchFileExtraction()
    {
        var fromFile = await _reader.ExtractAsync(KoreanFixture, cancellationToken: TestContext.Current.CancellationToken);

        await using var stream = File.OpenRead(KoreanFixture);
        var fromStream = await _reader.ExtractAsync(stream, "legacy-korean.xls", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(fromFile.Text, fromStream.Text);
        Assert.Equal(".xls", fromStream.File.Extension);
    }

    [Fact]
    public async Task AnExcel95Workbook_IsRead()
    {
        var content = await _reader.ExtractAsync(Biff5Fixture, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("Sample Excel Worksheet - Numbers and their Squares", content.Text);
        Assert.Contains("## Feuil1", content.Text);
        var table = Assert.Single(content.Tables);
        Assert.Contains(table.Cells, row => row.Contains("Square"));
        Assert.Contains(table.Cells, row => row.Contains("225"));
        Assert.Equal(".xls", content.File.Extension);
        Assert.Equal(".xls", FormatSignature.DetectFile(Biff5Fixture));
    }

    [Fact]
    public async Task AnExcel4Workbook_WithNoCompoundFileAroundIt_IsRead()
    {
        var content = await _reader.ExtractAsync(Biff4Fixture, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("Table 10 -- Examination Coverage", content.Text);
        var table = Assert.Single(content.Tables);
        Assert.Contains(table.Cells, row => row.Contains("United States, total [2]"));
        // A bare BIFF stream is no Office container, yet what was parsed is still an .xls.
        Assert.Equal(OfficeContainer.Unknown, ContainerSignature.DetectFile(Biff4Fixture));
        Assert.Equal(".xls", FormatSignature.DetectFile(Biff4Fixture));
        Assert.Equal(".xls", content.File.Extension);

        await using var stream = File.OpenRead(Biff4Fixture);
        var fromStream = await _reader.ExtractAsync(stream, "coverage.xls", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(content.Text, fromStream.Text);
        Assert.Equal(".xls", fromStream.File.Extension);
    }

    [Theory]
    [InlineData(new byte[] { 0x09, 0x00, 0x04, 0x00, 0x02, 0x00, 0x10, 0x00 }, ".xls")] // BIFF2 worksheet
    [InlineData(new byte[] { 0x09, 0x02, 0x06, 0x00, 0x00, 0x00, 0x10, 0x00 }, ".xls")] // BIFF3 worksheet
    [InlineData(new byte[] { 0x09, 0x04, 0x06, 0x00, 0x00, 0x00, 0x00, 0x01 }, ".xls")] // BIFF4 workbook
    [InlineData(new byte[] { 0x09, 0x04, 0x08, 0x00, 0x00, 0x00, 0x10, 0x00 }, null)] // BIFF4 id, wrong BOF length
    [InlineData(new byte[] { 0x09, 0x02, 0x06, 0x00, 0x00, 0x00, 0x77, 0x00 }, null)] // unknown substream type
    [InlineData(new byte[] { 0x09, 0x08, 0x10, 0x00, 0x00, 0x06, 0x05, 0x00 }, null)] // BIFF8 BOF lives in a compound file
    [InlineData(new byte[] { 0x09, 0x00, 0x04, 0x00 }, null)] // too short to tell
    public void Detect_ABareBiffStream_ByItsBofRecord(byte[] prefix, string? expected)
    {
        Assert.Equal(expected, FormatSignature.Detect(prefix));
    }

    [Fact]
    public async Task ADamagedExcel4Workbook_KeepsTheParsersDiagnosis_RatherThanBeingCalledMislabelled()
    {
        // A bare BIFF stream is neither Office container, so a container-only check would annotate its failure as a
        // file whose extension lies. The content is an .xls; what is wrong with it is the damage.
        var tempPath = Path.Combine(Path.GetTempPath(), $"truncated-biff4-{Guid.NewGuid():N}.xls");
        await File.WriteAllBytesAsync(tempPath, File.ReadAllBytes(Biff4Fixture)[..24], TestContext.Current.CancellationToken);

        try
        {
            var ex = await Assert.ThrowsAsync<DocumentProcessingException>(
                () => _reader.ExtractAsync(tempPath, cancellationToken: TestContext.Current.CancellationToken));

            Assert.DoesNotContain("container_mismatch", ex.Message);
            Assert.Contains("extraction_error_kind=", ex.Message);
        }
        finally
        {
            File.Delete(tempPath);
        }
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
