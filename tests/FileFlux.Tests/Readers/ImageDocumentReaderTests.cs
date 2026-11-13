using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using FileFlux.Infrastructure.Readers;
using Microsoft.Extensions.DependencyInjection;

namespace FileFlux.Tests.Readers;

/// <summary>
/// A picture file reads through the registered <see cref="IImageToTextService"/>: the service's text is the document,
/// the reader claims exactly the picture extensions that service reads (and none without a service), and a read that
/// found nothing or failed says so instead of passing as an empty success.
/// </summary>
public sealed class ImageDocumentReaderTests : IDisposable
{
    private readonly string _png = Path.Combine(Path.GetTempPath(), $"image-reader-{Guid.NewGuid():N}.png");

    public ImageDocumentReaderTests() => File.WriteAllBytes(_png, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

    public void Dispose() => File.Delete(_png);

    private sealed class FakeImageService(IEnumerable<string> formats, Func<ImageToTextResult>? result = null) : IImageToTextService
    {
        public CancellationToken SeenToken { get; private set; }
        public ImageToTextOptions? SeenOptions { get; private set; }

        public IEnumerable<string> SupportedImageFormats => formats;
        public string ProviderName => "fake-ocr";

        private Task<ImageToTextResult> Read(ImageToTextOptions? options, CancellationToken cancellationToken)
        {
            SeenToken = cancellationToken;
            SeenOptions = options;
            return Task.FromResult((result ?? (() => new ImageToTextResult { ExtractedText = "Total due: 42.00 EUR" }))());
        }

        public Task<ImageToTextResult> ExtractTextAsync(byte[] imageData, ImageToTextOptions? options = null, CancellationToken cancellationToken = default) =>
            Read(options, cancellationToken);

        public Task<ImageToTextResult> ExtractTextAsync(Stream imageStream, ImageToTextOptions? options = null, CancellationToken cancellationToken = default) =>
            Read(options, cancellationToken);

        public Task<ImageToTextResult> ExtractTextAsync(string imagePath, ImageToTextOptions? options = null, CancellationToken cancellationToken = default) =>
            Read(options, cancellationToken);
    }

    private sealed class ThrowingImageService : IImageToTextService
    {
        public IEnumerable<string> SupportedImageFormats => [".png"];
        public string ProviderName => "throwing";

        public Task<ImageToTextResult> ExtractTextAsync(byte[] imageData, ImageToTextOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new InvalidDataException("decoder failed");

        public Task<ImageToTextResult> ExtractTextAsync(Stream imageStream, ImageToTextOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new InvalidDataException("decoder failed");

        public Task<ImageToTextResult> ExtractTextAsync(string imagePath, ImageToTextOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new InvalidDataException("decoder failed");
    }

    [Fact]
    public async Task Extract_TheServicesTextIsTheDocument()
    {
        var service = new FakeImageService([".png"], () => new ImageToTextResult
        {
            ExtractedText = "  Total due: 42.00 EUR  ",
            ConfidenceScore = 0.93,
            DetectedLanguage = "pt",
            Metadata = new ImageMetadata { Width = 640, Height = 480, Format = "PNG" },
        });
        var reader = new ImageDocumentReader(service);

        var raw = await reader.ExtractAsync(_png, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Total due: 42.00 EUR", raw.Text);
        Assert.True(raw.IsSuccess);
        var span = Assert.Single(raw.Spans);
        Assert.Equal((0, raw.Text.Length), (span.Start, span.End));
        Assert.Equal("fake-ocr", raw.Hints["image_provider"]);
        Assert.Equal(640, raw.Hints["image_width"]);
        Assert.Equal(480, raw.Hints["image_height"]);
        Assert.Equal("PNG", raw.Hints["image_format"]);
        // A service's language guess over a few OCR'd lines is not passed on as a fact.
        Assert.False(raw.Hints.ContainsKey("detected_language"));
        Assert.Equal(0.93, raw.Hints["ocr_confidence"]);
        Assert.Equal("document", service.SeenOptions!.ImageTypeHint);
        Assert.False(raw.Hints.ContainsKey(EmptyExtraction.HintKey));
    }

    [Fact]
    public async Task Extract_FromAStream_ReadsThroughTheService()
    {
        var reader = new ImageDocumentReader(new FakeImageService([".jpg"]));
        await using var stream = File.OpenRead(_png);

        var raw = await reader.ExtractAsync(stream, "receipt.jpg", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Total due: 42.00 EUR", raw.Text);
        Assert.Equal("receipt.jpg", raw.File.Name);
        Assert.Equal(".jpg", raw.File.Extension);
    }

    [Fact]
    public async Task Extract_PassesTheCancellationTokenToTheService()
    {
        var service = new FakeImageService([".png"]);
        var reader = new ImageDocumentReader(service);
        using var cts = new CancellationTokenSource();

        await reader.ExtractAsync(_png, cancellationToken: cts.Token);

        Assert.Equal(cts.Token, service.SeenToken);
    }

    [Fact]
    public async Task Extract_AnEmptyReadIsReportedAsNoTextInTheImage()
    {
        var reader = new ImageDocumentReader(new FakeImageService([".png"], () => new ImageToTextResult { ExtractedText = "  " }));

        var raw = await reader.ExtractAsync(_png, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, raw.Text);
        Assert.Equal(ImageDocumentReader.NoTextInImage, raw.Hints[EmptyExtraction.HintKey]);
        Assert.Contains(raw.Warnings, w => w.Contains(ImageDocumentReader.NoTextInImage, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Extract_AServiceErrorIsAFailedReadNotAnEmptySuccess()
    {
        var reader = new ImageDocumentReader(new FakeImageService([".png"], () => new ImageToTextResult { ErrorMessage = "model not loaded" }));

        var raw = await reader.ExtractAsync(_png, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(raw.IsSuccess);
        Assert.Equal(ProcessingStatus.Failed, raw.Status);
        var error = Assert.Single(raw.Errors);
        Assert.Equal(ImageDocumentReader.ReadFailed, error.Code);
        Assert.Contains("model not loaded", error.Message, StringComparison.Ordinal);
        Assert.Equal(ImageDocumentReader.ReadFailed, raw.Hints[EmptyExtraction.HintKey]);
    }

    // A service adapter that catches every exception and answers with an error message (the shape consumers write around an
    // OCR engine, so a bad picture does not throw) also catches the cancellation of the call.
    private sealed class ErrorOnAnyFailureService : IImageToTextService
    {
        public IEnumerable<string> SupportedImageFormats => [".png"];
        public string ProviderName => "catch-all";

        private static async Task<ImageToTextResult> Read(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return new ImageToTextResult { ExtractedText = "never" };
            }
            catch (Exception ex)
            {
                return new ImageToTextResult { ErrorMessage = ex.Message };
            }
        }

        public Task<ImageToTextResult> ExtractTextAsync(byte[] imageData, ImageToTextOptions? options = null, CancellationToken cancellationToken = default) =>
            Read(cancellationToken);

        public Task<ImageToTextResult> ExtractTextAsync(Stream imageStream, ImageToTextOptions? options = null, CancellationToken cancellationToken = default) =>
            Read(cancellationToken);

        public Task<ImageToTextResult> ExtractTextAsync(string imagePath, ImageToTextOptions? options = null, CancellationToken cancellationToken = default) =>
            Read(cancellationToken);
    }

    [Fact]
    public async Task Extract_ACancelledCallIsACancellation_EvenWhenTheServiceAnswersItWithAnErrorMessage()
    {
        var reader = new ImageDocumentReader(new ErrorOnAnyFailureService());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.ExtractAsync(_png, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Extract_FromAStream_ACancelledCallIsACancellation()
    {
        var reader = new ImageDocumentReader(new ErrorOnAnyFailureService());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await using var stream = File.OpenRead(_png);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.ExtractAsync(stream, "receipt.png", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Extract_AnErrorWithoutACancellation_IsStillAFailedRead()
    {
        // The control: the same error answer on a call nobody cancelled is a failed read, not an exception.
        var reader = new ImageDocumentReader(new FakeImageService([".png"], () => new ImageToTextResult { ErrorMessage = "decoder failed" }));
        using var cts = new CancellationTokenSource();

        var raw = await reader.ExtractAsync(_png, cancellationToken: cts.Token);

        Assert.Equal(ProcessingStatus.Failed, raw.Status);
    }

    [Fact]
    public async Task Extract_AServiceExceptionPropagates()
    {
        var reader = new ImageDocumentReader(new ThrowingImageService());

        await Assert.ThrowsAsync<InvalidDataException>(
            () => reader.ExtractAsync(_png, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Extract_WithoutAService_Throws()
    {
        var reader = new ImageDocumentReader(null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ExtractAsync(_png, cancellationToken: TestContext.Current.CancellationToken));
    }

    // The claimed extensions are the picture extensions FileFlux knows intersected with what the service reads, however
    // the service spells its list: the LMSupply OCR service answers dot-less ("png"), the mock service dotted (".png").
    [Theory]
    [InlineData(new[] { ".png", ".jpg" }, new[] { ".jpg", ".png" })]
    [InlineData(new[] { "png", "jpg", "jpeg", "gif", "bmp", "webp" }, new[] { ".bmp", ".gif", ".jpeg", ".jpg", ".png", ".webp" })]
    [InlineData(new[] { "PNG", ".JPG" }, new[] { ".jpg", ".png" })]
    [InlineData(new[] { ".png", ".tif", ".tiff" }, new[] { ".png", ".tif", ".tiff" })]
    [InlineData(new[] { ".png", ".svg", ".heic" }, new[] { ".png" })]
    [InlineData(new string[0], new string[0])]
    public void SupportedExtensions_AreTheIntersectionWithTheServicesList(string[] serviceFormats, string[] expected)
    {
        var reader = new ImageDocumentReader(new FakeImageService(serviceFormats));

        Assert.Equal(expected, reader.SupportedExtensions.OrderBy(e => e, StringComparer.Ordinal));
    }

    [Fact]
    public void ALibraryThatDoesNotReadTiff_LeavesTiffUnclaimed()
    {
        // The LMSupply OCR decoder does not read TIFF, so its service does not list it.
        var reader = new ImageDocumentReader(new FakeImageService(["png", "jpg", "jpeg", "gif", "bmp", "webp"]));

        Assert.False(reader.CanRead("scan.tif"));
        Assert.False(reader.CanRead("scan.tiff"));
        Assert.True(reader.CanRead("scan.PNG"));
    }

    [Fact]
    public void WithoutAService_ThePictureReaderClaimsNoFile()
    {
        Assert.Empty(new ImageDocumentReader(null).SupportedExtensions);

        var services = new ServiceCollection();
        services.AddFileFlux();
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDocumentReaderFactory>();

        Assert.False(factory.CanRead("receipt.png"));
        Assert.False(factory.CanRead("receipt.jpg"));
        Assert.All(factory.GetAllReaders().OfType<ImageDocumentReader>(), r => Assert.Empty(r.SupportedExtensions));
    }

    [Fact]
    public void WithAService_TheFactoryClaimsThePicturesItReads()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IImageToTextService>(new FakeImageService(["png", "jpg"]));
        services.AddFileFlux();
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDocumentReaderFactory>();

        Assert.True(factory.CanRead("receipt.png"));
        Assert.True(factory.CanRead("receipt.JPG"));
        Assert.False(factory.CanRead("receipt.tif"));
    }

    // A DI-less consumer that registers the reader later sees the lists change at once: the factory asks every reader
    // for its extensions on each call.
    [Fact]
    public void ADiLessFactory_SeesALateRegistrationWithoutRebuilding()
    {
        var factory = new FileFlux.Infrastructure.Factories.DocumentReaderFactory();
        Assert.False(factory.IsExtensionSupported(".png"));

        factory.RegisterReader(new ImageDocumentReader(new FakeImageService([".png"])));

        Assert.True(factory.IsExtensionSupported(".png"));
        Assert.Contains(".png", factory.GetSupportedExtensions());
        Assert.Equal("ImageReader", factory.GetExtensionReaderMapping()[".png"]);
    }

    [Fact]
    public async Task ProcessedChunks_CarryTheTextOfThePicture_ThroughTheDiRegisteredReader()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IImageToTextService>(new FakeImageService([".png"]));
        services.AddFileFlux();
        using var provider = services.BuildServiceProvider();

        using var processor = provider.GetRequiredService<IDocumentProcessorFactory>().Create(_png);
        await processor.ChunkAsync(cancellationToken: TestContext.Current.CancellationToken);

        var chunks = processor.Result.Chunks!;
        Assert.NotEmpty(chunks);
        Assert.Contains("Total due: 42.00 EUR", string.Concat(chunks.Select(c => c.Content)), StringComparison.Ordinal);
    }
}

/// <summary>
/// With the LMSupply OCR service registered, a rendered picture of two lines of text (Fixtures/receipt-text.png, drawn
/// for this test from plain text, no third-party content) becomes the text of the document through the whole pipeline.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ImageDocumentReaderIntegrationTests
{
    [Fact]
    public async Task RealOcr_ReadsThePicturesTextThroughTheProcessor()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "receipt-text.png");

        var services = new ServiceCollection();
        FileFlux.Providers.LMSupply.Extensions.ServiceCollectionExtensions.AddLMSupplyOcr(services);
        services.AddFileFlux();
        await using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IDocumentReaderFactory>();
        Assert.True(factory.CanRead(path));
        Assert.False(factory.CanRead("scan.tif")); // the OCR decoder reads no TIFF, so the list does not claim it

        using var processor = provider.GetRequiredService<IDocumentProcessorFactory>().Create(path);
        await processor.ChunkAsync(cancellationToken: ct);

        var text = string.Concat(processor.Result.Chunks!.Select(c => c.Content));
        Assert.Contains("Total due", text, StringComparison.Ordinal);
        Assert.Contains("42.00", text, StringComparison.Ordinal);
    }
}
