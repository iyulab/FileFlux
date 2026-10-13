using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using FileFlux.Infrastructure;
using FileFlux.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// A paragraph in the source is a paragraph in the Markdown: consecutive body paragraphs are separated by a blank line,
/// in the reader's text and after refinement. Markdown joins adjacent lines into one paragraph, so without the blank line
/// a renderer shows one run-on paragraph and a paragraph-based chunker sees one block.
/// </summary>
public sealed class ParagraphSeparationTests : IDisposable
{
    private static readonly string[] Paragraphs =
    [
        "First body paragraph of the sample report.",
        "Second body paragraph of the sample report.",
        "Third body paragraph of the sample report.",
        "네 번째 본문 문단입니다.",
    ];

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "FileFluxParagraphs", Guid.NewGuid().ToString("N"));

    public ParagraphSeparationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Docx_ReaderText_SeparatesParagraphsWithABlankLine()
    {
        var path = WriteDocx();

        var raw = await new WordDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        AssertSeparated(raw.Text);
    }

    [Fact]
    public async Task Docx_ReaderTextFromBytes_SeparatesParagraphsWithABlankLine()
    {
        var path = WriteDocx();
        await using var stream = File.OpenRead(path);

        var raw = await new WordDocumentReader().ExtractAsync(stream, Path.GetFileName(path), cancellationToken: TestContext.Current.CancellationToken);

        AssertSeparated(raw.Text);
    }

    [Fact]
    public async Task Hwpx_ReaderText_SeparatesParagraphsWithABlankLine()
    {
        var path = WriteHwpx();

        var raw = await new HwpDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        AssertSeparated(raw.Text);
    }

    [Fact]
    public async Task Hwpx_ReaderTextFromBytes_SeparatesParagraphsWithABlankLine()
    {
        var path = WriteHwpx();
        await using var stream = File.OpenRead(path);

        var raw = await new HwpDocumentReader().ExtractAsync(stream, Path.GetFileName(path), cancellationToken: TestContext.Current.CancellationToken);

        AssertSeparated(raw.Text);
    }

    [Theory]
    [InlineData("docx")]
    [InlineData("hwpx")]
    public async Task RefinedText_KeepsEveryParagraphSeparate(string format)
    {
        var path = format == "docx" ? WriteDocx() : WriteHwpx();
        var services = new ServiceCollection();
        services.AddFileFlux();
        await using var provider = services.BuildServiceProvider();
        await using var processor = provider.GetRequiredService<IDocumentProcessorFactory>().Create(path);

        await processor.RefineAsync(cancellationToken: TestContext.Current.CancellationToken);

        AssertSeparated(processor.Result.Refined!.Text);
    }

    private static void AssertSeparated(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var blocks = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var paragraph in Paragraphs)
        {
            Assert.Contains(paragraph, normalized, StringComparison.Ordinal);
            Assert.True(blocks.Contains(paragraph, StringComparer.Ordinal),
                $"«{paragraph}» is not a block of its own; the text was:\n{normalized}");
        }
    }

    private string WriteDocx()
    {
        var path = Path.Combine(_dir, "paragraphs.docx");
        using (var doc = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            foreach (var paragraph in Paragraphs)
                body.Append(new Paragraph(new Run(new Text(paragraph))));
            main.Document = new Document(body);
            main.Document.Save();
        }
        return path;
    }

    private string WriteHwpx()
    {
        var path = Path.Combine(_dir, "paragraphs.hwpx");
        HwpxBuilder.Write(path, Paragraphs);
        return path;
    }
}
