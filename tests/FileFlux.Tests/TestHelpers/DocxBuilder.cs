using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace FileFlux.Tests.TestHelpers;

/// <summary>
/// Writes a minimal DOCX: plain body paragraphs (no styles, spaces preserved), then an optional table of plain cells.
/// </summary>
internal static class DocxBuilder
{
    public static void Write(string path, IEnumerable<string> paragraphs, IEnumerable<string[]>? tableRows = null)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var body = new Body();
        foreach (var paragraph in paragraphs)
            body.Append(new Paragraph(new Run(new Text(paragraph) { Space = SpaceProcessingModeValues.Preserve })));

        if (tableRows is not null)
        {
            var table = new Table();
            foreach (var row in tableRows)
                table.Append(new TableRow(row.Select(cell => new TableCell(new Paragraph(new Run(new Text(cell)))))));
            body.Append(table);
        }

        main.Document = new Document(body);
        main.Document.Save();
    }
}
