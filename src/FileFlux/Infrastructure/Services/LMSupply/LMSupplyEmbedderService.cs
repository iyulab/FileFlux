using LMSupply;
using LMSupply.Embedder;

namespace FileFlux.Infrastructure.Services.LMSupply;

/// <summary>
/// IEmbeddingService implementation using LMSupply.Embedder.
/// </summary>
public sealed class LMSupplyEmbedderService : IEmbeddingService, IAsyncDisposable
{
    private readonly IEmbeddingModel _model;
    private bool _disposed;

    /// <summary>
    /// Creates a new instance of LMSupplyEmbedderService with the specified model.
    /// </summary>
    /// <param name="model">The loaded embedding model.</param>
    public LMSupplyEmbedderService(IEmbeddingModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    /// <summary>
    /// Creates a new LMSupplyEmbedderService with the default model.
    /// </summary>
    /// <param name="options">Configuration options.</param>
    /// <param name="progress">Optional progress reporting for downloads.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new LMSupplyEmbedderService instance.</returns>
    public static async Task<LMSupplyEmbedderService> CreateAsync(
        LMSupplyOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new LMSupplyOptions();

        var embedderOptions = new EmbedderOptions
        {
            CacheDirectory = options.CacheDirectory,
            MaxSequenceLength = options.MaxSequenceLength,
            Provider = options.UseGpuAcceleration
                ? ExecutionProvider.DirectML
                : ExecutionProvider.Cpu
        };

        var model = await global::LMSupply.Embedder.LocalEmbedder.LoadAsync(
            options.EmbeddingModel,
            embedderOptions,
            progress,
            cancellationToken).ConfigureAwait(false);

        if (options.WarmupOnInit)
        {
            await model.WarmupAsync(cancellationToken).ConfigureAwait(false);
        }

        return new LMSupplyEmbedderService(model);
    }

    /// <inheritdoc />
    public int EmbeddingDimension => _model.Dimensions;

    /// <inheritdoc />
    public int MaxTokens => 8192;

    /// <inheritdoc />
    public bool SupportsBatchProcessing => true;

    /// <inheritdoc />
    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        EmbeddingPurpose purpose = EmbeddingPurpose.Analysis,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(text);

        return await _model.EmbedAsync(text, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<float[]>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts,
        EmbeddingPurpose purpose = EmbeddingPurpose.Analysis,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(texts);

        var textList = texts as IReadOnlyList<string> ?? texts.ToList();
        if (textList.Count == 0)
        {
            return [];
        }

        var results = await _model.EmbedAsync(textList, cancellationToken).ConfigureAwait(false);
        return results;
    }

    /// <inheritdoc />
    public double CalculateSimilarity(float[] embedding1, float[] embedding2)
    {
        ArgumentNullException.ThrowIfNull(embedding1);
        ArgumentNullException.ThrowIfNull(embedding2);

        return global::LMSupply.Embedder.LocalEmbedder.CosineSimilarity(embedding1, embedding2);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _model.DisposeAsync().ConfigureAwait(false);
    }
}
