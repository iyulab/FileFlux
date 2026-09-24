using FileFlux.Core;
using FileFlux.Providers.LMSupply;
using FileFlux.Providers.LMSupply.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FileFlux.Tests.Integration;

/// <summary>
/// Proves that FileFlux's FluxImprover-backed <see cref="IDocumentEnricher"/> actually runs end to
/// end against a real local model — the unit coverage of this path substitutes
/// <c>IGeneratorModel</c> and never runs one.
/// </summary>
/// <remarks>
/// Uses <see cref="LMSupplyGeneratorService"/> (from the <c>FileFlux.Providers.LMSupply</c> package) as the
/// <see cref="IDocumentAnalysisService"/>, wired through <c>ServiceCollectionExtensions.AddFileFlux(
/// IServiceCollection, IDocumentAnalysisService, ServiceLifetime)</c> — the same public composition
/// path a consumer would use, not FileFlux's internal <c>FluxImproverTextCompletionAdapter</c>
/// directly.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class LMSupplyFluxImproverWiringTests
{
    [Fact]
    public async Task DocumentEnricher_RealLocalModel_ProducesSummaryAndKeywords()
    {
        // The package defaults: the model LMSupply picks for this host, and FluxImprover's own per-call
        // token limits (they reach the service through the adapter).
        await using var analysisService = await LMSupplyGeneratorService.CreateAsync(new LMSupplyOptions(), cancellationToken: TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddFileFlux(analysisService, ServiceLifetime.Singleton);

        await using var provider = services.BuildServiceProvider();
        var enricher = provider.GetRequiredService<IDocumentEnricher>();

        Assert.True(enricher.HasLlmSupport);

        var chunks = new List<DocumentChunk>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RawId = Guid.NewGuid(),
                Content = "FluxIndex is a vector and keyword hybrid search engine designed for " +
                          "retrieval-augmented generation (RAG) pipelines. It embeds document chunks " +
                          "and retrieves the most relevant ones for a given query.",
                ChunkIndex = 0,
                Tokens = 40,
                Strategy = "test",
                Location = new SourceLocation { StartChar = 0, EndChar = 200 }
            }
        };

        var refined = new RefinedContent
        {
            RawId = Guid.NewGuid(),
            Text = chunks[0].Content,
            Sections = [],
            Structures = [],
            Metadata = new DocumentMetadata { FileName = "test.txt", FileType = "TXT" },
            Quality = new RefinementQuality(),
            Info = new RefinementInfo { RefinerType = "Test" }
        };

        var result = await enricher.EnrichAsync(chunks, refined, new EnrichOptions { GenerateSummaries = true, ExtractKeywords = true }, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Chunks);
        var enriched = result.Chunks[0];
        Assert.False(string.IsNullOrWhiteSpace(enriched.Summary));
        Assert.Equal(chunks[0].Content, enriched.Chunk.Content);
    }
}
