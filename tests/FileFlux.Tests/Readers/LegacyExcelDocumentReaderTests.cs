using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// LegacyExcelDocumentReader unit tests — BIFF (.xls) extraction to markdown tables.
/// Fixture: Fixtures/legacy-korean.xls (BIFF8, NPOI-generated) — Korean sheet name
/// ("견적서") and cell values, numeric/date/pipe-escape cases, plus an empty sheet.
/// Pins the Korean legacy-document acceptance criterion from the AIMS field report
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
        var result = await _reader.ReadAsync(FixturePath);

        Assert.Equal("LegacyExcelReader", result.ReaderType);
        Assert.Equal(2, result.Pages.Count);
        Assert.Equal("견적서", result.Pages[0].Props["sheet_name"]);
        Assert.Equal("빈시트", result.Pages[1].Props["sheet_name"]);
    }

    [Fact]
    public async Task ExtractAsync_ShouldSerializeKoreanWorkbookAsMarkdownTable()
    {
        var content = await _reader.ExtractAsync(FixturePath);

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
    public async Task ExtractAsync_ShouldSkipEmptySheetsWithWarning()
    {
        var content = await _reader.ExtractAsync(FixturePath);

        Assert.DoesNotContain("## 빈시트", content.Text);
        Assert.Contains(content.Warnings, w => w.Contains("1 of 2 worksheet"));
    }

    [Fact]
    public async Task ExtractAsync_FromStream_ShouldMatchFileExtraction()
    {
        var fromFile = await _reader.ExtractAsync(FixturePath);

        await using var stream = File.OpenRead(FixturePath);
        var fromStream = await _reader.ExtractAsync(stream, "legacy-korean.xls");

        Assert.Equal(fromFile.Text, fromStream.Text);
    }

    [Fact]
    public async Task ExtractAsync_NonBiffPayload_ShouldThrowDocumentProcessingException()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"not-biff-{Guid.NewGuid():N}.xls");
        await File.WriteAllTextAsync(tempPath, "this is not a BIFF workbook");

        try
        {
            await Assert.ThrowsAsync<DocumentProcessingException>(
                () => _reader.ExtractAsync(tempPath));
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
