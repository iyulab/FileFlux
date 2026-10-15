using LMSupply;
using LMSupply.Embedder;
using Microsoft.Extensions.AI;

namespace FileFlux.Providers.LMSupply.Services;

/// <summary>
/// A local LMSupply embedding model as a Microsoft.Extensions.AI <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/>
/// that owns the model: disposing it releases the model. Registered by <c>AddLMSupplyEmbedding</c>, it is the embedder of
/// <c>Semantic</c> chunking, and any other consumer of the container's generator can use it.
/// </summary>
/// <remarks>
/// Texts are embedded with the model's convention for text with no retrieval role (<see cref="EmbeddingTextKind.Default"/>),
/// which is right for similarity - what semantic chunking measures. For retrieval with a model that declares query and passage
/// prefixes, wrap the model with <see cref="EmbeddingModelExtensions.AsEmbeddingGenerator"/> once per side.
/// </remarks>
public sealed class LMSupplyEmbedderService : IEmbeddingGenerator<string, Embedding<float>>, IAsyncDisposable
{
    private readonly IEmbeddingModel _model;
    private readonly LocalEmbeddingGenerator _generator;
    private bool _disposed;

    /// <summary>Wraps a loaded model; the service owns it from now on.</summary>
    /// <param name="model">The loaded embedding model.</param>
    public LMSupplyEmbedderService(IEmbeddingModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _generator = model.AsEmbeddingGenerator();
    }

    /// <summary>Loads the configured model (<see cref="LMSupplyOptions.EmbeddingModel"/>) and wraps it.</summary>
    /// <param name="options">Configuration options.</param>
    /// <param name="progress">Optional progress reporting for downloads.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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
                // Auto lets LMSupply pick the provider for this host (CUDA / CoreML / CPU for ONNX sessions,
                // Vulkan for llama-server on AMD/Intel GPUs). This used to pin DirectML, which ONNX Runtime
                // 1.25+ no longer ships -- LMSupply 0.67.0 refuses it.
                ? ExecutionProvider.Auto
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

    /// <summary>Provider <c>"LMSupply"</c>, the model id and its native dimension count.</summary>
    public EmbeddingGeneratorMetadata Metadata => _generator.Metadata;

    /// <inheritdoc />
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _generator.GenerateAsync(values, options, cancellationToken);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : _generator.GetService(serviceType, serviceKey);
    }

    /// <summary>
    /// Disposes synchronously, for a container disposed with <c>Dispose()</c> (which throws on a service that is only
    /// <see cref="IAsyncDisposable"/>). Blocks on <see cref="DisposeAsync"/>.
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

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
