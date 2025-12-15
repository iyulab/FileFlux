using LocalAI;

namespace FileFlux.Infrastructure.Services.LocalAI;

/// <summary>
/// Factory for creating and caching LocalAI service instances.
/// Services are created lazily on first access because they require async model loading.
/// </summary>
public sealed class LocalAIServiceFactory : IAsyncDisposable
{
    private readonly LocalAIOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private LocalAIEmbedderService? _embedder;
    private LocalAIGeneratorService? _generator;
    private LocalAICaptionerService? _captioner;
    private LocalAIOcrService? _ocr;
    private bool _disposed;

    /// <summary>
    /// Creates a new LocalAIServiceFactory with the specified options.
    /// </summary>
    /// <param name="options">Configuration options for LocalAI services.</param>
    public LocalAIServiceFactory(LocalAIOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Gets or creates the embedding service.
    /// </summary>
    /// <param name="progress">Optional progress reporting for model download.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The embedding service instance.</returns>
    public async Task<LocalAIEmbedderService> GetEmbedderAsync(
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_embedder != null)
            return _embedder;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _embedder ??= await LocalAIEmbedderService.CreateAsync(_options, progress, cancellationToken)
                .ConfigureAwait(false);
            return _embedder;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets or creates the text generation service.
    /// </summary>
    /// <param name="progress">Optional progress reporting for model download.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The text generation service instance.</returns>
    public async Task<LocalAIGeneratorService> GetGeneratorAsync(
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_generator != null)
            return _generator;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _generator ??= await LocalAIGeneratorService.CreateAsync(_options, progress, cancellationToken)
                .ConfigureAwait(false);
            return _generator;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets or creates the image captioning service.
    /// </summary>
    /// <param name="progress">Optional progress reporting for model download.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The image captioning service instance.</returns>
    public async Task<LocalAICaptionerService> GetCaptionerAsync(
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_captioner != null)
            return _captioner;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _captioner ??= await LocalAICaptionerService.CreateAsync(_options, progress, cancellationToken)
                .ConfigureAwait(false);
            return _captioner;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets or creates the OCR service.
    /// </summary>
    /// <param name="progress">Optional progress reporting for model download.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The OCR service instance.</returns>
    public async Task<LocalAIOcrService> GetOcrAsync(
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_ocr != null)
            return _ocr;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _ocr ??= await LocalAIOcrService.CreateAsync(_options, progress, cancellationToken)
                .ConfigureAwait(false);
            return _ocr;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets or creates an OCR service for a specific language.
    /// </summary>
    /// <param name="languageCode">ISO language code (e.g., "en", "ko", "zh", "ja").</param>
    /// <param name="progress">Optional progress reporting for model download.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The OCR service instance for the specified language.</returns>
    public async Task<LocalAIOcrService> GetOcrForLanguageAsync(
        string languageCode,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(languageCode);

        // For language-specific OCR, we don't cache as users may request different languages
        return await LocalAIOcrService.CreateForLanguageAsync(languageCode, _options, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Dispose all created services
        var tasks = new List<Task>();

        if (_embedder != null)
            tasks.Add(_embedder.DisposeAsync().AsTask());

        if (_generator != null)
            tasks.Add(_generator.DisposeAsync().AsTask());

        if (_captioner != null)
            tasks.Add(_captioner.DisposeAsync().AsTask());

        if (_ocr != null)
            tasks.Add(_ocr.DisposeAsync().AsTask());

        await Task.WhenAll(tasks).ConfigureAwait(false);

        _lock.Dispose();
    }
}
