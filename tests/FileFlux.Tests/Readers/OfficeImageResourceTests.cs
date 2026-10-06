using System.IO.Compression;
using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Images a document shows, named by their real type: an HD Photo effects layer (<c>.wdp</c>) that Undoc lists beside a
/// picture is not an image of the document, and TIFF / JPEG XR bytes are named as such instead of
/// <c>application/octet-stream</c>.
/// </summary>
public class OfficeImageResourceTests
{
    private static readonly byte[] JpegXr = [0x49, 0x49, 0xBC, 0x01, 0x08, 0x00, 0x00, 0x00];

    [Theory]
    [InlineData(new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x08, 0x00 }, "image/tiff")]
    [InlineData(new byte[] { 0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x08 }, "image/tiff")]
    [InlineData(new byte[] { 0x49, 0x49, 0xBC, 0x01, 0x08, 0x00 }, "image/vnd.ms-photo")]
    public void Detect_NamesTiffAndJpegXrByTheirBytes(byte[] data, string expected) =>
        Assert.Equal(expected, ImageMimeTypeDetector.Detect(data, "rId7"));

    [Theory]
    [InlineData("scan.TIF", "image/tiff")]
    [InlineData("hdphoto1.wdp", "image/vnd.ms-photo")]
    [InlineData("photo.jxr", "image/vnd.ms-photo")]
    public void Detect_FallsBackToTheExtension(string id, string expected) =>
        Assert.Equal(expected, ImageMimeTypeDetector.Detect(null, id));

    /// <summary>
    /// The sample presentation with a picture placed on slide 1 that carries an HD Photo artistic-effects layer (as
    /// PowerPoint stores it: the picture's blip names the <c>.wdp</c> through <c>a14:imgLayer</c>). The reader returns the
    /// picture — with its alt text as the caption — and not the layer.
    /// </summary>
    [Fact]
    public async Task PowerPoint_HdPhotoLayer_IsNotReturnedAsAnImage()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "test-pptx", "samplepptx.pptx");
        Assert.True(File.Exists(source), source);
        var path = Path.Combine(Path.GetTempPath(), $"hdphoto-{Guid.NewGuid():N}.pptx");
        File.Copy(source, path);
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                await RewriteAsync(zip, "[Content_Types].xml", xml => xml.Replace("<Default Extension=\"xml\"",
                    "<Default Extension=\"wdp\" ContentType=\"image/vnd.ms-photo\"/><Default Extension=\"xml\"", StringComparison.Ordinal));
                await RewriteAsync(zip, "ppt/slides/_rels/slide1.xml.rels", xml => xml.Replace("</Relationships>",
                    "<Relationship Id=\"rIdPic99\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"../media/image1.jpeg\"/>"
                    + "<Relationship Id=\"rIdLayer99\" Type=\"http://schemas.microsoft.com/office/2007/relationships/hdphoto\" Target=\"../media/hdphoto1.wdp\"/>"
                    + "</Relationships>", StringComparison.Ordinal));
                await RewriteAsync(zip, "ppt/slides/slide1.xml", xml => xml.Replace("</p:spTree>", PictureWithLayer + "</p:spTree>", StringComparison.Ordinal));
                using var layer = zip.CreateEntry("ppt/media/hdphoto1.wdp").Open();
                await layer.WriteAsync(JpegXr, TestContext.Current.CancellationToken);
            }

            var content = await new PowerPointDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

            var picture = Assert.Single(content.Images);
            Assert.Contains("image1", picture.Id, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("image/jpeg", picture.MimeType);
            Assert.Equal("A test photo", picture.Caption);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private const string PictureWithLayer =
        "<p:pic><p:nvPicPr><p:cNvPr id=\"99\" name=\"Picture 99\" descr=\"A test photo\"/><p:cNvPicPr/><p:nvPr/></p:nvPicPr>"
        + "<p:blipFill><a:blip r:embed=\"rIdPic99\"><a:extLst><a:ext uri=\"{BEBA8EAE-BF5A-486C-A8C5-ECC9F3942E4B}\">"
        + "<a14:imgProps xmlns:a14=\"http://schemas.microsoft.com/office/drawing/2010/main\"><a14:imgLayer r:embed=\"rIdLayer99\"/></a14:imgProps>"
        + "</a:ext></a:extLst></a:blip><a:stretch><a:fillRect/></a:stretch></p:blipFill>"
        + "<p:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"914400\" cy=\"914400\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>";

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
