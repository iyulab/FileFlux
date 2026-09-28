using FileFlux.Core;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests;

/// <summary>
/// A <see cref="ChunkingOptions.Strategy"/> that is not one of <see cref="ChunkingStrategies"/> used to run as Auto without a
/// word — the README itself listed four such names (Smart, Intelligent, FixedSize, PageLevel), and the library's own
/// document-type optimizer defaulted to one of them. It is rejected now, before any stage runs.
/// </summary>
public sealed class ChunkingStrategyValidationTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"fileflux-strategy-{Guid.NewGuid():N}.md");
    private readonly ServiceProvider _provider;

    public ChunkingStrategyValidationTests()
    {
        File.WriteAllText(_path, "# Title\n\nFirst paragraph about wind turbines.\n\n## Section\n\nSecond paragraph about storage.\n");
        var services = new ServiceCollection();
        services.AddFileFlux();
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        File.Delete(_path);
    }

    private IDocumentProcessor Create() => _provider.GetRequiredService<IDocumentProcessorFactory>().Create(_path);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Smart")]
    [InlineData("Intelligent")]
    [InlineData("FixedSize")]
    [InlineData("PageLevel")]
    [InlineData("paragraphs")]
    public async Task ProcessAsync_RejectsAnUnknownStrategy_BeforeExtracting(string strategy)
    {
        await using var processor = Create();

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            processor.ProcessAsync(new ProcessingOptions { Chunking = new ChunkingOptions { Strategy = strategy } }, Ct));

        Assert.Contains(strategy, error.Message, StringComparison.Ordinal);
        Assert.Equal(ProcessorState.Created, processor.State);
    }

    [Fact]
    public async Task ChunkAsync_RejectsAnUnknownStrategy()
    {
        await using var processor = Create();

        await Assert.ThrowsAsync<ArgumentException>(() => processor.ChunkAsync(new ChunkingOptions { Strategy = "Smart" }, Ct));
    }

    [Fact]
    public async Task ProcessStreamAsync_RejectsAnUnknownStrategy()
    {
        await using var processor = Create();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in processor.ProcessStreamAsync(
                               new ProcessingOptions { Chunking = new ChunkingOptions { Strategy = "Smart" } }, Ct))
            {
            }
        });
    }

    [Theory]
    [InlineData("Auto")]
    [InlineData("sentence")]
    [InlineData("PARAGRAPH")]
    [InlineData("Token")]
    [InlineData("Hierarchical")]
    [InlineData(null)]
    public async Task KnownStrategies_AnyCase_Chunk(string? strategy)
    {
        await using var processor = Create();

        await processor.ProcessAsync(new ProcessingOptions { Chunking = new ChunkingOptions { Strategy = strategy! } }, Ct);

        Assert.NotEmpty(processor.Result);
    }

    [Fact]
    public void EveryNamedStrategy_Maps()
    {
        foreach (var name in ChunkingStrategies.All)
            ChunkingStrategyMap.ToFluxCurator(name);
    }

    [Fact]
    public void DocumentTypeOptimizer_RecommendsOnlyRealStrategies()
    {
        var optimizer = new DocumentTypeOptimizer();

        Assert.Contains(new PerformanceMetrics().RecommendedStrategy, ChunkingStrategies.All);
        foreach (var category in Enum.GetValues<DocumentCategory>())
        {
            var options = optimizer.GetOptimalOptions(new DocumentTypeInfo { Category = category });
            Assert.Contains(options.Strategy, ChunkingStrategies.All);
        }
    }
}
