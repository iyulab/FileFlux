using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using FileFlux.Infrastructure;
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
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        // OWPML requires the mimetype entry first and stored uncompressed.
        Add(zip, "mimetype", "application/hwp+zip", CompressionLevel.NoCompression);
        Add(zip, "version.xml",
            """<?xml version="1.0" encoding="UTF-8"?><hv:HCFVersion xmlns:hv="http://www.hancom.co.kr/hwpml/2011/version" targetApplication="WORDPROCESSOR" major="5" minor="1" micro="0" buildNumber="0"/>""");
        Add(zip, "META-INF/container.xml",
            """<?xml version="1.0" encoding="UTF-8"?><ocf:container xmlns:ocf="urn:oasis:names:tc:opendocument:xmlns:container"><ocf:rootfiles><ocf:rootfile full-path="Contents/content.hpf" media-type="application/hwpml-package+xml"/></ocf:rootfiles></ocf:container>""");
        Add(zip, "Contents/content.hpf",
            """<?xml version="1.0" encoding="UTF-8"?><opf:package xmlns:opf="http://www.idpf.org/2007/opf/" version="" unique-identifier="" id=""><opf:metadata/><opf:manifest><opf:item id="header" href="Contents/header.xml" media-type="application/xml"/><opf:item id="section0" href="Contents/section0.xml" media-type="application/xml"/></opf:manifest><opf:spine><opf:itemref idref="header" linear="yes"/><opf:itemref idref="section0" linear="yes"/></opf:spine></opf:package>""");
        Add(zip, "Contents/header.xml",
            """<?xml version="1.0" encoding="UTF-8"?><hh:head xmlns:hh="http://www.hancom.co.kr/hwpml/2011/head" version="1.4" secCnt="1"/>""");

        var section = new StringBuilder(
            """<?xml version="1.0" encoding="UTF-8"?><hs:sec xmlns:hs="http://www.hancom.co.kr/hwpml/2011/section" xmlns:hp="http://www.hancom.co.kr/hwpml/2011/paragraph">""");
        var id = 0;
        foreach (var paragraph in Paragraphs)
            section.Append(System.Globalization.CultureInfo.InvariantCulture, $"""<hp:p id="{id++}" paraPrIDRef="0" styleIDRef="0"><hp:run charPrIDRef="0"><hp:t>{paragraph}</hp:t></hp:run></hp:p>""");
        section.Append("</hs:sec>");
        Add(zip, "Contents/section0.xml", section.ToString());
        return path;

        static void Add(ZipArchive archive, string name, string content, CompressionLevel level = CompressionLevel.Optimal)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name, level).Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
    }
}
