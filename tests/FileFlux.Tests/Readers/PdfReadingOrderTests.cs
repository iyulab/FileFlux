using FileFlux.Core.Infrastructure.Readers;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Reading order of a two-column page whose gutter is narrow (30 pt, Courier 10 pt). Fixture
/// <c>Fixtures/two-column-narrow-gutter.pdf</c> is generated: 24 left-column lines <c>Lnn …</c> and 24 right-column lines
/// <c>Rnn …</c> on shared baselines; left line 10 ends in <c>inter-</c> and line 11 continues with <c>national</c>.
/// A reader that assembles lines across the gutter interleaves the columns and de-hyphenates into the other column.
/// </summary>
public class PdfReadingOrderTests
{
    private static readonly string TwoColumnPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "two-column-narrow-gutter.pdf");

    private readonly PdfDocumentReader _reader = new();

    [Fact]
    public async Task ExtractAsync_TwoColumnPage_ReadsTheLeftColumnBeforeTheRight()
    {
        var content = await _reader.ExtractAsync(TwoColumnPath, cancellationToken: TestContext.Current.CancellationToken);

        var lastLeft = content.Text.IndexOf("L24 ", StringComparison.Ordinal);
        var firstRight = content.Text.IndexOf("R01 ", StringComparison.Ordinal);
        Assert.True(lastLeft >= 0 && firstRight >= 0, content.Text);
        Assert.True(lastLeft < firstRight, "columns interleaved: " + content.Text);
    }

    [Fact]
    public async Task ExtractAsync_TwoColumnPage_RejoinsAHyphenatedWordWithinItsColumn()
    {
        var content = await _reader.ExtractAsync(TwoColumnPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("international boundaries", content.Text, StringComparison.Ordinal);
    }
}
