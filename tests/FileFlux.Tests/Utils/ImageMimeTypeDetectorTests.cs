using FileFlux.Core;
using Xunit;

namespace FileFlux.Tests.Utils;

public class ImageMimeTypeDetectorTests
{
    [Fact]
    public void Detect_WithPngMagicBytes_ReturnsPngRegardlessOfResourceId()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0];

        var result = ImageMimeTypeDetector.Detect(png, "rId11");

        Assert.Equal("image/png", result);
    }

    [Fact]
    public void Detect_WithJpegMagicBytes_ReturnsJpegEvenWhenResourceIdHasNoExtension()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

        var result = ImageMimeTypeDetector.Detect(jpeg, "rId11");

        Assert.Equal("image/jpeg", result);
    }

    [Fact]
    public void Detect_WithGifMagicBytes_ReturnsGif()
    {
        byte[] gif = "GIF89a"u8.ToArray();

        var result = ImageMimeTypeDetector.Detect(gif, "resource_1");

        Assert.Equal("image/gif", result);
    }

    [Fact]
    public void Detect_WithWebpMagicBytes_ReturnsWebp()
    {
        byte[] webp = ["R"u8[0], "I"u8[0], "F"u8[0], "F"u8[0], 0, 0, 0, 0, "W"u8[0], "E"u8[0], "B"u8[0], "P"u8[0]];

        var result = ImageMimeTypeDetector.Detect(webp, "resource_1");

        Assert.Equal("image/webp", result);
    }

    [Fact]
    public void Detect_WithNullData_FallsBackToExtensionGuess()
    {
        var result = ImageMimeTypeDetector.Detect(null, "media/image7.png");

        Assert.Equal("image/png", result);
    }

    [Fact]
    public void Detect_WithNoBytesMatchAndNoExtension_ReturnsOctetStream()
    {
        byte[] unknown = [0x01, 0x02, 0x03, 0x04];

        var result = ImageMimeTypeDetector.Detect(unknown, "rId11");

        Assert.Equal("application/octet-stream", result);
    }

    [Fact]
    public void Detect_MismatchBetweenExtensionAndBytes_TrustsBytes()
    {
        // A resource id claiming .png but actually carrying JPEG bytes (renamed/mislabeled source):
        // the real format must win, since consumers dispatch on the returned MIME type.
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE1];

        var result = ImageMimeTypeDetector.Detect(jpeg, "image1.png");

        Assert.Equal("image/jpeg", result);
    }
}
