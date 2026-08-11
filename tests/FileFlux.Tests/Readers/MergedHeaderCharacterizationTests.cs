using FileFlux.Core.Infrastructure.Readers;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Regression contract for multi-row (merged-cell) spreadsheet headers.
///
/// <para>
/// Modern <c>.xlsx</c> serialization is delegated in full to Undoc. Before Undoc 0.8.0, a
/// workbook whose header spans two rows — one merged row of group labels above a row of column
/// labels, a shape common in real reporting spreadsheets — came back with its structure
/// collapsed: group labels shifted to the far right, a fabricated <c>#</c> cell injected, no
/// error or warning. Undoc 0.8.0 fixed this by anchoring merged cells to their start column
/// (<c>render/grid.rs</c>) — see
/// <c>claudedocs/FileFlux/upstream-issues/closed/ISSUE-undoc-20260805-merged-multirow-header-table-collapse.md</c>
/// in the umbrella workspace. These tests used to pin the defect (see git history for the
/// characterization-test form); they now pin the fixed contract so a future Undoc bump cannot
/// regress it silently.
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
/// header is flat, so the defect was in header handling and not in the data path.
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
    /// Each merged group label sits at its own start column — not shifted to the far right of the
    /// row. The input places the four group-label merges starting at cell indices 1, 5, 10 and 14
    /// (index 0 is the leading empty split before the first pipe).
    /// </summary>
    [Fact]
    public async Task MergedHeader_GroupLabels_KeepTheirColumns()
    {
        var content = await _reader.ExtractAsync(MergedHeaderFixture);

        var groupLabelLine = content.Text
            .Split('\n')
            .FirstOrDefault(line => line.Contains("기본 정보"));

        Assert.NotNull(groupLabelLine);

        var cells = groupLabelLine!.Split('|');

        Assert.Equal(1, Array.FindIndex(cells, c => c.Contains("기본 정보")));
        Assert.Equal(5, Array.FindIndex(cells, c => c.Contains("장비 현황")));
        Assert.Equal(10, Array.FindIndex(cells, c => c.Contains("유지보수 계약")));
        Assert.Equal(14, Array.FindIndex(cells, c => c.Contains("담당자")));
    }

    /// <summary>
    /// No cell appears in the output that was not in the source workbook. Undoc 0.5.2 fabricated a
    /// <c>#</c> in the first column of the group-label row to pad a short header row; a fabricated
    /// cell is worse than a lost one because it reads as data.
    /// </summary>
    [Fact]
    public async Task MergedHeader_DoesNotInjectACellTheInputNeverHad()
    {
        var content = await _reader.ExtractAsync(MergedHeaderFixture);

        var groupLabelLine = content.Text
            .Split('\n')
            .FirstOrDefault(line => line.Contains("기본 정보"));

        Assert.NotNull(groupLabelLine);
        Assert.DoesNotContain("| # |", groupLabelLine!);
    }

    /// <summary>
    /// Confirmed intentional (not a defect): row 2's column labels (연번/이메일/...) render as the
    /// first data row, not as the markdown table header. Markdown cannot express a two-row header
    /// natively, so Undoc promotes row 1 (the group labels, now correctly column-anchored) to the
    /// header and flattens row 2 into data — see the undoc maintainer's response in
    /// <c>ISSUE-undoc-20260805-merged-multirow-header-table-collapse.md</c> §"다단 헤더 → 헤더1행 +
    /// 데이터N행 평탄화는 결함으로 접수하지 않는다". Content is not lost (asserted by
    /// <see cref="MergedHeader_LosesNoDataRows"/>), only relocated.
    /// </summary>
    [Fact]
    public async Task MergedHeader_ColumnLabelRow_IsFlattenedIntoData_ByDesign()
    {
        var content = await _reader.ExtractAsync(MergedHeaderFixture);

        var lines = content.Text.Split('\n');
        var separatorIndex = Array.FindIndex(lines, l => l.TrimStart().StartsWith("| ---", StringComparison.Ordinal));
        var labelRowIndex = Array.FindIndex(lines, l => l.Contains("연번") && l.Contains("이메일"));

        Assert.True(separatorIndex >= 0, "a markdown table is produced at all");
        Assert.True(labelRowIndex >= 0, "the column labels survive somewhere in the output");
        Assert.True(labelRowIndex > separatorIndex, "the column labels render as the first data row");
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
