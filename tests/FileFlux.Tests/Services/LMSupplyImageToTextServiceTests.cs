using FileFlux.Infrastructure.Services;
using LMSupply.Captioner;
using LMSupply.Ocr;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TextRegion = LMSupply.Ocr.TextRegion;

namespace FileFlux.Tests.Services;

/// <summary>
/// Unit tests for LMSupplyImageToTextService.
/// Uses NSubstitute to mock LMSupply.Ocr and LMSupply.Captioner interfaces.
/// </summary>
public sealed class LMSupplyImageToTextServiceTests : IAsyncDisposable
{
    private readonly IOcr _ocr = Substitute.For<IOcr>();
    private readonly ICaptionerModel _captioner = Substitute.For<ICaptionerModel>();
    private readonly ILogger<LMSupplyImageToTextService> _logger =
        Substitute.For<ILogger<LMSupplyImageToTextService>>();

    private static readonly byte[] TestImageData = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]; // PNG header

    public async ValueTask DisposeAsync()
    {
        // Dispose services created by tests
        await ValueTask.CompletedTask;
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_ThrowsOnNullOcr()
    {
        var act = () => new LMSupplyImageToTextService(null!, _logger);
        act.Should().Throw<ArgumentNullException>().WithParameterName("ocr");
    }

    [Fact]
    public void Constructor_ThrowsOnNullLogger()
    {
        var act = () => new LMSupplyImageToTextService(_ocr, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_AcceptsNullCaptioner()
    {
        var sut = new LMSupplyImageToTextService(_ocr, _logger, captioner: null);
        sut.ProviderName.Should().Be("LMSupply.Ocr");
    }

    #endregion

    #region ProviderName Tests

    [Fact]
    public void ProviderName_WithoutCaptioner_ReturnsOcrOnly()
    {
        var sut = new LMSupplyImageToTextService(_ocr, _logger);
        sut.ProviderName.Should().Be("LMSupply.Ocr");
    }

    [Fact]
    public void ProviderName_WithCaptioner_ReturnsOcrPlusCaptioner()
    {
        var sut = new LMSupplyImageToTextService(_ocr, _logger, _captioner);
        sut.ProviderName.Should().Be("LMSupply.Ocr+Captioner");
    }

    #endregion

    #region SupportedImageFormats Tests

    [Fact]
    public void SupportedImageFormats_ContainsCommonFormats()
    {
        var sut = new LMSupplyImageToTextService(_ocr, _logger);
        var formats = sut.SupportedImageFormats.ToList();

        formats.Should().Contain(".png");
        formats.Should().Contain(".jpg");
        formats.Should().Contain(".jpeg");
        formats.Should().Contain(".bmp");
        formats.Should().Contain(".tiff");
        formats.Should().Contain(".tif");
        formats.Should().Contain(".gif");
        formats.Should().Contain(".webp");
    }

    #endregion

    #region ExtractTextAsync (byte[]) Tests

    [Fact]
    public async Task ExtractTextAsync_ReturnsOcrText()
    {
        var ocrResult = CreateOcrResult("Hello from OCR", confidence: 0.95f);
        _ocr.RecognizeAsync(TestImageData, Arg.Any<CancellationToken>())
            .Returns(ocrResult);

        var sut = new LMSupplyImageToTextService(_ocr, _logger);
        var result = await sut.ExtractTextAsync(TestImageData);

        result.ExtractedText.Should().Be("Hello from OCR");
        result.ConfidenceScore.Should().BeApproximately(0.95, 0.01);
        result.ImageType.Should().Be("document");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ExtractTextAsync_WithCaptioner_CombinesCaptionAndOcr()
    {
        var ocrResult = CreateOcrResult("OCR text", confidence: 0.9f);
        _ocr.RecognizeAsync(TestImageData, Arg.Any<CancellationToken>())
            .Returns(ocrResult);

        var captionResult = CreateCaptionResult("A diagram showing system architecture");
        _captioner.CaptionAsync(TestImageData, Arg.Any<CancellationToken>())
            .Returns(captionResult);

        var sut = new LMSupplyImageToTextService(_ocr, _logger, _captioner);
        var result = await sut.ExtractTextAsync(TestImageData);

        result.ExtractedText.Should().Contain("[Image: A diagram showing system architecture]");
        result.ExtractedText.Should().Contain("OCR text");
    }

    [Fact]
    public async Task ExtractTextAsync_WhenCaptionerFails_FallsBackToOcrOnly()
    {
        var ocrResult = CreateOcrResult("OCR text", confidence: 0.9f);
        _ocr.RecognizeAsync(TestImageData, Arg.Any<CancellationToken>())
            .Returns(ocrResult);

        _captioner.CaptionAsync(TestImageData, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Caption service unavailable"));

        var sut = new LMSupplyImageToTextService(_ocr, _logger, _captioner);
        var result = await sut.ExtractTextAsync(TestImageData);

        result.ExtractedText.Should().Be("OCR text");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ExtractTextAsync_WhenOcrFails_ReturnsError()
    {
        _ocr.RecognizeAsync(TestImageData, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("OCR engine failure"));

        var sut = new LMSupplyImageToTextService(_ocr, _logger);
        var result = await sut.ExtractTextAsync(TestImageData);

        result.ErrorMessage.Should().Contain("OCR engine failure");
        result.ExtractedText.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractTextAsync_ReportsProcessingTime()
    {
        var ocrResult = CreateOcrResult("text", confidence: 0.8f);
        _ocr.RecognizeAsync(TestImageData, Arg.Any<CancellationToken>())
            .Returns(ocrResult);

        var sut = new LMSupplyImageToTextService(_ocr, _logger);
        var result = await sut.ExtractTextAsync(TestImageData);

        result.ProcessingTimeMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ExtractTextAsync_UsesLanguageFromOptions()
    {
        var ocrResult = CreateOcrResult("text", confidence: 0.8f);
        _ocr.RecognizeAsync(TestImageData, Arg.Any<CancellationToken>())
            .Returns(ocrResult);

        var options = new ImageToTextOptions { Language = "ko" };
        var sut = new LMSupplyImageToTextService(_ocr, _logger);
        var result = await sut.ExtractTextAsync(TestImageData, options);

        result.DetectedLanguage.Should().Be("ko");
    }

    [Fact]
    public async Task ExtractTextAsync_DefaultsToAutoLanguage()
    {
        var ocrResult = CreateOcrResult("text", confidence: 0.8f);
        _ocr.RecognizeAsync(TestImageData, Arg.Any<CancellationToken>())
            .Returns(ocrResult);

        var sut = new LMSupplyImageToTextService(_ocr, _logger);
        var result = await sut.ExtractTextAsync(TestImageData);

        result.DetectedLanguage.Should().Be("auto");
    }

    [Fact]
    public async Task ExtractTextAsync_WhenNoRegions_ReturnsZeroConfidence()
    {
        var ocrResult = CreateOcrResult("", confidence: 0.0f, regionCount: 0);
        _ocr.RecognizeAsync(TestImageData, Arg.Any<CancellationToken>())
            .Returns(ocrResult);

        var sut = new LMSupplyImageToTextService(_ocr, _logger);
        var result = await sut.ExtractTextAsync(TestImageData);

        result.ConfidenceScore.Should().Be(0.0);
    }

    #endregion

    #region ExtractTextAsync (Stream) Tests

    [Fact]
    public async Task ExtractTextAsync_Stream_DelegatesToByteArray()
    {
        var ocrResult = CreateOcrResult("stream text", confidence: 0.85f);
        _ocr.RecognizeAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(ocrResult);

        using var stream = new MemoryStream(TestImageData);
        var sut = new LMSupplyImageToTextService(_ocr, _logger);
        var result = await sut.ExtractTextAsync(stream);

        result.ExtractedText.Should().Be("stream text");
    }

    #endregion

    #region BuildExtractedText Tests

    [Fact]
    public void BuildExtractedText_BothEmpty_ReturnsEmpty()
    {
        LMSupplyImageToTextService.BuildExtractedText(null, "").Should().BeEmpty();
    }

    [Fact]
    public void BuildExtractedText_CaptionOnly_ReturnsImageTag()
    {
        var result = LMSupplyImageToTextService.BuildExtractedText("a cat", "");
        result.Should().Be("[Image: a cat]");
    }

    [Fact]
    public void BuildExtractedText_OcrOnly_ReturnsOcrText()
    {
        var result = LMSupplyImageToTextService.BuildExtractedText(null, "OCR text");
        result.Should().Be("OCR text");
    }

    [Fact]
    public void BuildExtractedText_Both_CombinesWithSeparator()
    {
        var result = LMSupplyImageToTextService.BuildExtractedText("a diagram", "OCR text");
        result.Should().Be("[Image: a diagram]\n\nOCR text");
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_DisposesOcr()
    {
        // IOcr extends IAsyncDisposable, so substitute is always disposable
        var sut = new LMSupplyImageToTextService(_ocr, _logger);

        await sut.DisposeAsync();

        await _ocr.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_DisposesCaptioner()
    {
        // ICaptionerModel extends IAsyncDisposable
        var sut = new LMSupplyImageToTextService(_ocr, _logger, _captioner);

        await sut.DisposeAsync();

        await _captioner.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_SafeWithoutCaptioner()
    {
        var sut = new LMSupplyImageToTextService(_ocr, _logger, captioner: null);

        // Should not throw
        await sut.DisposeAsync();
    }

    #endregion

    #region Helpers

    private static OcrResult CreateOcrResult(string fullText, float confidence, int regionCount = 1)
    {
        var regions = new List<TextRegion>();
        if (regionCount > 0 && !string.IsNullOrEmpty(fullText))
        {
            for (int i = 0; i < regionCount; i++)
            {
                regions.Add(new TextRegion(
                    Text: fullText,
                    Confidence: confidence,
                    BoundingBox: new LMSupply.Ocr.BoundingBox(X: 0, Y: 0, Width: 100, Height: 20)));
            }
        }

        return new OcrResult(Regions: regions, ProcessingTimeMs: 10.0);
    }

    private static CaptionResult CreateCaptionResult(string caption)
    {
        return new CaptionResult(caption, confidence: 0.9f);
    }

    #endregion
}
