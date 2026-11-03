using FileFlux.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Text that follows an inline link or inline code in HTML stays a separate word. The reader emits each text node
/// trimmed with one trailing space, but wrote links and inline code with no trailing space, so
/// <c>Read &lt;a href="u"&gt;the docs&lt;/a&gt; for more</c> extracted as <c>Read [the docs](u)for more</c>.
/// </summary>
public sealed class HtmlInlineSpacingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fileflux-html-spacing-" + Guid.NewGuid().ToString("N"));

    public HtmlInlineSpacingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp cleanup is best effort */ }
    }

    [Theory]
    [InlineData("<p>Read <a href=\"https://example.com/x\">the docs</a> for more.</p>", "Read [the docs](https://example.com/x) for more.")]
    [InlineData("<p><a href=\"https://example.com/x\">FileFlux</a> reads documents.</p>", "[FileFlux](https://example.com/x) reads documents.")]
    [InlineData("<p>See <a>the docs</a> for more.</p>", "See the docs for more.")]
    [InlineData("<p>Run <code>dotnet build</code> first.</p>", "Run `dotnet build` first.")]
    [InlineData("<p><a href=\"https://example.com/a\">One</a> and <a href=\"https://example.com/b\">two</a> links.</p>", "[One](https://example.com/a) and [two](https://example.com/b) links.")]
    public async Task ExtractedText_KeepsTheWordAfterAnInlineLinkOrCodeSeparate(string body, string expected)
    {
        var path = Path.Combine(_dir, "page.html");
        await File.WriteAllTextAsync(path, "<html><body>" + body + "</body></html>", TestContext.Current.CancellationToken);
        var services = new ServiceCollection();
        services.AddFileFlux();
        await using var provider = services.BuildServiceProvider();
        await using var processor = provider.GetRequiredService<IDocumentProcessorFactory>().Create(path);

        await processor.ExtractAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(expected, processor.Result.Raw!.Text);
    }
}
