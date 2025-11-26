using LocalEmbedder;
using LocalEmbedder.Download;
using Microsoft.Extensions.Logging;

namespace FileFlux.Infrastructure.Services;

/// <summary>
/// Production-ready embedding service using LocalEmbedder for high-quality local embeddings.
/// Supports automatic model downloading from HuggingFace and GPU acceleration.
/// </summary>
public sealed class LocalEmbedderService : IEmbeddingService, IAsyncDisposable
{
    private readonly ILogger<LocalEmbedderService>? _logger;
    private readonly LocalEmbedderOptions _options;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private IEmbeddingModel? _analysisModel;
    private IEmbeddingModel? _searchModel;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of LocalEmbedderService.
    /// </summary>
    /// <param name="options">Configuration options for the embedder</param>
    /// <param name="logger">Optional logger</param>
    public LocalEmbedderService(
        LocalEmbedderOptions? options = null,
        ILogger<LocalEmbedderService>? logger = null)
    {
        _options = options ?? new LocalEmbedderOptions();
        _logger = logger;
    }

    public int EmbeddingDimension => _options.PrimaryDimension;
    public int MaxTokens => _options.MaxSequenceLength;
    public bool SupportsBatchProcessing => true;

    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        EmbeddingPurpose purpose = EmbeddingPurpose.Analysis,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(text))
        {
            return new float[EmbeddingDimension];
        }

        var model = await GetModelForPurposeAsync(purpose, cancellationToken).ConfigureAwait(false);
        return await model.EmbedAsync(text, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<float[]>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts,
        EmbeddingPurpose purpose = EmbeddingPurpose.Analysis,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var textList = texts as IReadOnlyList<string> ?? texts.ToList();

        if (textList.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        var model = await GetModelForPurposeAsync(purpose, cancellationToken).ConfigureAwait(false);
        var embeddings = await model.EmbedAsync(textList, cancellationToken).ConfigureAwait(false);

        return embeddings;
    }

    public double CalculateSimilarity(float[] embedding1, float[] embedding2)
    {
        if (embedding1.Length != embedding2.Length)
        {
            throw new ArgumentException("Embeddings must have the same dimension");
        }

        return LocalEmbedder.LocalEmbedder.CosineSimilarity(embedding1, embedding2);
    }

    private async Task<IEmbeddingModel> GetModelForPurposeAsync(
        EmbeddingPurpose purpose,
        CancellationToken cancellationToken)
    {
        return purpose switch
        {
            EmbeddingPurpose.Analysis => await GetAnalysisModelAsync(cancellationToken).ConfigureAwait(false),
            EmbeddingPurpose.SemanticSearch => await GetSearchModelAsync(cancellationToken).ConfigureAwait(false),
            EmbeddingPurpose.Storage => await GetSearchModelAsync(cancellationToken).ConfigureAwait(false),
            _ => await GetAnalysisModelAsync(cancellationToken).ConfigureAwait(false)
        };
    }

    private async Task<IEmbeddingModel> GetAnalysisModelAsync(CancellationToken cancellationToken)
    {
        if (_analysisModel != null)
        {
            return _analysisModel;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_analysisModel != null)
            {
                return _analysisModel;
            }

            _logger?.LogInformation(
                "Loading analysis model: {Model} (first use, will auto-download if needed)",
                _options.AnalysisModel);

            var embedderOptions = new EmbedderOptions
            {
                CacheDirectory = _options.CacheDirectory,
                MaxSequenceLength = _options.MaxSequenceLength,
                NormalizeEmbeddings = _options.NormalizeEmbeddings,
                Provider = _options.Provider
            };

            var progress = _options.ShowProgress ? CreateProgressReporter("Analysis model") : null;

            _analysisModel = await LocalEmbedder.LocalEmbedder.LoadAsync(
                _options.AnalysisModel,
                embedderOptions,
                progress).ConfigureAwait(false);

            _logger?.LogInformation(
                "Analysis model loaded successfully: {Model} ({Dimensions} dimensions)",
                _analysisModel.ModelId,
                _analysisModel.Dimensions);

            return _analysisModel;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<IEmbeddingModel> GetSearchModelAsync(CancellationToken cancellationToken)
    {
        if (_searchModel != null)
        {
            return _searchModel;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_searchModel != null)
            {
                return _searchModel;
            }

            _logger?.LogInformation(
                "Loading search model: {Model} (first use, will auto-download if needed)",
                _options.SearchModel);

            var embedderOptions = new EmbedderOptions
            {
                CacheDirectory = _options.CacheDirectory,
                MaxSequenceLength = _options.MaxSequenceLength,
                NormalizeEmbeddings = _options.NormalizeEmbeddings,
                Provider = _options.Provider
            };

            var progress = _options.ShowProgress ? CreateProgressReporter("Search model") : null;

            _searchModel = await LocalEmbedder.LocalEmbedder.LoadAsync(
                _options.SearchModel,
                embedderOptions,
                progress).ConfigureAwait(false);

            _logger?.LogInformation(
                "Search model loaded successfully: {Model} ({Dimensions} dimensions)",
                _searchModel.ModelId,
                _searchModel.Dimensions);

            return _searchModel;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private IProgress<DownloadProgress>? CreateProgressReporter(string modelType)
    {
        if (_logger == null)
        {
            return null;
        }

        return new Progress<DownloadProgress>(progress =>
        {
            if (progress.PercentComplete % 10 == 0 || progress.PercentComplete >= 99)
            {
                _logger.LogInformation(
                    "{ModelType} download: {FileName} - {Percent:F1}% ({Downloaded:F1} MB / {Total:F1} MB)",
                    modelType,
                    progress.FileName,
                    progress.PercentComplete,
                    progress.BytesDownloaded / (1024.0 * 1024.0),
                    progress.TotalBytes / (1024.0 * 1024.0));
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _analysisModel?.Dispose();
        _searchModel?.Dispose();
        _initLock.Dispose();

        await Task.CompletedTask;
    }
}

/// <summary>
/// Configuration options for LocalEmbedderService.
/// </summary>
public sealed class LocalEmbedderOptions
{
    /// <summary>
    /// Model to use for analysis during chunking (lightweight, fast).
    /// Default: all-MiniLM-L6-v2 (384 dimensions)
    /// </summary>
    public string AnalysisModel { get; set; } = "all-MiniLM-L6-v2";

    /// <summary>
    /// Model to use for semantic search and storage (high quality).
    /// Default: all-mpnet-base-v2 (768 dimensions)
    /// </summary>
    public string SearchModel { get; set; } = "all-mpnet-base-v2";

    /// <summary>
    /// Primary embedding dimension (should match AnalysisModel dimension).
    /// Default: 384 (all-MiniLM-L6-v2)
    /// </summary>
    public int PrimaryDimension { get; set; } = 384;

    /// <summary>
    /// Cache directory for downloaded models.
    /// Default: ~/.cache/huggingface/hub
    /// </summary>
    public string? CacheDirectory { get; set; }

    /// <summary>
    /// Maximum sequence length for tokenization.
    /// Default: 512
    /// </summary>
    public int MaxSequenceLength { get; set; } = 512;

    /// <summary>
    /// Whether to normalize embeddings to unit vectors.
    /// Default: true
    /// </summary>
    public bool NormalizeEmbeddings { get; set; } = true;

    /// <summary>
    /// Execution provider for ONNX Runtime.
    /// Default: Cpu
    /// </summary>
    public ExecutionProvider Provider { get; set; } = ExecutionProvider.Cpu;

    /// <summary>
    /// Whether to show download progress in logs.
    /// Default: true
    /// </summary>
    public bool ShowProgress { get; set; } = true;
}
