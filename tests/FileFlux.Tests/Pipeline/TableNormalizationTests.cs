using FileFlux.Core;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Conversion;
using Xunit;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// The refine path (Markdown converter + normaliser) must not damage a table: a header row a parser separated from its
/// body by a blank line stays the header, the delimiter row matches the header's width, a consistent table is not broken
/// up because one cell is long, and a table that cannot stay GFM keeps its column positions.
/// </summary>
public class TableNormalizationTests
{
    private static readonly NormalizationOptions RefineTableOptions = new()
    {
        NormalizeTables = true,
        MaxColumnVariance = 0,
    };

    // The shape a PDF parser writes: header row, blank line, body rows, no delimiter row.
    private const string DetachedHeader = """
        | Group | Plan | Done |

        | Banks | 5,025 | 930 |
        | Total | 9,076 | 1,265 |
        """;

    [Fact]
    public void Normalize_HeaderDetachedByABlankLine_StaysTheHeaderOfOneTable()
    {
        var result = new MarkdownNormalizer().Normalize(DetachedHeader, RefineTableOptions);

        var lines = result.Markdown.Trim().Split('\n');
        Assert.Equal("| Group | Plan | Done |", lines[0]);
        Assert.Equal("| --- | --- | --- |", lines[1]);
        Assert.Equal("| Banks | 5,025 | 930 |", lines[2]);
        Assert.DoesNotContain("<table>", result.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_HeaderDetachedByABlankLine_DoesNotPromoteADataRow()
    {
        var raw = new RawContent { Text = DetachedHeader, File = new SourceFileInfo { Name = "a.pdf", Extension = ".pdf" } };

        var result = await new MarkdownConverter().ConvertAsync(raw, new MarkdownConversionOptions { ConvertTables = true },
            TestContext.Current.CancellationToken);

        var lines = result.Markdown.Split('\n').Where(l => l.StartsWith('|')).ToList();
        Assert.Equal("| Group | Plan | Done |", lines[0]);
        Assert.Equal("| --- | --- | --- |", lines[1]);
        Assert.Equal(4, lines.Count);
    }

    [Fact]
    public async Task Convert_PipeBlockWithoutDelimiter_GetsAnEmptyHeaderAsWideAsTheRows()
    {
        var raw = new RawContent
        {
            Text = "|  | Bank | Officer |\n| A | B | C |",
            File = new SourceFileInfo { Name = "a.pdf", Extension = ".pdf" },
        };

        var result = await new MarkdownConverter().ConvertAsync(raw, new MarkdownConversionOptions { ConvertTables = true },
            TestContext.Current.CancellationToken);

        var lines = result.Markdown.Split('\n').Where(l => l.StartsWith('|')).ToList();
        Assert.Equal("|  |  |  |", lines[0]);
        Assert.Equal("| --- | --- | --- |", lines[1]);
        Assert.Equal("|  | Bank | Officer |", lines[2]);
    }

    [Fact]
    public void Normalize_ConsistentTableWithOneLongCell_IsKept()
    {
        var longCell = new string('x', 300);
        var markdown = $"| A | B |\n| --- | --- |\n| {longCell} | 1 |\n| y | 2 |";

        var result = new MarkdownNormalizer().Normalize(markdown, RefineTableOptions);

        Assert.DoesNotContain("<table>", result.Markdown, StringComparison.Ordinal);
        Assert.Contains($"| {longCell} | 1 |", result.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_TableThatCannotStayGfm_KeepsItsColumnPositions()
    {
        // Rows of different widths cannot be one GFM table; the leading empty cell still places "9,076".
        var markdown = "| Group | Item | Amount |\n| --- | --- | --- |\n| Banks | x | 5,025 |\n|  | 9,076 |";

        var result = new MarkdownNormalizer().Normalize(markdown, RefineTableOptions);

        Assert.Contains("<table>", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("|  | 9,076 |", result.Markdown, StringComparison.Ordinal);
    }
}
