using FileFlux.Infrastructure;
using FluxCurator.Core.Core;
using FluxCurator.Core.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests;

/// <summary>
/// Semantic chunking runs on FluxCurator, which reads its own <see cref="IEmbedder"/>. An embedding service registered for
/// FileFlux (<c>AddLMSupplyEmbedding</c>, an adapter, a consumer's own) now reaches it; before, <c>Semantic</c> failed for want
/// of an embedder even with one registered.
/// </summary>
public sealed class SemanticChunkingEmbedderBridgeTests
{
    private sealed class CountingEmbeddingService : IEmbeddingService
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);
        public int EmbeddingDimension => 2;
        public int MaxTokens => 512;
        public bool SupportsBatchProcessing => false;

        public Task<float[]> GenerateEmbeddingAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Analysis, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            float[] vector = text.Contains("revenue", StringComparison.OrdinalIgnoreCase) ? [0f, 1f] : [1f, 0f];
            return Task.FromResult(vector);
        }

        public async Task<IEnumerable<float[]>> GenerateBatchEmbeddingsAsync(IEnumerable<string> texts, EmbeddingPurpose purpose = EmbeddingPurpose.Analysis, CancellationToken cancellationToken = default)
        {
            var result = new List<float[]>();
            foreach (var text in texts)
                result.Add(await GenerateEmbeddingAsync(text, purpose, cancellationToken));
            return result;
        }

        public double CalculateSimilarity(float[] embedding1, float[] embedding2)
        {
            double dot = 0, n1 = 0, n2 = 0;
            for (var i = 0; i < embedding1.Length; i++)
            {
                dot += embedding1[i] * embedding2[i];
                n1 += embedding1[i] * embedding1[i];
                n2 += embedding2[i] * embedding2[i];
            }

            return dot / Math.Sqrt(n1 * n2);
        }
    }

    [Fact]
    public async Task A_registered_embedding_service_drives_semantic_chunking()
    {
        var embedding = new CountingEmbeddingService();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEmbeddingService>(embedding);
        services.AddFileFlux();
        using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IChunkerFactory>();
        Assert.True(factory.IsStrategyAvailable(ChunkingStrategy.Semantic));

        var chunks = await factory.CreateChunker(ChunkingStrategy.Semantic).ChunkAsync(
            "Cats purr and sleep. Kittens are young cats.\n\nQuarterly revenue grew. The revenue outlook is strong.",
            new ChunkOptions(),
            TestContext.Current.CancellationToken);

        Assert.NotEmpty(chunks);
        Assert.True(embedding.Calls > 0, "the registered FileFlux embedding service is the one semantic chunking uses");
    }

    [Fact]
    public void Without_an_embedding_service_semantic_is_not_offered()
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
        services.AddSingleton<IEmbeddingService>(new CountingEmbeddingService());
        services.AddFileFlux();
        using var provider = services.BuildServiceProvider();

        Assert.Same(own, provider.GetRequiredService<IEmbedder>());
    }
}
