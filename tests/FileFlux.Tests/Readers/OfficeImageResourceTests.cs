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
    /// The sample presentation with an HD Photo part added (as PowerPoint stores an artistic-effects layer): the reader
    /// returns the presentation's pictures and not the layer.
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
                var types = zip.GetEntry("[Content_Types].xml")!;
                string xml;
                using (var reader = new StreamReader(types.Open()))
                    xml = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
                types.Delete();
                xml = xml.Replace("<Default Extension=\"xml\"",
                    "<Default Extension=\"wdp\" ContentType=\"image/vnd.ms-photo\"/><Default Extension=\"xml\"", StringComparison.Ordinal);
                using (var writer = new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open()))
                    await writer.WriteAsync(xml);
                using var layer = zip.CreateEntry("ppt/media/hdphoto1.wdp").Open();
                await layer.WriteAsync(JpegXr, TestContext.Current.CancellationToken);
            }

            var content = await new PowerPointDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEmpty(content.Images);
            Assert.DoesNotContain(content.Images, i => i.Id.Contains("hdphoto", StringComparison.OrdinalIgnoreCase));
            Assert.All(content.Images, i => Assert.StartsWith("image/", i.MimeType, StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
