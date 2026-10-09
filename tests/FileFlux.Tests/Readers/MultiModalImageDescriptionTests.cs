using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using FileFlux.Infrastructure.Readers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// The multimodal PDF, Word and Excel readers describe the images the base reader extracted
/// (<see cref="RawContent.Images"/>): the stream path describes them as the file path does, and the image options
/// (<see cref="ExtractOptions.ExtractImages"/>) decide what reaches the <see cref="IImageToTextService"/>.
/// </summary>
public class MultiModalImageDescriptionTests
{
    private const string Description = "A described picture of a bar chart.";

    private static readonly string Pdf = Path.Combine(AppContext.BaseDirectory, "Fixtures", "oai_gpt-oss_model_card.pdf");

    private static readonly string Docx = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "test-docx", "demo.docx");

    public static TheoryData<string> Formats => new() { "pdf", "docx", "xlsx" };

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task TheStreamPath_DescribesTheImages(string format)
    {
        var service = new CountingImageToTextService();
        var reader = Reader(format, service);
        var path = await FixtureAsync(format);
        try
        {
            await using var stream = File.OpenRead(path);
            var content = await reader.ExtractAsync(stream, Path.GetFileName(path), cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(service.Calls > 0, "the service was never called");
            Assert.Contains(Description, content.Text, StringComparison.Ordinal);
            Assert.Contains("<!-- IMAGE_START:IMG_1 -->", content.Text, StringComparison.Ordinal);
            Assert.Contains(LabelOfTheFirstImage(format), content.Text, StringComparison.Ordinal);
            Assert.True((bool)content.Hints["HasImages"]);
        }
        finally
        {
            DeleteIfGenerated(format, path);
        }
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task ExtractImagesOff_DescribesNothing_OnTheFilePath(string format)
    {
        var service = new CountingImageToTextService();
        var reader = Reader(format, service);
        var path = await FixtureAsync(format);
        try
        {
            var content = await reader.ExtractAsync(path, new ExtractOptions { ExtractImages = false }, TestContext.Current.CancellationToken);

            Assert.Equal(0, service.Calls);
            Assert.DoesNotContain(Description, content.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("<!-- IMAGE_START:", content.Text, StringComparison.Ordinal);
            Assert.False(content.Hints.ContainsKey("HasImages"));
            Assert.Empty(content.Images);
        }
        finally
        {
            DeleteIfGenerated(format, path);
        }
    }

    // The same files with default options are described on the file path: the zero above is the option, not the file.
    [Theory]
    [MemberData(nameof(Formats))]
    public async Task DefaultOptions_DescribeTheImages_OnTheFilePath(string format)
    {
        var service = new CountingImageToTextService();
        var path = await FixtureAsync(format);
        try
        {
            var content = await Reader(format, service).ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(service.Calls > 0, "the service was never called");
            Assert.Contains(Description, content.Text, StringComparison.Ordinal);
        }
        finally
        {
            DeleteIfGenerated(format, path);
        }
    }

    // The Excel reader returns the workbook's pictures, each with the sheet that shows it, and its text is unchanged.
    [Fact]
    public async Task TheExcelReader_ReturnsTheWorkbooksPictures_WithTheirSheet()
    {
        var path = await WorkbookWithAPictureAsync();
        try
        {
            var reader = new ExcelDocumentReader();
            var content = await reader.ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

            var image = Assert.Single(content.Images);
            Assert.Equal("image/png", image.MimeType);
            Assert.Equal(ChartPng, image.Data);
            Assert.Equal(2, image.PageNumber);
            Assert.Contains("Quarterly revenue", content.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("image1", content.Text, StringComparison.Ordinal);

            await using var stream = File.OpenRead(path);
            var fromStream = await reader.ExtractAsync(stream, "book.xlsx", cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(content.Text, fromStream.Text);
            Assert.Equal(2, Assert.Single(fromStream.Images).PageNumber);

            var off = await reader.ExtractAsync(path, new ExtractOptions { ExtractImages = false }, TestContext.Current.CancellationToken);
            Assert.Empty(off.Images);
            Assert.Equal(content.Text, off.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // The Word reader applies the image options too: off means no images, a size cap drops the larger ones.
    [Fact]
    public async Task TheWordReader_AppliesTheImageOptions()
    {
        var reader = new WordDocumentReader();
        var ct = TestContext.Current.CancellationToken;

        var all = await reader.ExtractAsync(Docx, cancellationToken: ct);
        Assert.NotEmpty(all.Images);

        Assert.Empty((await reader.ExtractAsync(Docx, new ExtractOptions { ExtractImages = false }, ct)).Images);

        await using var stream = File.OpenRead(Docx);
        var capped = await reader.ExtractAsync(stream, "demo.docx", new ExtractOptions { MaxImageSize = 2000 }, ct);
        Assert.NotEmpty(capped.Images);
        Assert.All(capped.Images, image => Assert.True(image.Data!.Length <= 2000));
        Assert.True(capped.Images.Count < all.Images.Count);
        Assert.Contains(capped.Warnings, w => w.Contains("MaxImageSize", StringComparison.Ordinal));
    }

    private static IDocumentReader Reader(string format, IImageToTextService service)
    {
        var provider = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
        return format switch
        {
            "pdf" => new MultiModalPdfDocumentReader(provider),
            "docx" => new MultiModalWordDocumentReader(provider),
            "xlsx" => new MultiModalExcelDocumentReader(provider),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    private static string LabelOfTheFirstImage(string format) => format switch
    {
        "pdf" => "Image 1:",
        "docx" => "Document Image 1:",
        "xlsx" => "Spreadsheet Image 1:",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static async Task<string> FixtureAsync(string format) => format switch
    {
        "pdf" => Pdf,
        "docx" => Docx,
        "xlsx" => await WorkbookWithAPictureAsync(),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static void DeleteIfGenerated(string format, string path)
    {
        if (format == "xlsx")
            File.Delete(path);
    }

    // A 120x120 PNG: large enough not to be taken for a decorative icon.
    [Theory]
    [InlineData("DOCUMENT_IMAGES")]
    [InlineData(null)]
    public async Task TheFirstMarker_StartsItsOwnLine_WhenTheTextDoesNotEndOne(string? sectionMarker)
    {
        // A document whose text ends without a line break (a table's last row) got the marker glued onto that row.
        var content = new RawContent
        {
            Text = "| Quarter | Revenue |" + "\n" + "| Q4 | 120 |",
            Images = [new ImageInfo { Id = "img1", MimeType = "image/png", Data = ChartPng }],
        };
        var format = new ImageDescriptionFormat
        {
            DocumentType = "Test",
            ImageTypeHint = "chart",
            SectionMarker = sectionMarker,
            Label = (_, n) => $"Image {n}:",
            ResultSubject = (n, _) => $"Image {n}",
        };

        var described = await ImageDescriptions.AppendAsync(
            content, "test", "TestReader", format, new CountingImageToTextService(), relevanceEvaluator: null,
            TestContext.Current.CancellationToken);

        Assert.StartsWith("| Quarter | Revenue |" + "\n" + "| Q4 | 120 |" + "\n" + "<!-- ", described.Text, StringComparison.Ordinal);
    }

    private static readonly byte[] ChartPng = SolidPng(120, 120);

    /// <summary>A two-sheet workbook whose second sheet shows one picture (a drawing anchored to a cell range).</summary>
    private static async Task<string> WorkbookWithAPictureAsync()
    {
        const string sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string pkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var parts = new Dictionary<string, string>
        {
            ["[Content_Types].xml"] =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Default Extension=\"png\" ContentType=\"image/png\"/>"
                + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
                + "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
                + "<Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
                + "<Override PartName=\"/xl/drawings/drawing1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>"
                + "</Types>",
            ["_rels/.rels"] =
                $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"{pkgRelNs}\">"
                + $"<Relationship Id=\"rId1\" Type=\"{relNs}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>",
            ["xl/workbook.xml"] =
                $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"{sheetNs}\" xmlns:r=\"{relNs}\"><sheets>"
                + "<sheet name=\"Data\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Chart\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>",
            ["xl/_rels/workbook.xml.rels"] =
                $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"{pkgRelNs}\">"
                + $"<Relationship Id=\"rId1\" Type=\"{relNs}/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
                + $"<Relationship Id=\"rId2\" Type=\"{relNs}/worksheet\" Target=\"worksheets/sheet2.xml\"/></Relationships>",
            ["xl/worksheets/sheet1.xml"] =
                $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"{sheetNs}\"><sheetData>"
                + "<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Quarter</t></is></c><c r=\"B1\" t=\"inlineStr\"><is><t>Quarterly revenue</t></is></c></row>"
                + "<row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>Q1</t></is></c><c r=\"B2\"><v>120</v></c></row>"
                + "</sheetData></worksheet>",
            ["xl/worksheets/sheet2.xml"] =
                $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"{sheetNs}\" xmlns:r=\"{relNs}\"><sheetData>"
                + "<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Revenue chart</t></is></c></row>"
                + "</sheetData><drawing r:id=\"rId1\"/></worksheet>",
            ["xl/worksheets/_rels/sheet2.xml.rels"] =
                $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"{pkgRelNs}\">"
                + $"<Relationship Id=\"rId1\" Type=\"{relNs}/drawing\" Target=\"../drawings/drawing1.xml\"/></Relationships>",
            ["xl/drawings/drawing1.xml"] =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" "
                + $"xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"{relNs}\">"
                + "<xdr:twoCellAnchor><xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>"
                + "<xdr:to><xdr:col>4</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>8</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>"
                + "<xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"2\" name=\"Picture 1\" descr=\"Revenue by quarter\"/><xdr:cNvPicPr/></xdr:nvPicPr>"
                + "<xdr:blipFill><a:blip r:embed=\"rId1\"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill>"
                + "<xdr:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1143000\" cy=\"1143000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr>"
                + "</xdr:pic><xdr:clientData/></xdr:twoCellAnchor></xdr:wsDr>",
            ["xl/drawings/_rels/drawing1.xml.rels"] =
                $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"{pkgRelNs}\">"
                + $"<Relationship Id=\"rId1\" Type=\"{relNs}/image\" Target=\"../media/image1.png\"/></Relationships>",
        };

        var path = Path.Combine(Path.GetTempPath(), $"workbook-picture-{Guid.NewGuid():N}.xlsx");
        await using (var file = File.Create(path))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            foreach (var (name, xml) in parts)
            {
                await using var entry = zip.CreateEntry(name).Open();
                await entry.WriteAsync(Encoding.UTF8.GetBytes(xml), TestContext.Current.CancellationToken);
            }

            await using var media = zip.CreateEntry("xl/media/image1.png").Open();
            await media.WriteAsync(ChartPng, TestContext.Current.CancellationToken);
        }

        return path;
    }

    /// <summary>A valid single-colour RGB PNG of the given size.</summary>
    private static byte[] SolidPng(int width, int height)
    {
        var raw = new byte[height * (1 + width * 3)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (1 + width * 3);
            for (var x = 0; x < width; x++)
                raw[row + 1 + x * 3] = 0xC0;
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 2; // colour type: RGB

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();

        static void Chunk(Stream output, string type, byte[] data)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
            output.Write(length);
            var typeBytes = Encoding.ASCII.GetBytes(type);
            output.Write(typeBytes);
            output.Write(data);
            Span<byte> crc = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32([.. typeBytes, .. data]));
            output.Write(crc);
        }

        static uint Crc32(byte[] bytes)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in bytes)
            {
                crc ^= b;
                for (var k = 0; k < 8; k++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return ~crc;
        }
    }

    private sealed class CountingImageToTextService : IImageToTextService
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public IEnumerable<string> SupportedImageFormats => ["png", "jpeg", "gif"];

        public string ProviderName => "Counting";

        public Task<ImageToTextResult> ExtractTextAsync(
            byte[] imageData, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new ImageToTextResult { ExtractedText = Description, ImageType = "chart" });
        }

        public Task<ImageToTextResult> ExtractTextAsync(
            Stream imageStream, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ImageToTextResult> ExtractTextAsync(
            string imagePath, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
