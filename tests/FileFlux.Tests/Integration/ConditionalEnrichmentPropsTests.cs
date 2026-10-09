using FileFlux.Core;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FluxCurator.Infrastructure.Chunking;
using FluxImprover;
using FluxImprover.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace FileFlux.Tests.Integration;

/// <summary>
/// With conditional enrichment on, every chunk carries the pre-assessment quality score and whether enrichment was
/// skipped, under <see cref="ChunkPropsKeys.QualityScore"/> and <see cref="ChunkPropsKeys.EnrichmentSkipped"/>.
/// </summary>
public sealed class ConditionalEnrichmentPropsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fileflux-conditional-" + Guid.NewGuid().ToString("N"));

    public ConditionalEnrichmentPropsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task SkippedChunks_CarryQualityScoreAndSkipFlag()
    {
        var path = Path.Combine(_root, "doc.md");
        await File.WriteAllTextAsync(path,
            "# Title\n\nA short paragraph about quarterly results and the outlook for the next year.\n",
            TestContext.Current.CancellationToken);

        var completion = Substitute.For<ITextGenerationService>();
        completion.CompleteAsync(Arg.Any<string>(), Arg.Any<CompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns("ok");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFluxImprover(_ => completion);
        using var provider = services.BuildServiceProvider();

        var processor = new FluxDocumentProcessor(
            new DocumentReaderFactory(), new DocumentParserFactory(), new ChunkerFactory(),
            provider.GetRequiredService<FluxImproverServices>());

        var options = new ChunkingOptions
        {
            EnableConditionalEnrichment = true,
            // Every chunk scores at least 0, so every chunk is skipped and no completion is requested.
            ConditionalEnrichmentThreshold = 0f,
        };
        options.CustomProperties["enableEnhancement"] = true;
        options.CustomProperties["enrichKeywords"] = true;

        var chunks = await processor.ProcessAsync(path, options, TestContext.Current.CancellationToken);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk =>
        {
            Assert.Equal(true, chunk.Props[ChunkPropsKeys.EnrichmentSkipped]);
            var score = Assert.IsType<float>(chunk.Props[ChunkPropsKeys.QualityScore]);
            Assert.InRange(score, 0f, 1f);
        });
        await completion.DidNotReceive().CompleteAsync(
            Arg.Any<string>(), Arg.Any<CompletionOptions?>(), Arg.Any<CancellationToken>());
    }
}
