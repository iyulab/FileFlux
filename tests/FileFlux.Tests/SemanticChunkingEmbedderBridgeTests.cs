using FileFlux.Infrastructure;
using FluxCurator.Core.Core;
using FluxCurator.Core.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests;

/// <summary>
/// Semantic chunking runs on FluxCurator, which reads its own <see cref="IEmbedder"/>. The container's Microsoft.Extensions.AI
/// embedding generator (<c>AddLMSupplyEmbedding</c>, OpenAI, IronHive, a consumer's own) reaches it through FluxCurator's
/// adapter, with no FileFlux-specific embedding interface to implement.
/// </summary>
public sealed class SemanticChunkingEmbedderBridgeTests
{
    private sealed class CountingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private int _texts;

        public int Texts => Volatile.Read(ref _texts);

        public EmbeddingGeneratorMetadata Metadata { get; } = new("test", null, "two-topics", 2);

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var embeddings = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var text in values)
            {
                Interlocked.Increment(ref _texts);
                float[] vector = text.Contains("revenue", StringComparison.OrdinalIgnoreCase) ? [0f, 1f] : [1f, 0f];
                embeddings.Add(new Embedding<float>(vector));
            }

            return Task.FromResult(embeddings);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType == typeof(EmbeddingGeneratorMetadata) ? Metadata : null;

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task A_registered_embedding_generator_drives_semantic_chunking()
    {
        var generator = new CountingGenerator();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(generator);
        services.AddFileFlux();
        using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IChunkerFactory>();
        Assert.True(factory.IsStrategyAvailable(ChunkingStrategy.Semantic));
        Assert.Equal(2, provider.GetRequiredService<IEmbedder>().EmbeddingDimension);

        var chunks = await factory.CreateChunker(ChunkingStrategy.Semantic).ChunkAsync(
            "Cats purr and sleep. Kittens are young cats.\n\nQuarterly revenue grew. The revenue outlook is strong.",
            new ChunkOptions(),
            TestContext.Current.CancellationToken);

        Assert.NotEmpty(chunks);
        Assert.True(generator.Texts > 0, "the container's embedding generator is the one semantic chunking uses");
    }

    [Fact]
    public void Without_an_embedding_generator_semantic_is_not_offered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFileFlux();
        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<IEmbedder>());
        Assert.False(provider.GetRequiredService<IChunkerFactory>().IsStrategyAvailable(ChunkingStrategy.Semantic));
    }

    private sealed class OwnEmbedder : IEmbedder
    {
        private static readonly float[] One = [1f];

        public int EmbeddingDimension => 1;
        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(One);
        public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => One).ToList());
        public float CalculateSimilarity(float[] embedding1, float[] embedding2) => 1f;
    }

    [Fact]
    public void An_IEmbedder_registered_before_AddFileFlux_wins()
    {
        var own = new OwnEmbedder();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEmbedder>(own);
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new CountingGenerator());
        services.AddFileFlux();
        using var provider = services.BuildServiceProvider();

        Assert.Same(own, provider.GetRequiredService<IEmbedder>());
    }
}
