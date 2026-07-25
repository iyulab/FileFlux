using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;
using System.Text;

namespace FileFlux.Tests.Readers;

/// <summary>
/// ExcelDocumentReader unit tests — modern .xlsx (OOXML) extraction via Undoc (Rust FFI).
///
/// Fixtures (openpyxl-generated, deterministic — see scratchpad gen_xlsx_fixtures.py):
/// - Fixtures/list-simple.xlsx — single sheet "유지보수수행사", header + 12 Korean data rows.
/// - Fixtures/list-offset-multisheet.xlsx — sheet1 "수행사목록" (2 leading blank rows +
///   offset title + header + 8 rows) and sheet2 "요약" (summary table).
///
/// These pin the AIMS field report (`ISSUE-FileFlux-20260724-xlsx-extraction-near-empty`):
/// modern .xlsx serialization delegates 100% to Undoc's ToMarkdown, and the reported
/// "near-empty / 1-chunk" symptom was NOT reproducible — a multi-row list extracts in full.
/// The row-loss guard below (asserting the FIRST, a MID, and the LAST data row) is what makes
/// this fixture bite: a truncated extraction that keeps only the header would still pass a
/// header-only assertion, which is precisely the failure mode under suspicion.
///
/// Assertions are split by ownership:
/// - FileFlux managed contract (hints, Trim, worksheet_count) — what FileFlux can regress alone.
/// - Delegated Undoc serialization (row/cell presence, multi-sheet, blank-leading absorption) —
///   guards the FileFlux↔Undoc integration boundary across Undoc bumps.
/// No exact character-count assertion: Undoc formatting shifts on bump and would make the test
/// brittle (convention: LegacyExcelDocumentReaderTests asserts zero char counts).
/// </summary>
public class ExcelDocumentReaderTests
{
    private static readonly string SimpleListFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "list-simple.xlsx");

    private static readonly string MultiSheetFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "list-offset-multisheet.xlsx");

    private readonly ExcelDocumentReader _reader = new();

    [Fact]
    public void ReaderType_ShouldReturnExcelReader()
    {
        Assert.Equal("ExcelReader", _reader.ReaderType);
    }

    [Fact]
    public void SupportedExtensions_ShouldIncludeXlsx()
    {
        Assert.Contains(".xlsx", _reader.SupportedExtensions);
    }

    [Theory]
    [InlineData("test.xlsx", true)]
    [InlineData("TEST.XLSX", true)]
    [InlineData("workbook.xlsx", true)]
    [InlineData("test.xls", false)] // legacy BIFF handled by LegacyExcelDocumentReader
    [InlineData("test.csv", false)]
    [InlineData("test.pdf", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CanRead_ShouldReturnCorrectResult(string? fileName, bool expected)
    {
        Assert.Equal(expected, _reader.CanRead(fileName!));
    }

    // ----- Argument / guard behavior -----

    [Fact]
    public async Task ExtractAsync_WithNullFilePath_ShouldThrowArgumentException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _reader.ExtractAsync((string)null!, null, CancellationToken.None));
        Assert.Contains("File path cannot be null or empty", exception.Message);
    }

    [Fact]
    public async Task ExtractAsync_WithEmptyFilePath_ShouldThrowArgumentException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _reader.ExtractAsync("", null, CancellationToken.None));
        Assert.Contains("File path cannot be null or empty", exception.Message);
    }

    [Fact]
    public async Task ExtractAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
    {
        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _reader.ExtractAsync("non-existent-file.xlsx", null, CancellationToken.None));
        Assert.Contains("Excel document not found", exception.Message);
    }

    [Fact]
    public async Task ExtractAsync_WithUnsupportedExtension_ShouldThrowArgumentException()
    {
        var tempFile = Path.GetTempFileName();
        var wrongExtFile = Path.ChangeExtension(tempFile, ".pdf");
        File.Move(tempFile, wrongExtFile);
        try
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                _reader.ExtractAsync(wrongExtFile, null, CancellationToken.None));
            Assert.Contains("File format not supported", exception.Message);
        }
        finally
        {
            if (File.Exists(wrongExtFile)) File.Delete(wrongExtFile);
        }
    }

    [Fact]
    public async Task ExtractAsync_WithNullStream_ShouldThrowArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _reader.ExtractAsync((Stream)null!, "test.xlsx", null, CancellationToken.None));
        Assert.Equal("stream", exception.ParamName);
    }

    [Fact]
    public async Task ExtractAsync_StreamWithUnsupportedExtension_ShouldThrowArgumentException()
    {
        using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes("test content"));
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _reader.ExtractAsync(memoryStream, "test.pdf", null, CancellationToken.None));
        Assert.Contains("File format not supported", exception.Message);
    }

    [Fact]
    public async Task ExtractAsync_InvalidXlsxPayload_ShouldThrowDocumentProcessingException()
    {
        // A .xlsx name with non-OOXML bytes must surface as a wrapped processing failure,
        // never as a silent empty result.
        using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes("this is not an OOXML package"));
        await Assert.ThrowsAsync<DocumentProcessingException>(() =>
            _reader.ExtractAsync(memoryStream, "broken.xlsx", null, CancellationToken.None));
    }

    // ----- Read stage: sheets → pages -----

    [Fact]
    public async Task ReadAsync_ShouldReportEachSheetAsPage()
    {
        var result = await _reader.ReadAsync(MultiSheetFixture);

        Assert.Equal("ExcelReader", result.ReaderType);
        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(2, result.DocumentProps["section_count"]);
        Assert.All(result.Pages, p => Assert.Equal("excel_worksheet", p.Props["file_type"]));
    }

    // ----- Extract stage: FileFlux managed contract (regressable by FileFlux alone) -----

    [Fact]
    public async Task ExtractAsync_SimpleList_ShouldFollowExcelReaderContract()
    {
        var content = await _reader.ExtractAsync(SimpleListFixture);

        Assert.Equal("ExcelReader", content.ReaderType);
        Assert.Equal(".xlsx", content.File.Extension);
        Assert.Equal(1, content.Hints["worksheet_count"]);
        Assert.Equal("excel_workbook", content.Hints["file_type"]);
        Assert.Equal("undoc_native", content.Hints["conversion_method"]);
        Assert.Equal(true, content.Hints["has_tables"]);
        Assert.True((int)content.Hints["character_count"] >= 0);

        // Text is trimmed (managed post-processing) and non-empty for a populated sheet.
        Assert.Equal(content.Text.Trim(), content.Text);
        Assert.NotEmpty(content.Text);
    }

    // ----- Extract stage: delegated Undoc serialization (row-loss guard) -----

    [Fact]
    public async Task ExtractAsync_SimpleList_ShouldPreserveEveryDataRow()
    {
        var content = await _reader.ExtractAsync(SimpleListFixture);

        // Sheet heading + header row
        Assert.Contains("## 유지보수수행사", content.Text);
        Assert.Contains("| 번호 | 수행사 | 담당자 | 계약일 |", content.Text);

        // FIRST, a MID, and the LAST data row must all survive — a truncated extraction that
        // kept only the header (the reported near-empty symptom) fails on the mid/last asserts.
        Assert.Contains("| 1 | 가나전산 | 김철수 | 2026-01-05 |", content.Text);   // first
        Assert.Contains("| 6 | 카타정보 | 강도현 | 2026-02-09 |", content.Text);   // mid
        Assert.Contains("| 12 | 엠엔오솔루션 | 배진우 | 2026-03-23 |", content.Text); // last (unique token)
    }

    [Fact]
    public async Task ExtractAsync_MultiSheetWithOffset_ShouldExtractAllSheets()
    {
        var content = await _reader.ExtractAsync(MultiSheetFixture);

        // Both worksheets are surfaced (section_count → worksheet_count hint).
        Assert.Equal(2, content.Hints["worksheet_count"]);

        // Sheet 1: leading blank rows are absorbed, offset title survives, and every vendor
        // row (including the real "구분|업체명|지역" header now shifted into the body) is present.
        Assert.Contains("## 수행사목록", content.Text);
        Assert.Contains("2026년도 유지보수 수행사 현황", content.Text); // offset title preserved
        Assert.Contains("| 구분 | 업체명 | 지역 |", content.Text);        // shifted header row preserved
        Assert.Contains("| 정기 | 가나전산 | 서울 |", content.Text);      // first data row
        Assert.Contains("| 정기 | 에이비씨소프트 | 울산 |", content.Text); // last data row of sheet 1

        // Sheet 2 fully extracted after the section separator.
        Assert.Contains("## 요약", content.Text);
        Assert.Contains("| 총수행사 | 8 |", content.Text);
        Assert.Contains("| 정기계약 | 5 |", content.Text); // last row of sheet 2 (unique token)
    }

    // ----- File vs stream parity (the two extraction paths are copy-paste twins) -----

    [Theory]
    [InlineData("list-simple.xlsx")]
    [InlineData("list-offset-multisheet.xlsx")]
    public async Task ExtractAsync_FromStream_ShouldMatchFileExtraction(string fixtureName)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName);

        var fromFile = await _reader.ExtractAsync(fixturePath);

        await using var stream = File.OpenRead(fixturePath);
        var fromStream = await _reader.ExtractAsync(stream, fixtureName);

        Assert.Equal(fromFile.Text, fromStream.Text);
        Assert.Equal(fromFile.Hints["worksheet_count"], fromStream.Hints["worksheet_count"]);
        Assert.Equal(fromFile.Hints["conversion_method"], fromStream.Hints["conversion_method"]);
    }
}
