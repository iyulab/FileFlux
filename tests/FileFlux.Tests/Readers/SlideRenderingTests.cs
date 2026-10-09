using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using FileFlux.Infrastructure.Readers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// <see cref="ExtractOptions.SlideRendering"/>: a slide drawn with shapes and connectors keeps its labels in the text but
/// not its picture, so the selected slides are rendered and added to the images with their slide number. The fixture's
/// slide 2 is a three-box flow with two arrows; slides 1 and 3 are title-and-body text.
/// </summary>
public class SlideRenderingTests
{
    private static readonly string Deck = Path.Combine(AppContext.BaseDirectory, "Fixtures", "diagram-slides.pptx");
    // A JPEG photo on slide 1.
    private static readonly string PictureDeck = Path.Combine(AppContext.BaseDirectory, "Fixtures", "picture-slides.pptx");
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void Composition_IsCountedPerSlide_InPresentationOrder()
    {
        using var package = File.OpenRead(Deck);

        var slides = SlideRenderer.ReadCompositions(package);

        Assert.Equal(3, slides.Count);
        Assert.Equal(new SlideComposition(1, Placeholders: 2, TextBoxes: 0, Shapes: 0, Connectors: 0, Pictures: 0, GraphicFrames: 0), slides[0]);
        Assert.Equal(new SlideComposition(2, Placeholders: 1, TextBoxes: 0, Shapes: 3, Connectors: 2, Pictures: 0, GraphicFrames: 0), slides[1]);
        Assert.False(SlideRenderingOptions.DrawnSlides(slides[0]));
        Assert.True(SlideRenderingOptions.DrawnSlides(slides[1]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DrawnSlides_RendersTheDiagramSlide_AndNoOther(bool fromStream)
    {
        var content = await ExtractAsync(fromStream, new SlideRenderingOptions { SelectSlides = SlideRenderingOptions.DrawnSlides });

        var rendered = Assert.Single(content.Images, i => i.RenderedPage is not null);
        Assert.Equal(2, rendered.PageNumber);
        Assert.Equal("image/png", rendered.MimeType);
        Assert.Equal(PngSignature, rendered.Data![..8]);
        Assert.Equal(150, rendered.RenderedPage!.Dpi);
        // A 10 x 7.5 inch slide at 150 dpi.
        Assert.Equal(1500, rendered.RenderedPage.Width);
        Assert.Equal(1125, rendered.RenderedPage.Height);
        Assert.True(rendered.RenderedPage.IsComplete);
        Assert.Equal(1, content.Hints["rendered_slides"]);
        // The labels stay in the text; the render adds the drawing, it does not replace anything.
        Assert.Contains("Ingest", content.Text);
    }

    [Fact]
    public async Task WithoutSlideRendering_NoSlideIsRendered()
    {
        var content = await new PowerPointDocumentReader().ExtractAsync(Deck, cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(content.Images, i => i.RenderedPage is not null);
        Assert.False(content.Hints.ContainsKey("rendered_slides"));
    }

    [Fact]
    public async Task MaxSlides_RendersThatMany_AndNamesTheRest()
    {
        var content = await ExtractAsync(fromStream: false, new SlideRenderingOptions { SelectSlides = SlideRenderingOptions.AllSlides, MaxSlides = 1, Dpi = 72 });

        var rendered = Assert.Single(content.Images, i => i.RenderedPage is not null);
        Assert.Equal(1, rendered.PageNumber);
        Assert.Equal(720, rendered.RenderedPage!.Width);
        Assert.Equal(2, content.Warnings.Count(w => w.Contains("MaxSlides 1", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task WithExtractImagesOff_NothingIsRendered_AndAWarningSaysSo()
    {
        var options = new ExtractOptions
        {
            ExtractImages = false,
            SlideRendering = new SlideRenderingOptions { SelectSlides = SlideRenderingOptions.AllSlides },
        };

        var content = await new PowerPointDocumentReader().ExtractAsync(Deck, options, TestContext.Current.CancellationToken);

        Assert.Empty(content.Images);
        Assert.Contains(content.Warnings, w => w.Contains("ExtractImages is false", StringComparison.Ordinal));
    }

    // ── The multimodal reader describes what the base reader extracted ─────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheMultiModalReader_DescribesTheRenderedSlide_WithItsSlideNumber(bool fromStream)
    {
        var service = new RecordingImageToTextService();
        var content = await ExtractMultiModalAsync(service, fromStream,
            new ExtractOptions { SlideRendering = new SlideRenderingOptions { SelectSlides = SlideRenderingOptions.DrawnSlides } });

        var described = Assert.Single(service.Images);
        Assert.Equal(PngSignature, described[..8]);
        Assert.Contains("Slide 2 (rendered):", content.Text, StringComparison.Ordinal);
        Assert.Contains("described image", content.Text, StringComparison.Ordinal);
        Assert.Equal("MultiModalPowerPointReader", content.ReaderType);
    }

    [Fact]
    public async Task TheMultiModalReader_DescribesEmbeddedPictures_OnTheStreamPathToo()
    {
        // Before, the stream path returned the base content: no picture was described for a caller that read from a stream.
        var deck = PictureDeck;
        var service = new RecordingImageToTextService();
        var services = new ServiceCollection().AddSingleton<IImageToTextService>(service).BuildServiceProvider();
        var reader = new MultiModalPowerPointDocumentReader(services);

        var fromFile = await reader.ExtractAsync(deck, cancellationToken: TestContext.Current.CancellationToken);
        var describedFromFile = service.Images.Count;
        service.Images.Clear();
        await using var stream = File.OpenRead(deck);
        var fromStream = await reader.ExtractAsync(stream, "picture-slides.pptx", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(describedFromFile > 0);
        Assert.Equal(describedFromFile, service.Images.Count);
        Assert.Equal(fromFile.Text, fromStream.Text);
    }

    [Fact]
    public async Task TheMultiModalReader_DescribesNothing_WhenImagesAreNotExtracted()
    {
        var service = new RecordingImageToTextService();
        var deck = PictureDeck;
        var services = new ServiceCollection().AddSingleton<IImageToTextService>(service).BuildServiceProvider();

        await new MultiModalPowerPointDocumentReader(services)
            .ExtractAsync(deck, new ExtractOptions { ExtractImages = false }, TestContext.Current.CancellationToken);

        Assert.Empty(service.Images);
    }

    private static async Task<RawContent> ExtractMultiModalAsync(IImageToTextService service, bool fromStream, ExtractOptions options)
    {
        var services = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
        var reader = new MultiModalPowerPointDocumentReader(services);
        if (!fromStream)
            return await reader.ExtractAsync(Deck, options, TestContext.Current.CancellationToken);

        await using var stream = File.OpenRead(Deck);
        return await reader.ExtractAsync(stream, "diagram-slides.pptx", options, TestContext.Current.CancellationToken);
    }

    private sealed class RecordingImageToTextService : IImageToTextService
    {
        public List<byte[]> Images { get; } = [];

        public IEnumerable<string> SupportedImageFormats => ["jpeg", "png"];

        public string ProviderName => "Recording";

        public Task<ImageToTextResult> ExtractTextAsync(byte[] imageData, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
        {
            Images.Add(imageData);
            return Task.FromResult(new ImageToTextResult { ExtractedText = "described image" });
        }

        public Task<ImageToTextResult> ExtractTextAsync(Stream imageStream, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ImageToTextResult> ExtractTextAsync(string imagePath, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static async Task<RawContent> ExtractAsync(bool fromStream, SlideRenderingOptions rendering)
    {
        var options = new ExtractOptions { SlideRendering = rendering };
        var reader = new PowerPointDocumentReader();
        if (!fromStream)
            return await reader.ExtractAsync(Deck, options, TestContext.Current.CancellationToken);

        await using var stream = File.OpenRead(Deck);
        return await reader.ExtractAsync(stream, "diagram-slides.pptx", options, TestContext.Current.CancellationToken);
    }
}
