using System.IO.Compression;
using System.Security;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// A picture's alt text reaches the body and the image caption only when an author wrote it: the file path Office
/// recorded when the picture was inserted, and a description Office generated (marked with its disclaimer), are not
/// document content.
/// </summary>
public class ImageAltTextTests
{
    private const string KoreanDisclaimer = "AI가 생성한 콘텐츠는 올바르지 않을 수 있습니다.";

    [Theory]
    [InlineData(@"C:\Users\Hong\Desktop\AcmeCorp\photo.png")]
    [InlineData(@"d:/work/client/diagram.emf")]
    [InlineData(@"\\fileserver\share\images\logo.png")]
    [InlineData("file:///C:/Users/Hong/Pictures/chart.png")]
    [InlineData("/Users/hong/Desktop/screenshot.png")]
    [InlineData("/home/hong/pictures/plot.svg")]
    [InlineData("back.png")]
    [InlineData("IMG_1234.JPG")]
    public void Classify_FilePathOrFileName_IsDropped(string alt) =>
        Assert.Equal((AltTextKind.FilePath, (string?)null), ImageAltText.Classify(alt));

    [Theory]
    [InlineData("A person standing in front of a building\n\n" + KoreanDisclaimer, "A person standing in front of a building")]
    [InlineData("A chart with numbers\n\nAI-generated content may be incorrect.", "A chart with numbers")]
    [InlineData("A picture containing text, outdoor  Description automatically generated", "A picture containing text, outdoor")]
    [InlineData("텍스트, 스크린샷이(가) 표시된 사진  자동 생성된 설명", "텍스트, 스크린샷이(가) 표시된 사진")]
    public void Classify_GeneratedDescription_KeepsTheDescriptionWithoutItsMarker(string alt, string expected) =>
        Assert.Equal((AltTextKind.Generated, (string?)expected), ImageAltText.Classify(alt));

    [Theory]
    [InlineData("Quarterly revenue chart", "Quarterly revenue chart")]
    [InlineData("  Process flow:\n  intake → review  ", "Process flow: intake → review")]
    [InlineData("Logo of the C: drive campaign", "Logo of the C: drive campaign")]
    public void Classify_AuthoredText_IsKeptOnOneLine(string alt, string expected) =>
        Assert.Equal((AltTextKind.Authored, (string?)expected), ImageAltText.Classify(alt));

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Classify_Empty_IsNone(string? alt) =>
        Assert.Equal((AltTextKind.None, (string?)null), ImageAltText.Classify(alt));

    [Fact]
    public void CleanMarkdown_RewritesEveryImageAlt()
    {
        var markdown = "Intro\n\n![C:\\Users\\Hong\\a.png](image1.jpeg)\n\n![A dog\n\n" + KoreanDisclaimer + "](image2.png)\n\n![Revenue  by\nregion](image3.png) tail";

        Assert.Equal("Intro\n\n![](image1.jpeg)\n\n![](image2.png)\n\n![Revenue by region](image3.png) tail", ImageAltText.CleanMarkdown(markdown));
    }

    [Fact]
    public void Attach_PutsAGeneratedDescriptionInItsOwnProperty()
    {
        var image = new FileFlux.Core.ImageInfo();
        ImageAltText.Attach(image, "A dog on a beach\n\n" + KoreanDisclaimer);

        Assert.Null(image.Caption);
        Assert.Equal("A dog on a beach", image.Properties[ImageAltText.GeneratedProperty]);
    }

    /// <summary>
    /// End to end through the PowerPoint reader (Undoc renders the picture's <c>descr</c> as the Markdown image alt).
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\Hong\Desktop\AcmeCorp\photo.png", null, null, "AcmeCorp")]
    [InlineData("A person standing in front of a building\n\n" + KoreanDisclaimer, null, "A person standing in front of a building", "AI가 생성한")]
    [InlineData("Quarterly revenue chart", "Quarterly revenue chart", null, null)]
    public async Task PowerPoint_BodyAndCaptionCarryOnlyAuthoredAltText(string descr, string? caption, string? generated, string? notInBody)
    {
        var path = await SamplePresentationWithPictureAsync(descr);
        try
        {
            var content = await new PowerPointDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

            var image = Assert.Single(content.Images);
            Assert.Equal(caption, image.Caption);
            Assert.Equal(generated, image.Properties.TryGetValue(ImageAltText.GeneratedProperty, out var g) ? g : null);
            Assert.Contains($"![{caption}](", content.Text, StringComparison.Ordinal);
            if (notInBody is not null)
                Assert.DoesNotContain(notInBody, content.Text, StringComparison.Ordinal);
            Assert.Contains("St. Cloud Technical College", content.Text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The Word reader takes the same path: the sample document's pictures carry their file names as alt text
    /// (<c>descr="dot_green.png"</c>), and one is rewritten to an author's local path.
    /// </summary>
    [Fact]
    public async Task Word_FileNamesAndPathsDoNotReachTheBody()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "test-docx", "demo.docx");
        var path = Path.Combine(Path.GetTempPath(), $"alt-{Guid.NewGuid():N}.docx");
        File.Copy(source, path);
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                await RewriteAsync(zip, "word/document.xml", xml => xml.Replace("descr=\"back.png\"",
                    "descr=\"C:\\Users\\Hong\\Documents\\AcmeCorp\\back.png\"", StringComparison.Ordinal));
            }

            var content = await new WordDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEmpty(content.Images);
            Assert.DoesNotContain("AcmeCorp", content.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("dot_green.png", content.Text, StringComparison.Ordinal);
            Assert.All(content.Images, image => Assert.Null(image.Caption));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<string> SamplePresentationWithPictureAsync(string descr)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "test-pptx", "samplepptx.pptx");
        var path = Path.Combine(Path.GetTempPath(), $"alt-{Guid.NewGuid():N}.pptx");
        File.Copy(source, path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        await RewriteAsync(zip, "ppt/slides/_rels/slide1.xml.rels", xml => xml.Replace("</Relationships>",
            "<Relationship Id=\"rIdPic99\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"../media/image1.jpeg\"/>"
            + "</Relationships>", StringComparison.Ordinal));
        await RewriteAsync(zip, "ppt/slides/slide1.xml", xml => xml.Replace("</p:spTree>",
            "<p:pic><p:nvPicPr><p:cNvPr id=\"99\" name=\"Picture 99\" descr=\"" + SecurityElement.Escape(descr)!.Replace("\n", "&#xA;", StringComparison.Ordinal) + "\"/><p:cNvPicPr/><p:nvPr/></p:nvPicPr>"
            + "<p:blipFill><a:blip r:embed=\"rIdPic99\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>"
            + "<p:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"914400\" cy=\"914400\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>"
            + "</p:spTree>", StringComparison.Ordinal));
        return path;
    }

    private static async Task RewriteAsync(ZipArchive zip, string entryName, Func<string, string> edit)
    {
        var entry = zip.GetEntry(entryName)!;
        string xml;
        using (var reader = new StreamReader(entry.Open()))
            xml = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        var edited = edit(xml);
        Assert.NotEqual(xml, edited);
        entry.Delete();
        using var writer = new StreamWriter(zip.CreateEntry(entryName).Open());
        await writer.WriteAsync(edited);
    }
}
