using FileFlux.CLI.Services.LMSupply;
using FileFlux.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Integration;

/// <summary>
/// Proves that FileFlux's FluxImprover-backed <see cref="IDocumentEnricher"/> actually runs end to
/// end against a real local model — the mock-only audit
/// (<c>claudedocs/issues/closed/ISSUE-umbrella-20260823-233848-lmsupply-mock-only-verification-scope-broadened.md</c>,
/// HD-21) found FileFlux's own <c>LMSupplyCompletionServiceTests</c>-equivalent coverage was entirely
/// <c>Substitute.For&lt;IGeneratorModel&gt;()</c>, never a real model.
/// </summary>
/// <remarks>
/// Uses <see cref="LMSupplyGeneratorService"/> (FileFlux.CLI's own local-generator adapter over
/// <c>LMSupply.Generator</c>) as the <see cref="IDocumentAnalysisService"/>, wired through
/// <c>ServiceCollectionExtensions.AddFileFlux(IServiceCollection, IDocumentAnalysisService,
/// ServiceLifetime)</c> — the same public composition path a consumer would use, not FileFlux's
/// internal <c>FluxImproverTextCompletionAdapter</c> directly.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class LMSupplyFluxImproverWiringTests
{
    [Fact]
    public async Task DocumentEnricher_RealLocalModel_ProducesSummaryAndKeywords()
    {
        // MaxGenerationTokens trimmed from the 1024 default — this test only needs to prove the
        // pipeline runs end to end, not produce a long summary, and the default made this test take
        // 10+ minutes of CPU-bound ONNX Runtime GenAI inference per run.
        await using var analysisService = await LMSupplyGeneratorService.CreateAsync(
            new LMSupplyOptions { MaxGenerationTokens = 64 });

        var services = new ServiceCollection();
        services.AddLogging();
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

        var result = await enricher.EnrichAsync(
            chunks,
            refined,
            new EnrichOptions { GenerateSummaries = true, ExtractKeywords = true });

        Assert.NotNull(result);
        Assert.Single(result.Chunks);
        var enriched = result.Chunks[0];
        Assert.False(string.IsNullOrWhiteSpace(enriched.Summary));
        Assert.Equal(chunks[0].Content, enriched.Chunk.Content);
    }
}
