using FileFlux;
using FileFlux.Infrastructure.Readers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// <see cref="MultiModalPdfDocumentReader"/> calls <c>UnpdfDocument.GetResourceIds</c> to feed its
/// image-captioning pipeline, but until Unpdf 0.15.0 that call always returned an empty inventory
/// (resource extraction was off with no opt-in — docket iyulab/unpdf#125) — so this reader had
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

        var content = await reader.ExtractAsync(ModelCardFixture);

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

        var content = await reader.ExtractAsync(ModelCardFixture);

        Assert.False(content.Hints.ContainsKey("HasImages"));
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
