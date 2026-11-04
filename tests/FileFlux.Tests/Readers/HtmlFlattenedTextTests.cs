using FileFlux.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Elements the HTML reader writes as one line of text - a table cell, a heading, a link, a caption - keep the words in them
/// apart. The text of such an element was joined from its text nodes with nothing between them, so a line break or two
/// paragraphs inside a cell fused their words (<c>Anna&lt;br&gt;Berta</c> became <c>AnnaBerta</c>). Inline markup inside a word
/// still does not add a space.
/// </summary>
public sealed class HtmlFlattenedTextTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fileflux-html-flat-" + Guid.NewGuid().ToString("N"));

    public HtmlFlattenedTextTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp cleanup is best effort */ }
    }

    private async Task<string> ExtractAsync(string body)
    {
        var path = Path.Combine(_dir, "page.html");
        await File.WriteAllTextAsync(path, "<html><body>" + body + "</body></html>", TestContext.Current.CancellationToken);
        var services = new ServiceCollection();
        services.AddFileFlux();
        await using var provider = services.BuildServiceProvider();
        await using var processor = provider.GetRequiredService<IDocumentProcessorFactory>().Create(path);

        await processor.ExtractAsync(cancellationToken: TestContext.Current.CancellationToken);

        return processor.Result.Raw!.Text;
    }

    [Fact]
    public async Task TableCell_WithALineBreak_KeepsItsWordsApart()
    {
        var text = await ExtractAsync("<table><tr><td>Anna<br>Berta</td><td>Clara</td></tr></table>");

        Assert.Contains("Anna Berta | Clara", text);
    }

    [Fact]
    public async Task TableCell_WithTwoParagraphs_KeepsTheirWordsApart()
    {
        var text = await ExtractAsync("<table><tr><td><p>Dora</p><p>Emil</p></td><td>Franz</td></tr></table>");

        Assert.Contains("Dora Emil | Franz", text);
    }

    [Fact]
    public async Task Heading_WithALineBreak_KeepsItsWordsApart()
    {
        var text = await ExtractAsync("<h2>Gustav<br/>Heinrich</h2>");

        Assert.Contains("## Gustav Heinrich", text);
    }

    [Fact]
    public async Task Link_WithALineBreakInItsText_KeepsItsWordsApart()
    {
        var text = await ExtractAsync("<p><a href=\"https://example.com/x\">Ida<br>Jakob</a></p>");

        Assert.Contains("[Ida Jakob](https://example.com/x)", text);
    }

    [Fact]
    public async Task TableCell_WithInlineMarkupInsideAWord_AddsNoSpace()
    {
        var text = await ExtractAsync("<table><tr><td>un<b>believ</b>able</td><td>x</td></tr></table>");

        Assert.Contains("unbelievable | x", text);
    }
}
