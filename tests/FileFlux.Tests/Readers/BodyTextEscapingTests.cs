using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FileFlux.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Body text whose characters Markdown reads as syntax stays text in the refined Markdown: an expression with asterisks
/// is not emphasis, backticks are not a code span, brackets are not a link, a backslash is not an escape. And a paragraph
/// that happens to contain pipes is not merged into the table that follows it.
/// </summary>
public sealed class BodyTextEscapingTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "FileFluxEscaping-" + Guid.NewGuid().ToString("N") + ".docx");

    public void Dispose()
    {
        try { File.Delete(_path); } catch (IOException) { }
    }

    [Fact]
    public async Task RefinedText_EscapesMarkdownSyntaxInBodyText_AndKeepsTheTableApart()
    {
        WriteDocx(
            [
                "snake_case_name 과 a*b*c 그리고 [대괄호](괄호) 표기",
                @"경로 C:\dir\file.txt",
                "가격 <10% 이상> 그리고 `백틱` 과 |파이프|",
            ],
            [("항목_이름", "값"), ("a_b", "1")]);
        var services = new ServiceCollection();
        services.AddFileFlux();
        await using var provider = services.BuildServiceProvider();
        await using var processor = provider.GetRequiredService<IDocumentProcessorFactory>().Create(_path);

        await processor.RefineAsync(cancellationToken: TestContext.Current.CancellationToken);
        var text = processor.Result.Refined!.Text.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains(@"a\*b\*c", text, StringComparison.Ordinal);
        Assert.Contains(@"\[대괄호\](괄호)", text, StringComparison.Ordinal);
        Assert.Contains(@"C:\\dir\\file.txt", text, StringComparison.Ordinal);
        Assert.Contains(@"\`백틱\`", text, StringComparison.Ordinal);

        var lines = text.Split('\n');
        var paragraph = Array.FindIndex(lines, l => l.Contains("|파이프|", StringComparison.Ordinal));
        Assert.Equal(string.Empty, lines[paragraph + 1].Trim());
        var tableStart = Array.FindIndex(lines, l => l.StartsWith("| 항목_이름", StringComparison.Ordinal));
        Assert.True(tableStart > paragraph + 1, "the table must follow the paragraph, not absorb it:\n" + text);
        Assert.Equal("| --- | --- |", lines[tableStart + 1]);
    }

    private void WriteDocx(string[] paragraphs, (string, string)[] rows)
    {
        using var doc = WordprocessingDocument.Create(_path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var body = new Body();
        foreach (var paragraph in paragraphs)
            body.Append(new Paragraph(new Run(new Text(paragraph) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve })));
        var table = new Table();
        foreach (var (a, b) in rows)
            table.Append(new TableRow(new TableCell(new Paragraph(new Run(new Text(a)))), new TableCell(new Paragraph(new Run(new Text(b))))));
        body.Append(table);
        main.Document = new Document(body);
        main.Document.Save();
    }
}
