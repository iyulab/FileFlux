using FileFlux;
using FileFlux.Infrastructure.Readers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// <see cref="MultiModalPdfDocumentReader"/> calls <c>UnpdfDocument.GetResourceIds</c> to feed its
/// image-captioning pipeline, but until Unpdf 0.15.0 that call always returned an empty inventory
/// (resource extraction was off with no opt-in prior to 0.15.0) — so this reader had
/// never actually processed an image, regardless of an <see cref="IImageToTextService"/> being
/// configured. Fixed by passing <c>ParseOptions.ExtractResources = true</c> at parse time
/// (mirrors the same fix in the base <c>PdfDocumentReader</c>).
/// </summary>
public class MultiModalPdfDocumentReaderTests
{
    private static readonly string ModelCardFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "oai_gpt-oss_model_card.pdf");

    [Fact]
    public async Task ExtractAsync_WithImageToTextService_ReportsExtractedImages()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IImageToTextService>(new StubImageToTextService());
        var reader = new MultiModalPdfDocumentReader(services.BuildServiceProvider());

        var content = await reader.ExtractAsync(ModelCardFixture, cancellationToken: TestContext.Current.CancellationToken);

        // Regression guard: before the ExtractResources fix, HasImages/TotalImageCount never
        // appeared because GetResourceIds() always returned an empty array.
        Assert.True((bool)content.Hints["HasImages"]);
        Assert.True((int)content.Hints["TotalImageCount"] > 0);
    }

    [Fact]
    public async Task ExtractAsync_WithoutImageToTextService_ReturnsBaseContentUnchanged()
    {
        var services = new ServiceCollection();
        var reader = new MultiModalPdfDocumentReader(services.BuildServiceProvider());

        var content = await reader.ExtractAsync(ModelCardFixture, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(content.Hints.ContainsKey("HasImages"));
    }

    private static readonly string ImageOnlyFixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "image-only.pdf");

    // A page with no text layer, selected by the page record, is rendered (a PNG reaches the service) and its read
    // becomes the page's text — on the file path and the stream path alike.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PageReading_AScannedPage_IsRenderedAndReadIntoTheText(bool fromStream)
    {
        var service = new RecordingImageToTextService("Text read from the scanned page.");
        var services = new ServiceCollection();
        services.AddSingleton<IImageToTextService>(service);
        var reader = new MultiModalPdfDocumentReader(services.BuildServiceProvider());
        var options = new FileFlux.Core.ExtractOptions
        {
            ExtractImages = false,
            PageReading = new FileFlux.Core.PageReadingOptions { SelectPages = q => !q.HasTextLayer && q.ImageCoverage > 0.5 }
        };
        var ct = TestContext.Current.CancellationToken;

        FileFlux.Core.RawContent content;
        if (fromStream)
        {
            await using var stream = File.OpenRead(ImageOnlyFixture);
            content = await reader.ExtractAsync(stream, "image-only.pdf", options, ct);
        }
        else
        {
            content = await reader.ExtractAsync(ImageOnlyFixture, options, ct);
        }

        var read = Assert.Single(content.PageReads);
        Assert.Equal(FileFlux.Core.PageReadOutcome.Replaced, read.Outcome);
        Assert.Equal(0, read.UnrenderedImages);
        var png = Assert.Single(service.Images);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
        Assert.Contains("Text read from the scanned page.", content.Text, StringComparison.Ordinal);
        var span = Assert.Single(content.Spans);
        Assert.Equal(1, span.Page);
        Assert.Equal("Text read from the scanned page.", content.Text[span.Start..span.End]);
    }

    [Fact]
    public async Task PageReading_WithoutAnImageToTextService_SaysSoAndRendersNothing()
    {
        var reader = new MultiModalPdfDocumentReader(new ServiceCollection().BuildServiceProvider());
        var options = new FileFlux.Core.ExtractOptions { PageReading = new FileFlux.Core.PageReadingOptions { SelectPages = _ => true } };

        var content = await reader.ExtractAsync(ImageOnlyFixture, options, TestContext.Current.CancellationToken);

        Assert.Empty(content.PageReads);
        Assert.Contains(content.Warnings, w => w.Contains("IImageToTextService", StringComparison.Ordinal));
    }

    private sealed class RecordingImageToTextService(string text) : IImageToTextService
    {
        public List<byte[]> Images { get; } = [];

        public IEnumerable<string> SupportedImageFormats => ["png"];

        public string ProviderName => "Recording";

        public Task<ImageToTextResult> ExtractTextAsync(
            byte[] imageData, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
        {
            Images.Add(imageData);
            return Task.FromResult(new ImageToTextResult { ExtractedText = text });
        }

        public Task<ImageToTextResult> ExtractTextAsync(
            Stream imageStream, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ImageToTextResult> ExtractTextAsync(
            string imagePath, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
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
}
