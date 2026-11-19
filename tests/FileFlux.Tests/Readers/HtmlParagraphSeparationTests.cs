using FileFlux.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Paragraphs of an HTML page are separated by a blank line in the text the reader writes, as they are in Markdown. Two
/// paragraphs were written one line apart, which Markdown reads as one paragraph and which the paragraph-aware chunkers
/// treat as one paragraph too: a page of <c>&lt;p&gt;</c> elements was cut in the middle of a paragraph where the same
/// text as Markdown is cut between two.
/// </summary>
public sealed class HtmlParagraphSeparationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fileflux-html-para-" + Guid.NewGuid().ToString("N"));

    public HtmlParagraphSeparationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp cleanup is best effort */ }
    }

    private async Task<IDocumentProcessor> OpenAsync(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
        var services = new ServiceCollection();
        services.AddFileFlux();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IDocumentProcessorFactory>().Create(path);
    }

    private async Task<string> ExtractAsync(string body)
    {
        await using var processor = await OpenAsync("page.html", "<html><body>" + body + "</body></html>");
        await processor.ExtractAsync(cancellationToken: TestContext.Current.CancellationToken);
        return processor.Result.Raw!.Text;
    }

    private static string[] Lines(string text) => text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).ToArray();

    [Fact]
    public async Task ConsecutiveParagraphs_AreSeparatedByABlankLine()
    {
        var text = await ExtractAsync("<p>One.</p><p>Two.</p><p>Three.</p>");

        Assert.Equal(["One.", "", "Two.", "", "Three."], Lines(text).Where((l, i) => i < 5).ToArray());
    }

    [Fact]
    public async Task SiblingDivsWithText_AreSeparatedByABlankLine()
    {
        var text = await ExtractAsync("<div>Div one</div><div>Div two</div>");

        var lines = Lines(text);
        var one = Array.IndexOf(lines, "Div one");
        Assert.True(one >= 0);
        Assert.Equal("", lines[one + 1]);
        Assert.Equal("Div two", lines[one + 2]);
    }

    [Fact]
    public async Task ParagraphInsideAListItem_StaysOnTheItemLine()
    {
        var text = await ExtractAsync("<ul><li><p>first item</p></li><li><p>second item</p></li></ul>");

        var lines = Lines(text);
        Assert.Contains(lines, l => l.Contains("- first item"));
        Assert.Contains(lines, l => l.Contains("- second item"));
    }

    [Fact]
    public async Task ParagraphsInATableCell_StayInTheCell()
    {
        var text = await ExtractAsync("<table><tr><td><p>Dora</p><p>Emil</p></td><td>Franz</td></tr></table>");

        Assert.Contains("Dora Emil | Franz", text);
    }

    [Fact]
    public async Task NestedBlocks_DoNotStackBlankLines()
    {
        var text = await ExtractAsync("<div><section><p>One.</p></section><section><p>Two.</p></section></div>");

        Assert.DoesNotContain("\n\n\n", text.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task DefinitionList_WritesOneLinePerTerm_WithItsDefinitions()
    {
        var text = await ExtractAsync(
            "<p>Before.</p><dl><dt>Term A</dt><dd>Definition of A.</dd><dt>Term B</dt><dd>First.</dd><dd>Second.</dd><dt>Term C</dt><dt>Term D</dt><dd>Shared.</dd></dl><p>After.</p>");

        var lines = Lines(text);
        Assert.Contains("Term A: Definition of A.", lines);
        Assert.Contains("Term B: First. Second.", lines);
        Assert.Contains("Term C, Term D: Shared.", lines);
        // set apart from the paragraphs around it
        var first = Array.IndexOf(lines, "Term A: Definition of A.");
        Assert.Equal("", lines[first - 1]);
        Assert.Equal("", lines[Array.IndexOf(lines, "Term C, Term D: Shared.") + 1]);
    }

    [Fact]
    public async Task Blockquote_KeepsItsQuoteMarker_AndSeparatesItsParagraphs()
    {
        var text = await ExtractAsync("<p>Before.</p><blockquote><p>Quoted one.</p><p>Quoted two.</p></blockquote><p>After.</p>");

        var lines = Lines(text);
        var one = Array.IndexOf(lines, "> Quoted one.");
        Assert.True(one >= 0, "the quoted paragraph carries the quote marker");
        Assert.Equal(">", lines[one + 1]);
        Assert.Equal("> Quoted two.", lines[one + 2]);
        Assert.Equal("", lines[one - 1]);
        Assert.Equal("", lines[one + 3]);
        Assert.Equal("After.", lines[one + 4]);
    }

    [Fact]
    public async Task Blockquote_AroundACodeBlock_KeepsTheFenceAndItsIndentation()
    {
        await using var processor = await OpenAsync("quote-code.html", "<html><body><blockquote><p>Run this:</p><pre>first\n    indented\nlast</pre></blockquote></body></html>");

        await processor.ProcessAsync(cancellationToken: TestContext.Current.CancellationToken);

        var chunk = string.Join("\n", processor.Result.Select(c => c.Content)).Replace("\r\n", "\n");
        Assert.Contains("> Run this:", chunk);
        Assert.Contains("```\nfirst\n    indented\nlast\n```", chunk);
    }

    [Fact]
    public async Task NestedBlockquote_DoublesTheMarker()
    {
        var text = await ExtractAsync("<blockquote><p>Outer.</p><blockquote><p>Inner.</p></blockquote></blockquote>");

        var lines = Lines(text);
        Assert.Contains("> Outer.", lines);
        Assert.Contains("> > Inner.", lines);
    }

    [Fact]
    public async Task DefinitionList_WithoutDefinitions_KeepsTheTerm()
    {
        var text = await ExtractAsync("<dl><dt>Lonely term</dt></dl>");

        Assert.Contains("Lonely term", Lines(text));
    }

    [Fact]
    public async Task ParagraphPage_IsChunkedBetweenParagraphs_NotInsideOne()
    {
        var paragraphs = Enumerable.Range(1, 24)
            .Select(i => $"Paragraph {i} starts here and carries a few sentences. It talks about topic number {i} at some length. The last sentence of paragraph {i} ends here.");
        var html = "<html><body><h1>Many</h1>" + string.Join("\n", paragraphs.Select(p => $"<p>{p}</p>")) + "</body></html>";
        await using var processor = await OpenAsync("many.html", html);

        await processor.ProcessAsync(new ProcessingOptions
        {
            Chunking = new ChunkingOptions { Strategy = ChunkingStrategies.Paragraph, MaxChunkSize = 500, OverlapSize = 0 },
        }, TestContext.Current.CancellationToken);

        var chunks = processor.Result.ToList();
        Assert.True(chunks.Count > 1);
        foreach (var chunk in chunks.Where(c => !c.Content.TrimStart().StartsWith('#')))
        {
            Assert.StartsWith("Paragraph ", chunk.Content.TrimStart());
            Assert.EndsWith("ends here.", chunk.Content.TrimEnd());
        }
    }
}
