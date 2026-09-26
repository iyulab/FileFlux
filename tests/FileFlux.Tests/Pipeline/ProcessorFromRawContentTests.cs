using FileFlux.Core;
using Microsoft.Extensions.DependencyInjection;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// <see cref="IDocumentProcessorFactory.Create(RawContent)"/>: content read earlier (text plus the spans that locate
/// it) runs through refine and chunk without a reader, and the chunks carry the pages the spans name.
/// </summary>
public class ProcessorFromRawContentTests
{
    private static readonly string[] Pages =
    [
        "Page one talks about apples. Apples are red and grow on trees in cool climates.",
        "Page two talks about bananas. Bananas are yellow and grow in warm climates.",
        "Page three talks about cherries. Cherries are dark red and have a stone.",
    ];

    [Fact]
    public async Task Chunks_CarryThePagesTheSpansName()
    {
        var (text, spans) = ThreePages();
        var raw = new RawContent { Text = text, Spans = spans, File = new SourceFileInfo { Name = "stored.md", Extension = ".md" } };

        var chunks = await ChunkAsync(raw);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.NotNull(c.Location.StartPage));
        Assert.Contains(chunks, c => c.Content.Contains("bananas", StringComparison.Ordinal) && c.Location.StartPage <= 2 && c.Location.EndPage >= 2);
        Assert.Equal(3, chunks.Max(c => c.Location.EndPage));
        Assert.DoesNotContain(chunks, c => c.Content.Contains("ffspan", StringComparison.Ordinal));
    }

    // Positive control: the same text without spans yields chunks with no page — the pages above come from the spans.
    [Fact]
    public async Task WithoutSpans_ChunksHaveNoPage()
    {
        var (text, _) = ThreePages();
        var raw = new RawContent { Text = text, File = new SourceFileInfo { Name = "stored.md", Extension = ".md" } };

        var chunks = await ChunkAsync(raw);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.Null(c.Location.StartPage));
    }

    [Fact]
    public void Processor_StartsAtExtracted_WithTheGivenContent()
    {
        var raw = new RawContent { Text = "Stored text.", File = new SourceFileInfo() };
        using var provider = Services();

        using var processor = provider.GetRequiredService<IDocumentProcessorFactory>().Create(raw);

        Assert.Equal(ProcessorState.Extracted, processor.State);
        Assert.Same(raw, processor.Result.Raw);
        Assert.Equal("memory://content.txt", processor.FilePath);
    }

    private static async Task<IReadOnlyList<DocumentChunk>> ChunkAsync(RawContent raw)
    {
        using var provider = Services();
        using var processor = provider.GetRequiredService<IDocumentProcessorFactory>().Create(raw);
        await processor.ChunkAsync(new ChunkingOptions { Strategy = ChunkingStrategies.Paragraph, MaxChunkSize = 120, OverlapSize = 0 },
            TestContext.Current.CancellationToken);
        return processor.Result.Chunks!;
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddFileFlux();
        return services.BuildServiceProvider();
    }

    private static (string Text, List<SourceSpan> Spans) ThreePages()
    {
        var spans = new List<SourceSpan>();
        var text = "";
        for (var i = 0; i < Pages.Length; i++)
        {
            if (i > 0)
                text += "\n\n";
            spans.Add(new SourceSpan(text.Length, text.Length + Pages[i].Length) { Page = i + 1 });
            text += Pages[i];
        }
        return (text, spans);
    }
}
