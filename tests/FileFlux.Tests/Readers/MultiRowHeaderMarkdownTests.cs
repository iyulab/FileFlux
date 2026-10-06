using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// A header of several rows (a merged group row above column labels) is written as one GFM header row naming every
/// column (<c>group / label</c>), so a table chunk — which repeats only the header row — still names its columns.
/// </summary>
public class MultiRowHeaderMarkdownTests
{
    [Fact]
    public void AGroupRowAboveLabels_BecomesOneHeaderRow()
    {
        var table = new TableData
        {
            Cells =
            [
                ["Basic", "", "Contract", ""],
                ["No", "Name", "Vendor", "Amount"],
                ["1", "Alpha", "Acme", "100"],
            ],
            MergedCells =
            [
                new MergedCell { StartRow = 0, EndRow = 0, StartCol = 0, EndCol = 1, Content = "Basic" },
                new MergedCell { StartRow = 0, EndRow = 0, StartCol = 2, EndCol = 3, Content = "Contract" },
            ],
        };

        var lines = TableMarkdown.Render(table).Split('\n');

        Assert.Equal("| Basic / No | Basic / Name | Contract / Vendor | Contract / Amount |", lines[0]);
        Assert.Equal("| --- | --- | --- | --- |", lines[1]);
        Assert.Equal("| 1 | Alpha | Acme | 100 |", lines[2]);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void AVerticallyMergedLabel_IsWrittenOnce()
    {
        var table = new TableData
        {
            Cells =
            [
                ["No", "Contract", ""],
                ["", "Vendor", "Amount"],
                ["1", "Acme", "100"],
            ],
            MergedCells =
            [
                new MergedCell { StartRow = 0, EndRow = 1, StartCol = 0, EndCol = 0, Content = "No" },
                new MergedCell { StartRow = 0, EndRow = 0, StartCol = 1, EndCol = 2, Content = "Contract" },
            ],
        };

        Assert.StartsWith("| No | Contract / Vendor | Contract / Amount |\n", TableMarkdown.Render(table), StringComparison.Ordinal);
    }

    [Fact]
    public void ASingleHeaderRow_IsUnchanged()
    {
        var table = new TableData { Cells = [["No", "Name"], ["1", "Alpha"]] };

        Assert.Equal("| No | Name |\n| --- | --- |\n| 1 | Alpha |", TableMarkdown.Render(table));
    }

    [Fact]
    public void ATwoRowTableWithAGroupRow_KeepsItsBodyRow()
    {
        var table = new TableData
        {
            Cells = [["Group", ""], ["1", "2"]],
            MergedCells = [new MergedCell { StartRow = 0, EndRow = 0, StartCol = 0, EndCol = 1, Content = "Group" }],
        };

        Assert.EndsWith("| 1 | 2 |", TableMarkdown.Render(table), StringComparison.Ordinal);
    }

    /// <summary>The spreadsheet fixture: a merged group row over column labels, as a real workbook stores it.</summary>
    [Fact]
    public async Task Xlsx_MergedGroupRow_NamesEveryColumnInTheHeader()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "merged-2row-header.xlsx");
        var content = await new ExcelDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        var lines = content.Text.Split('\n');
        var header = Array.FindIndex(lines, l => l.StartsWith("| 기본 정보", StringComparison.Ordinal));
        Assert.True(header >= 0, content.Text);
        Assert.Contains("기본 정보 / 연번", lines[header], StringComparison.Ordinal);
        Assert.Contains("담당자 / 이메일", lines[header], StringComparison.Ordinal);
        Assert.StartsWith("| --- |", lines[header + 1], StringComparison.Ordinal);
        Assert.StartsWith("| 1 |", lines[header + 2], StringComparison.Ordinal);
    }
}
