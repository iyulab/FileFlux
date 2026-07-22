using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using FileFlux.Infrastructure.Factories;
using Xunit;
using System.Text;

namespace FileFlux.Tests.Readers;

/// <summary>
/// CsvDocumentReader unit tests - structure-aware CSV/TSV extraction
/// </summary>
public class CsvDocumentReaderTests
{
    private readonly CsvDocumentReader _reader;

    public CsvDocumentReaderTests()
    {
        _reader = new CsvDocumentReader();
    }

    [Fact]
    public void ReaderType_ShouldReturnCsvReader()
    {
        Assert.Equal("CsvReader", _reader.ReaderType);
    }

    [Fact]
    public void SupportedExtensions_ShouldIncludeCsvAndTsv()
    {
        Assert.Contains(".csv", _reader.SupportedExtensions);
        Assert.Contains(".tsv", _reader.SupportedExtensions);
    }

    [Theory]
    [InlineData("data.csv", true)]
    [InlineData("REPORT.CSV", true)]
    [InlineData("data.tsv", true)]
    [InlineData("document.txt", false)]
    [InlineData("file.xlsx", false)]
    public void CanRead_ShouldReturnCorrectResult(string filePath, bool expected)
    {
        Assert.Equal(expected, _reader.CanRead(filePath));
    }

    [Fact]
    public async Task ExtractAsync_ShouldSerializeHeaderAwareMarkdownTable()
    {
        // Arrange
        var csv = "Name,Age,City\nKim,30,Seoul\nLee,25,Busan\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        // Act
        var result = await _reader.ExtractAsync(stream, "people.csv");

        // Assert - header row + separator + data rows as markdown table
        Assert.Contains("| Name | Age | City |", result.Text);
        Assert.Contains("| --- | --- | --- |", result.Text);
        Assert.Contains("| Kim | 30 | Seoul |", result.Text);
        Assert.Contains("| Lee | 25 | Busan |", result.Text);
    }

    [Fact]
    public async Task ExtractAsync_Tsv_ShouldUseTabDelimiter()
    {
        // Arrange
        var tsv = "Col1\tCol2\nval1\tval2\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(tsv));

        // Act
        var result = await _reader.ExtractAsync(stream, "data.tsv");

        // Assert
        Assert.Contains("| Col1 | Col2 |", result.Text);
        Assert.Contains("| val1 | val2 |", result.Text);
    }

    [Fact]
    public async Task ExtractAsync_Cp949EncodedStream_ShouldDecodeKorean()
    {
        // Arrange - CP949(EUC-KR) encoded Korean CSV (AIMS field case: legacy Excel export)
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        var csv = "이름,부서\n김철수,영업팀\n";
        using var stream = new MemoryStream(cp949.GetBytes(csv));

        // Act
        var result = await _reader.ExtractAsync(stream, "직원.csv");

        // Assert - decoded via CP949 fallback, not mojibake
        Assert.Contains("| 이름 | 부서 |", result.Text);
        Assert.Contains("| 김철수 | 영업팀 |", result.Text);
    }

    [Fact]
    public async Task ExtractAsync_QuotedFieldWithCommaAndNewline_ShouldPreserveCellIntegrity()
    {
        // Arrange - RFC 4180: quoted field containing delimiter and line break
        var csv = "Product,Note\nWidget,\"small, blue\nfragile\"\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        // Act
        var result = await _reader.ExtractAsync(stream, "products.csv");

        // Assert - embedded comma preserved inside a single cell,
        // embedded newline must not break the markdown table row
        Assert.Contains("| Widget | small, blue<br>fragile |", result.Text);
    }

    [Fact]
    public async Task ExtractAsync_PipeInCell_ShouldEscapeForMarkdown()
    {
        // Arrange
        var csv = "Key,Value\na|b,c\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        // Act
        var result = await _reader.ExtractAsync(stream, "pipes.csv");

        // Assert
        Assert.Contains(@"| a\|b | c |", result.Text);
    }

    [Fact]
    public async Task ExtractAsync_ShouldPopulateStructuralHints()
    {
        // Arrange
        var csv = "A,B\n1,2\n3,4\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        // Act
        var result = await _reader.ExtractAsync(stream, "hints.csv");

        // Assert
        Assert.Equal("CsvReader", result.ReaderType);
        Assert.True((bool)result.Hints["has_tables"]);
        Assert.Equal(2, (int)result.Hints["column_count"]);
        Assert.Equal(2, (int)result.Hints["row_count"]); // data rows excluding header
    }

    [Fact]
    public async Task ExtractAsync_EmptyFile_ShouldReturnEmptyTextWithWarning()
    {
        // Arrange
        using var stream = new MemoryStream();

        // Act
        var result = await _reader.ExtractAsync(stream, "empty.csv");

        // Assert
        Assert.Equal(string.Empty, result.Text);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public async Task ExtractAsync_FilePath_ShouldExtractSameAsStream()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), $"fileflux-csv-test-{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(path, "H1,H2\nv1,v2\n", Encoding.UTF8);

        try
        {
            // Act
            var result = await _reader.ExtractAsync(path);

            // Assert
            Assert.Contains("| H1 | H2 |", result.Text);
            Assert.Contains("| v1 | v2 |", result.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnSinglePageStructure()
    {
        // Arrange
        var csv = "A,B\n1,2\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        // Act
        var result = await _reader.ReadAsync(stream, "read.csv");

        // Assert
        Assert.Equal("CsvReader", result.ReaderType);
        Assert.Single(result.Pages);
        Assert.True(result.Pages[0].HasContent);
    }

    [Fact]
    public void DocumentReaderFactory_DefaultRegistration_ShouldResolveCsvAndTsv()
    {
        // Regression for AIMS "No reader found for: *.csv"
        var factory = new DocumentReaderFactory();

        Assert.NotNull(factory.GetReader("report.csv"));
        Assert.NotNull(factory.GetReader("report.tsv"));
    }

    [Theory]
    [InlineData(".csv", DocumentType.Csv)]
    [InlineData(".tsv", DocumentType.Csv)]
    public void DocumentType_ShouldMapCsvFamilyExtensions(string extension, DocumentType expected)
    {
        Assert.Equal(expected, DocumentTypeExtensions.FromFilePath($"file{extension}"));
        Assert.Contains(extension, expected.GetExtensions());
    }
}
