using FileFlux.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// A paragraph that begins with a link keeps its link text through extraction and refinement. The Markdown converter once
/// read <c>[text](url)</c> at the start of a line as an alphabetic list marker, so <c>[FileFlux](https://...) reads</c>
/// came out as <c>- (https://...) reads</c> for Markdown, plain text and HTML (anchor first in a paragraph) alike.
/// </summary>
public sealed class LinkLeadingParagraphTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fileflux-link-leading-" + Guid.NewGuid().ToString("N"));

    public LinkLeadingParagraphTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp cleanup is best effort */ }
    }

    [Theory]
    [InlineData("link-first.md", "[FileFlux](https://example.com/fileflux) reads documents and returns chunks.\n")]
    [InlineData("link-first.txt", "[FileFlux](https://example.com/fileflux) reads documents and returns chunks.\n")]
    [InlineData("link-first.html", "<html><body><p><a href=\"https://example.com/fileflux\">FileFlux</a> reads documents and returns chunks.</p></body></html>")]
    public async Task RefinedText_KeepsTheTextOfALinkThatStartsTheParagraph(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
        var services = new ServiceCollection();
        services.AddFileFlux();
        await using var provider = services.BuildServiceProvider();
        await using var processor = provider.GetRequiredService<IDocumentProcessorFactory>().Create(path);

        await processor.RefineAsync(cancellationToken: TestContext.Current.CancellationToken);
        var text = processor.Result.Refined!.Text;

        Assert.Contains("[FileFlux](https://example.com/fileflux)", text);
        Assert.DoesNotContain("- (https://", text);
    }
}
