using System.IO.Compression;
using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using FileFlux.Infrastructure.Readers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// A presentation image names every slide that shows it: a picture reused on slides 1 and 2 lists both
/// (<see cref="ImageInfo.PageNumbers"/>), and <see cref="ImageInfo.PageNumber"/> stays the first.
/// </summary>
public class PresentationImageSlidesTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void PageNumbers_IsThePageAlone_WhenTheReaderKnowsOnePage()
    {
        Assert.Equal([4], new ImageInfo { PageNumber = 4 }.PageNumbers);
        Assert.Empty(new ImageInfo().PageNumbers);

        var image = new ImageInfo { PageNumbers = [2, 5] };
        Assert.Equal(2, image.PageNumber);
        Assert.Equal([2, 5], image.PageNumbers);
    }

    [Fact]
    public async Task APictureReusedOnTwoSlides_ListsBoth()
    {
        var path = await ReusedPictureDeckAsync();
        try
        {
            var content = await new PowerPointDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

            var reused = Assert.Single(content.Images, i => i.Id == "image1.jpeg");
            Assert.Equal([1, 2], reused.PageNumbers);
            Assert.Equal(1, reused.PageNumber);

            // Undoc 0.16 does not reference a picture bullet or a picture fill from the content, so their slide is
            // unknown; the images themselves are still returned.
            Assert.Empty(Assert.Single(content.Images, i => i.Id == "fill1.png").PageNumbers);
            Assert.Empty(Assert.Single(content.Images, i => i.Id == "bullet1.png").PageNumbers);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TheMultiModalReader_LabelsADescriptionWithEverySlide()
    {
        var path = await ReusedPictureDeckAsync();
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IImageToTextService>(new StubImageToTextService());
            var content = await new MultiModalPowerPointDocumentReader(services.BuildServiceProvider())
                .ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("Presentation Image 1 (slides 1, 2):", content.Text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<string> ReusedPictureDeckAsync()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "test-pptx", "samplepptx.pptx");
        var path = Path.Combine(Path.GetTempPath(), $"slides-{Guid.NewGuid():N}.pptx");
        File.Copy(source, path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            await RewriteAsync(zip, "[Content_Types].xml", xml => xml.Contains("Extension=\"png\"") ? xml : xml.Replace("<Default Extension=\"xml\"",
                "<Default Extension=\"png\" ContentType=\"image/png\"/><Default Extension=\"xml\"", StringComparison.Ordinal));
            const string rels = "<Relationship Id=\"rIdPic99\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"../media/image1.jpeg\"/>"
                + "<Relationship Id=\"rIdFill\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"../media/fill1.png\"/>"
                + "<Relationship Id=\"rIdBullet\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"../media/bullet1.png\"/>";
            await RewriteAsync(zip, "ppt/slides/_rels/slide1.xml.rels", xml => xml.Replace("</Relationships>", rels + "</Relationships>", StringComparison.Ordinal));
            await RewriteAsync(zip, "ppt/slides/_rels/slide2.xml.rels", xml => xml.Replace("</Relationships>", rels + "</Relationships>", StringComparison.Ordinal));
            const string pic = "<p:pic><p:nvPicPr><p:cNvPr id=\"99\" name=\"Picture 99\"/><p:cNvPicPr/><p:nvPr/></p:nvPicPr>"
                + "<p:blipFill><a:blip r:embed=\"rIdPic99\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>"
                + "<p:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"914400\" cy=\"914400\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>";
            const string fill = "<p:sp><p:nvSpPr><p:cNvPr id=\"98\" name=\"Filled 98\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>"
                + "<p:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"914400\" cy=\"914400\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>"
                + "<a:blipFill><a:blip r:embed=\"rIdFill\"/><a:stretch><a:fillRect/></a:stretch></a:blipFill></p:spPr>"
                + "<p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang=\"en-US\"/><a:t>Filled shape text</a:t></a:r></a:p></p:txBody></p:sp>";
            const string bullet = "<p:sp><p:nvSpPr><p:cNvPr id=\"97\" name=\"Bullets 97\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>"
                + "<p:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"914400\" cy=\"914400\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr>"
                + "<p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:pPr marL=\"285750\" indent=\"-285750\"><a:buBlip><a:blip r:embed=\"rIdBullet\"/></a:buBlip></a:pPr><a:r><a:rPr lang=\"en-US\"/><a:t>Picture bullet item</a:t></a:r></a:p></p:txBody></p:sp>";
            await RewriteAsync(zip, "ppt/slides/slide1.xml", xml => xml.Replace("</p:spTree>", pic + fill + bullet + "</p:spTree>", StringComparison.Ordinal));
            await RewriteAsync(zip, "ppt/slides/slide2.xml", xml => xml.Replace("</p:spTree>", pic + "</p:spTree>", StringComparison.Ordinal));
            foreach (var name in new[] { "ppt/media/fill1.png", "ppt/media/bullet1.png" })
            {
                using var s = zip.CreateEntry(name).Open();
                await s.WriteAsync(Png, TestContext.Current.CancellationToken);
            }
        }

        return path;
    }

    private sealed class StubImageToTextService : IImageToTextService
    {
        public IEnumerable<string> SupportedImageFormats => ["jpeg", "png"];

        public string ProviderName => "Stub";

        public Task<ImageToTextResult> ExtractTextAsync(
            byte[] imageData, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImageToTextResult { ExtractedText = "stub caption", ImageType = "diagram" });

        public Task<ImageToTextResult> ExtractTextAsync(
            Stream imageStream, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImageToTextResult { ExtractedText = "stub caption", ImageType = "diagram" });

        public Task<ImageToTextResult> ExtractTextAsync(
            string imagePath, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImageToTextResult { ExtractedText = "stub caption", ImageType = "diagram" });
    }

    private static async Task RewriteAsync(ZipArchive zip, string entryName, Func<string, string> edit)
    {
        var entry = zip.GetEntry(entryName)!;
        string xml;
        using (var reader = new StreamReader(entry.Open()))
            xml = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        var edited = edit(xml);
        entry.Delete();
        using var writer = new StreamWriter(zip.CreateEntry(entryName).Open());
        await writer.WriteAsync(edited);
    }
}
