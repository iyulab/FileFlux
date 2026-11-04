using System.IO.Compression;
using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// A soft line break inside a paragraph (Shift+Enter in Word and PowerPoint) is how addresses, form labels, verses
/// and signature blocks are written. The words on either side of it must stay two words in the Markdown: Undoc 0.27.2
/// wrote «ZeroCharlie» (its plain-text output kept a newline). Each case is a minimal package holding one paragraph.
/// </summary>
public sealed class OfficeLineBreakTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "FileFluxLineBreaks", Guid.NewGuid().ToString("N"));

    public OfficeLineBreakTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    public static TheoryData<string> WordParagraphs() => new()
    {
        "<w:r><w:t>Zero</w:t><w:br/><w:t>Charlie</w:t></w:r>",                          // break inside one run
        "<w:r><w:t>Zero</w:t></w:r><w:r><w:br/></w:r><w:r><w:t>Charlie</w:t></w:r>",    // break in its own run
        "<w:r><w:t>Zero</w:t><w:br/></w:r><w:r><w:t>Charlie</w:t></w:r>",               // break at the end of a run
        "<w:r><w:t>Zero</w:t></w:r><w:r><w:br/><w:t>Charlie</w:t></w:r>",               // break at the start of a run
        "<w:r><w:t>Zero</w:t><w:br w:type=\"page\"/><w:t>Charlie</w:t></w:r>",          // page break
    };

    [Theory]
    [MemberData(nameof(WordParagraphs))]
    public async Task Docx_SoftLineBreak_KeepsTheWordsApart(string paragraphBody)
    {
        var path = WriteDocx(paragraphBody);

        var raw = await new WordDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("ZeroCharlie", raw.Text);
        Assert.Contains("Zero", raw.Text);
        Assert.Contains("Charlie", raw.Text);
        Assert.Matches(@"Zero\s+Charlie", raw.Text);
    }

    [Fact]
    public async Task Docx_SoftLineBreak_FromStream_MatchesTheFile()
    {
        var path = WriteDocx("<w:r><w:t>Street 1</w:t><w:br/><w:t>City</w:t></w:r>");
        var fromFile = await new WordDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        await using var stream = File.OpenRead(path);
        var fromStream = await new WordDocumentReader().ExtractAsync(stream, Path.GetFileName(path), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(fromFile.Text, fromStream.Text);
        Assert.DoesNotContain("Street 1City", fromFile.Text);
    }

    [Fact]
    public async Task Docx_TabBetweenWords_IsStillKept()
    {
        var path = WriteDocx("<w:r><w:t>Zero</w:t><w:tab/><w:t>Charlie</w:t></w:r>");

        var raw = await new WordDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Matches(@"Zero\s+Charlie", raw.Text);
    }

    [Fact]
    public async Task Pptx_SoftLineBreak_KeepsTheWordsApart()
    {
        var path = WritePptx("<a:r><a:t>Zero</a:t></a:r><a:br/><a:r><a:t>Bravo</a:t></a:r>");

        var raw = await new PowerPointDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("ZeroBravo", raw.Text);
        Assert.Matches(@"Zero\s+Bravo", raw.Text);
    }

    private string WriteDocx(string paragraphBody)
    {
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.docx");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
        Add(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        Add(zip, "word/document.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            $"<w:body><w:p>{paragraphBody}</w:p></w:body></w:document>");
        return path;
    }

    private string WritePptx(string paragraphBody)
    {
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.pptx");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/ppt/presentation.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\"/>" +
            "<Override PartName=\"/ppt/slides/slide1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/></Types>");
        Add(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"ppt/presentation.xml\"/></Relationships>");
        Add(zip, "ppt/presentation.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><p:presentation xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><p:sldIdLst><p:sldId id=\"256\" r:id=\"rId1\"/></p:sldIdLst></p:presentation>");
        Add(zip, "ppt/_rels/presentation.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide\" Target=\"slides/slide1.xml\"/></Relationships>");
        Add(zip, "ppt/slides/slide1.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" " +
            "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><p:cSld><p:spTree><p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Body\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr/>" +
            $"<p:txBody><a:bodyPr/><a:p>{paragraphBody}</a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sld>");
        return path;
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new System.Text.UTF8Encoding(false));
        writer.Write(content);
    }
}
