using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FileFlux.Tests.Services;

/// <summary>
/// An image-to-text adapter that answers every exception with <see cref="ImageToTextResult.ErrorMessage"/> answers the
/// caller's cancellation with one too. Each caller that reads such a result must surface the cancellation, not carry on
/// as if the picture simply had no text: with one picture there is no later loop check to catch it.
/// </summary>
public sealed class ImageToTextCancellationTests
{
    // A 1x1 PNG: valid image bytes, below every default size filter (the tests zero the filters).
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class CancelsThenAnswersWithAnError(CancellationTokenSource source) : IImageToTextService
    {
        public IEnumerable<string> SupportedImageFormats => ["png"];

        public string ProviderName => "CancelsThenErrors";

        public Task<ImageToTextResult> ExtractTextAsync(byte[] imageData, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
        {
            source.Cancel();
            return Task.FromResult(new ImageToTextResult { ErrorMessage = "The operation was canceled." });
        }

        public Task<ImageToTextResult> ExtractTextAsync(Stream imageStream, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ImageToTextResult> ExtractTextAsync(string imagePath, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public async Task Refine_AnImageReadCancelledMidCall_IsACancellation()
    {
        using var cts = new CancellationTokenSource();
        var services = new ServiceCollection();
        services.AddFileFlux();
        services.AddSingleton<IImageToTextService>(new CancelsThenAnswersWithAnError(cts));
        using var provider = services.BuildServiceProvider();
        var processor = provider.GetRequiredService<FluxDocumentProcessor>();

        var raw = new RawContent { Text = "before ![](embedded:img1) after" };
        raw.Images.Add(new ImageInfo { Id = "img1", Data = Png, MimeType = "image/png" });
        var options = new RefiningOptions { ProcessImagesToText = true };
        options.Extra["_rawContent"] = raw;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => processor.RefineAsync(new RefinedContent { Text = raw.Text }, options, cts.Token));
    }

    [Fact]
    public async Task ProcessPreExtractedImages_AnAiReadCancelledMidCall_IsACancellation()
    {
        using var cts = new CancellationTokenSource();
        var directory = Path.Combine(Path.GetTempPath(), $"image-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var processor = new ImageProcessor(new OutputOptions { EnableAI = true, MinImageSize = 0, MinImageDimension = 0 });
            var images = new List<ImageInfo> { new() { Id = "img1", Data = Png, MimeType = "image/png" } };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessPreExtractedImagesAsync(
                "![](embedded:img1)", images, directory, new CancelsThenAnswersWithAnError(cts), cts.Token));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessPreExtractedImages_AnAiErrorOnACallNobodyCancelled_IsStillRecordedAsTheImagesError()
    {
        // The control: the same error answer without a cancellation is no exception.
        var directory = Path.Combine(Path.GetTempPath(), $"image-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var neverCancelled = new CancellationTokenSource();
            var processor = new ImageProcessor(new OutputOptions { EnableAI = true, MinImageSize = 0, MinImageDimension = 0 });
            var images = new List<ImageInfo> { new() { Id = "img1", Data = Png, MimeType = "image/png" } };

            var result = await processor.ProcessPreExtractedImagesAsync(
                "![](embedded:img1)", images, directory, new CancelsThenAnswersWithAnError(new CancellationTokenSource()), neverCancelled.Token);

            Assert.Single(result.Images);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
