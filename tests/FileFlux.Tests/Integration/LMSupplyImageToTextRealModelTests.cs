using FileFlux.Infrastructure.Services;
using LMSupply.Captioner;
using LMSupply.Ocr;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FileFlux.Tests.Integration;

/// <summary>
/// Proves that FileFlux's production <see cref="LMSupplyImageToTextService"/> actually runs OCR and
/// captioning end to end against real local models — the mock-only audit
/// (<c>claudedocs/issues/closed/ISSUE-umbrella-20260823-233848-lmsupply-mock-only-verification-scope-broadened.md</c>,
/// HD-21) found <c>LMSupplyImageToTextServiceTests</c> was entirely
/// <c>Substitute.For&lt;IOcr&gt;()</c>/<c>Substitute.For&lt;ICaptionerModel&gt;()</c>, never a real
/// model, and the standing "재현 자산(모델 가중치) 부재" blocker premise had never actually been
/// checked against this environment.
/// </summary>
/// <remarks>
/// A cycle-300 throwaway probe (<c>LocalOcr.LoadAsync()</c> / <c>LocalCaptioner.LoadAsync("default")</c>)
/// confirmed both model families load in this environment without any of the llama-server-specific
/// constraints HD-13 has — this test is the follow-up proof that the loaded models actually run
/// inference through FileFlux's own production adapter, not just that they load.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class LMSupplyImageToTextRealModelTests
{
    [Fact]
    public async Task ExtractTextAsync_RealOcrAndCaptioner_RunsWithoutError()
    {
        var ocr = await LocalOcr.LoadAsync();
        var captioner = await LocalCaptioner.LoadAsync("default");
        var sut = new LMSupplyImageToTextService(
            ocr,
            NullLogger<LMSupplyImageToTextService>.Instance,
            captioner);

        var imageData = CreateSimpleBmp(width: 240, height: 80);

        var result = await sut.ExtractTextAsync(imageData);

        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.ExtractedText);
        Assert.True(result.ProcessingTimeMs >= 0);

        await sut.DisposeAsync();
    }

    /// <summary>
    /// Hand-rolls a valid uncompressed 24bpp BMP (no external imaging library — this repo's
    /// convention is to avoid adding a new dependency for a single test-only image fixture) with a
    /// simple diagonal-stripe pattern, so OCR/captioning inference runs against non-degenerate pixel
    /// data rather than a flat single-color image.
    /// </summary>
    private static byte[] CreateSimpleBmp(int width, int height)
    {
        int rowSize = ((width * 3 + 3) / 4) * 4;
        int pixelDataSize = rowSize * height;
        int fileSize = 54 + pixelDataSize;

        var bytes = new byte[fileSize];

        // BITMAPFILEHEADER
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BitConverter.GetBytes(fileSize).CopyTo(bytes, 2);
        BitConverter.GetBytes(54).CopyTo(bytes, 10); // pixel data offset

        // BITMAPINFOHEADER
        BitConverter.GetBytes(40).CopyTo(bytes, 14); // header size
        BitConverter.GetBytes(width).CopyTo(bytes, 18);
        BitConverter.GetBytes(height).CopyTo(bytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 26); // planes
        BitConverter.GetBytes((short)24).CopyTo(bytes, 28); // bpp
        BitConverter.GetBytes(pixelDataSize).CopyTo(bytes, 34);

        int offset = 54;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool onStripe = ((x + y) / 8) % 2 == 0;
                byte value = onStripe ? (byte)20 : (byte)235;
                int pixelOffset = offset + y * rowSize + x * 3;
                bytes[pixelOffset] = value;     // B
                bytes[pixelOffset + 1] = value; // G
                bytes[pixelOffset + 2] = value; // R
            }
        }

        return bytes;
    }
}
