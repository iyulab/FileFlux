using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// The readers' <c>has_tables</c> hint comes from the delimiter row of a Markdown table. It used to look for the text
/// <c>|---</c> or <c>| ---</c>, so a table whose first column is aligned (<c>| :---: |</c>, which the Word parser writes for
/// a centred column) was not seen.
/// </summary>
public sealed class MarkdownTablesTests
{
    [Theory]
    [InlineData("| a | b |\n| --- | --- |\n| 1 | 2 |")]
    [InlineData("|a|b|\n|---|---|\n|1|2|")]
    [InlineData("| a | b |\n| :---: | :---: |\n| 1 | 2 |")]
    [InlineData("| a | b |\n| :--- | ---: |\n| 1 | 2 |")]
    [InlineData("| a | b |\r\n| :---: | --- |\r\n| 1 | 2 |")]
    [InlineData("| a |\n| --- |\n| 1 |")]
    [InlineData("a | b\n--- | ---\n1 | 2")]
    [InlineData("Text before.\n\n| a | b |\n|:-:|:-:|\n| 1 | 2 |")]
    public void Contains_FindsADelimiterRow(string markdown)
    {
        Assert.True(MarkdownTables.Contains(markdown));
    }

    [Theory]
    [InlineData("A paragraph.\n\n---\n\nAnother paragraph.")]
    [InlineData("Section 1 ------ 10")]
    [InlineData("A paragraph with a | pipe and --- dashes.")]
    [InlineData("")]
    public void Contains_IgnoresWhatIsNotATable(string markdown)
    {
        Assert.False(MarkdownTables.Contains(markdown));
    }
    [Fact]
    public async Task WordReader_ReportsATableWhoseColumnsAreCentred()
    {
        var path = Path.Combine(Path.GetTempPath(), "FileFluxCentredTable-" + Guid.NewGuid().ToString("N") + ".docx");
        try
        {
            using (var doc = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                var table = new Table();
                foreach (var row in new[] { ("Name", "Score"), ("Alpha", "1"), ("Beta", "2") })
                    table.Append(new TableRow(Cell(row.Item1), Cell(row.Item2)));
                main.Document = new Document(new Body(new Paragraph(new Run(new Text("Scores."))), table));
                main.Document.Save();
            }

            var raw = await new WordDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains(":---:", raw.Text, StringComparison.Ordinal);
            Assert.True(raw.Hints.TryGetValue("has_tables", out var hasTables) && hasTables is true,
                "has_tables was not set for:\n" + raw.Text);
        }
        finally
        {
            File.Delete(path);
        }

        static TableCell Cell(string text) => new(new Paragraph(
            new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
            new Run(new Text(text))));
    }
}
