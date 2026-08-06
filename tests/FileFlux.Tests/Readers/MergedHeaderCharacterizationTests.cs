using FileFlux.Core.Infrastructure.Readers;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Characterization tests for multi-row (merged-cell) spreadsheet headers.
///
/// <para>
/// These pin behavior that is <b>known to be wrong</b>. Modern <c>.xlsx</c> serialization is
/// delegated in full to Undoc, and a workbook whose header spans two rows — one merged row of
/// group labels above a row of column labels, a shape common in real reporting spreadsheets —
/// comes back with its structure collapsed. No error, no warning: the output is simply a table
/// that does not match the input.
/// </para>
///
/// <para>
/// The point of pinning a defect is the reversal. When the upstream fix lands, these tests fail,
/// and the failure is the signal that the expectations here should be inverted into a real
/// contract. Without them an Undoc bump could fix this quietly and nobody would know to remove
/// the consumer-side caveats that exist because of it — or, worse, could change the shape of the
/// breakage in a way no test would notice.
/// </para>
///
/// <para>
/// Fixtures are deterministic and self-contained (openpyxl-generated):
/// <list type="bullet">
/// <item><c>Fixtures/merged-2row-header.xlsx</c> — row 1 merged group labels over 4 spans,
/// row 2 column labels, 20 data rows.</item>
/// <item><c>Fixtures/flat-header.xlsx</c> — control: identical data under one header row.</item>
/// </list>
/// The control is what makes the finding attributable: the same data extracts correctly when the
/// header is flat, so the defect is in header handling and not in the data path.
/// </para>
/// </summary>
public class MergedHeaderCharacterizationTests
{
    private static readonly string MergedHeaderFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "merged-2row-header.xlsx");

    private static readonly string FlatHeaderFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "flat-header.xlsx");

    private readonly ExcelDocumentReader _reader = new();

    /// <summary>
    /// The control. Same 17 columns and 20 rows under a single header row extract intact — so a
    /// failure in the merged-header case below cannot be blamed on the data, the column count, or
    /// the Korean text.
    /// </summary>
    [Fact]
    public async Task FlatHeader_ExtractsTheHeaderAndTheDataIntact()
    {
        var content = await _reader.ExtractAsync(FlatHeaderFixture);

        Assert.Contains("연번", content.Text);
        Assert.Contains("이메일", content.Text);      // last column survives
        Assert.Contains("한국기관00", content.Text);  // first data row
        Assert.Contains("한국기관19", content.Text);  // last data row — guards a truncated extract
    }

    /// <summary>
    /// CHARACTERIZATION — the merged group labels do not keep their columns.
    /// Expected once fixed: each group label sits above the span it labels.
    /// </summary>
    [Fact]
    public async Task MergedHeader_GroupLabels_DoNotKeepTheirColumns()
    {
        var content = await _reader.ExtractAsync(MergedHeaderFixture);

        var groupLabelLine = content.Text
            .Split('\n')
            .FirstOrDefault(line => line.Contains("기본 정보"));

        Assert.NotNull(groupLabelLine);

        // In the input the four labels start at columns 1, 5, 10 and 14 — a span of 13 cells.
        // Observed output puts all four adjacent at the far right of the row.
        var cells = groupLabelLine!.Split('|');
        var firstLabelCell = Array.FindIndex(cells, c => c.Contains("기본 정보"));
        var lastLabelCell = Array.FindLastIndex(cells, c => c.Contains("담당자"));

        // Both must be present, or the assertion below would pass on an output that dropped them:
        // a span of zero between two absent labels is not evidence of anything.
        Assert.True(firstLabelCell >= 0 && lastLabelCell >= 0, "both group labels appear in the row");

        Assert.True(
            lastLabelCell - firstLabelCell < 13,
            $"the four group labels span 13 cells in the input; they occupy {lastLabelCell - firstLabelCell} " +
            "in the output. If this now fails because the span is correct, the upstream defect " +
            "is fixed — invert this test into a positional contract and drop the consumer caveats.");
    }

    /// <summary>
    /// CHARACTERIZATION — a <c>#</c> appears in the first column of the group-label row, and no
    /// cell of the input contains one. Expected once fixed: nothing is present that was not in the
    /// source. A fabricated cell is worse than a lost one: it reads as data.
    /// </summary>
    [Fact]
    public async Task MergedHeader_InjectsACellTheInputNeverHad()
    {
        var content = await _reader.ExtractAsync(MergedHeaderFixture);

        var groupLabelLine = content.Text
            .Split('\n')
            .FirstOrDefault(line => line.Contains("기본 정보"));

        Assert.NotNull(groupLabelLine);
        Assert.Contains("| # |", groupLabelLine!);
    }

    /// <summary>
    /// CHARACTERIZATION — the column-label row is not emitted as the table's header row.
    /// Expected once fixed: row 2's labels form the markdown header, above the separator.
    /// </summary>
    [Fact]
    public async Task MergedHeader_ColumnLabelRow_IsNotTheTableHeader()
    {
        var content = await _reader.ExtractAsync(MergedHeaderFixture);

        var lines = content.Text.Split('\n');
        var separatorIndex = Array.FindIndex(lines, l => l.TrimStart().StartsWith("| ---", StringComparison.Ordinal));
        var labelRowIndex = Array.FindIndex(lines, l => l.Contains("연번") && l.Contains("이메일"));

        Assert.True(separatorIndex >= 0, "a markdown table is produced at all");

        // The label row must be found. "Absent" would also satisfy "not the header", and pinning a
        // defect on an absence proves nothing about where the labels went.
        Assert.True(labelRowIndex >= 0, "the column labels survive somewhere in the output");

        Assert.True(
            labelRowIndex > separatorIndex,
            "the column labels are demoted below the separator, i.e. rendered as data rather than as " +
            "the header. If this now fails, the header row is being emitted correctly — invert the test.");
    }

    /// <summary>
    /// The one thing that must hold regardless: no data may be lost. A structural defect in the
    /// header is recoverable by a reader; missing rows are not, and would be a far worse failure
    /// than a mislaid label.
    /// </summary>
    [Fact]
    public async Task MergedHeader_LosesNoDataRows()
    {
        var content = await _reader.ExtractAsync(MergedHeaderFixture);

        Assert.Contains("한국기관00", content.Text);
        Assert.Contains("한국기관10", content.Text);
        Assert.Contains("한국기관19", content.Text);
    }
}
