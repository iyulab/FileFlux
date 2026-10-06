using FileFlux;
using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using FileFlux.Infrastructure.Readers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// <see cref="RawContent.Quality"/> carries one <see cref="PageQuality"/> per PDF page — the facts a consumer needs to
/// pick the pages it reads another way. Fixtures are the reader fixtures of the no-text-layer and reading-order tests.
/// </summary>
public class PdfPageQualityTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private readonly PdfDocumentReader _reader = new();

    private Task<RawContent> Extract(string name) =>
        _reader.ExtractAsync(Fixture(name), cancellationToken: TestContext.Current.CancellationToken);

    [Fact]
    public async Task TextPage_HasATextLayerAndItsCharactersAreThePageText()
    {
        var content = await Extract("two-column-narrow-gutter.pdf");

        var page = Assert.Single(content.Quality.Pages);
        Assert.Equal(1, page.Page);
        Assert.True(page.HasTextLayer);
        Assert.True(page.TextOperators > 0);
        Assert.Equal(0, page.ReplacementCharacters);
        var span = Assert.Single(content.Spans);
        Assert.Equal(span.End - span.Start, page.Characters);
        Assert.True(page.Characters > 0);
    }

    [Fact]
    public async Task ImageOnlyPage_HasNoTextLayerAndPaintsAnImage()
    {
        var content = await Extract("image-only.pdf");

        var page = Assert.Single(content.Quality.Pages);
        Assert.False(page.HasTextLayer);
        Assert.Equal(0, page.TextOperators);
        Assert.True(page.ImageOperators > 0);
    }

    [Fact]
    public async Task BlankPage_ReportsNothingOnIt()
    {
        var content = await Extract("blank-page.pdf");

        var page = Assert.Single(content.Quality.Pages);
        Assert.Equal(new PageQuality(1), page);
    }

    [Fact]
    public async Task UndecodableStream_IsCountedOnItsPage()
    {
        var content = await Extract("undecodable-content-stream.pdf");

        Assert.Contains(content.Quality.Pages, p => p.UndecodableContentStreams > 0);
    }

    [Fact]
    public async Task TextInsideAForm_CountsTheFormAndItsText()
    {
        var content = await Extract("form-xobject-text.pdf");

        var page = Assert.Single(content.Quality.Pages);
        Assert.True(page.FormOperators > 0);
        Assert.True(page.HasTextLayer);
    }

    [Fact]
    public async Task MultiPageDocument_ReportsEveryPageInOrderAndTheirCharactersAddUpToTheSpans()
    {
        var content = await Extract("oai_gpt-oss_model_card.pdf");

        var pages = content.Quality.Pages;
        Assert.True(pages.Count > 1);
        Assert.Equal(Enumerable.Range(1, pages.Count), pages.Select(p => p.Page));
        Assert.Equal(content.Spans.Sum(s => s.End - s.Start), pages.Sum(p => p.Characters));
    }

    [Fact]
    public async Task MultiModalReader_CarriesThePageRecordOfItsBaseReader()
    {
        // With an image service the wrapper rebuilds the result (captions appended) — the path that must carry it.
        var services = new ServiceCollection();
        services.AddSingleton<IImageToTextService>(new StubImageToTextService());
        var reader = new MultiModalPdfDocumentReader(services.BuildServiceProvider());

        var content = await reader.ExtractAsync(Fixture("oai_gpt-oss_model_card.pdf"),
            cancellationToken: TestContext.Current.CancellationToken);
        var baseContent = await Extract("oai_gpt-oss_model_card.pdf");

        Assert.True((bool)content.Hints["HasImages"]);
        Assert.NotEmpty(content.Quality.Pages);
        Assert.Equal(baseContent.Quality.Pages, content.Quality.Pages);
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
