using FluxCurator.Core.Core;

namespace FileFlux.Infrastructure.Adapters;

/// <summary>
/// Presents a FileFlux <see cref="IEmbeddingService"/> as FluxCurator's <see cref="IEmbedder"/>. Semantic chunking runs on
/// FluxCurator, which reads <see cref="IEmbedder"/> from the container; without this bridge an embedding service registered
/// for FileFlux (<c>AddLMSupplyEmbedding</c>, an IronHive adapter, your own) never reached it and <c>Semantic</c> failed for want
/// of an embedder.
/// </summary>
internal sealed class EmbeddingServiceEmbedder(IEmbeddingService service) : IEmbedder
{
    public int EmbeddingDimension => service.EmbeddingDimension;

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
        service.GenerateEmbeddingAsync(text, EmbeddingPurpose.Analysis, cancellationToken);

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        if (service.SupportsBatchProcessing)
            return [.. await service.GenerateBatchEmbeddingsAsync(texts, EmbeddingPurpose.Analysis, cancellationToken).ConfigureAwait(false)];

        var result = new List<float[]>();
        foreach (var text in texts)
            result.Add(await service.GenerateEmbeddingAsync(text, EmbeddingPurpose.Analysis, cancellationToken).ConfigureAwait(false));
        return result;
    }

    public float CalculateSimilarity(float[] embedding1, float[] embedding2) =>
        (float)service.CalculateSimilarity(embedding1, embedding2);
}
