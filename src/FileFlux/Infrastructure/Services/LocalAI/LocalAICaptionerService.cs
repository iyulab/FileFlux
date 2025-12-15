using System.Diagnostics;
using LocalAI;
using LocalAI.Captioner;

namespace FileFlux.Infrastructure.Services.LocalAI;

/// <summary>
/// IImageToTextService implementation using LocalAI.Captioner.
/// Provides image captioning capabilities for visual content description.
/// </summary>
public sealed class LocalAICaptionerService : IImageToTextService, IAsyncDisposable
{
    private readonly ICaptionerModel _model;
    private bool _disposed;

    private static readonly string[] SupportedFormats = ["png", "jpg", "jpeg", "gif", "bmp", "webp"];

    /// <summary>
    /// Creates a new instance of LocalAICaptionerService with the specified model.
    /// </summary>
    /// <param name="model">The loaded captioner model.</param>
    public LocalAICaptionerService(ICaptionerModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    /// <summary>
    /// Creates a new LocalAICaptionerService with the default model.
    /// </summary>
    /// <param name="options">Configuration options.</param>
    /// <param name="progress">Optional progress reporting for downloads.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new LocalAICaptionerService instance.</returns>
    public static async Task<LocalAICaptionerService> CreateAsync(
        LocalAIOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new LocalAIOptions();

        var captionerOptions = new CaptionerOptions
        {
            CacheDirectory = options.CacheDirectory
        };

        var model = await LocalCaptioner.LoadAsync(
            options.CaptionerModel,
            captionerOptions,
            progress,
            cancellationToken).ConfigureAwait(false);

        if (options.WarmupOnInit)
        {
            await model.WarmupAsync(cancellationToken).ConfigureAwait(false);
        }

        return new LocalAICaptionerService(model);
    }

    /// <inheritdoc />
    public IEnumerable<string> SupportedImageFormats => SupportedFormats;

    /// <inheritdoc />
    public string ProviderName => $"LocalAI Captioner ({_model.ModelId})";

    /// <inheritdoc />
    public async Task<ImageToTextResult> ExtractTextAsync(
        byte[] imageData,
        ImageToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(imageData);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await _model.CaptionAsync(imageData, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            return new ImageToTextResult
            {
                ExtractedText = result.Caption,
                ConfidenceScore = NormalizeConfidence(result.Confidence),
                DetectedLanguage = "en",
                ImageType = options?.ImageTypeHint ?? "photo",
                StructuralElements = [],
                Metadata = new ImageMetadata
                {
                    FileSize = imageData.Length,
                    Format = DetectImageFormat(imageData)
                },
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ImageToTextResult
            {
                ExtractedText = string.Empty,
                ConfidenceScore = 0,
                ImageType = "unknown",
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public async Task<ImageToTextResult> ExtractTextAsync(
        Stream imageStream,
        ImageToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(imageStream);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await _model.CaptionAsync(imageStream, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            return new ImageToTextResult
            {
                ExtractedText = result.Caption,
                ConfidenceScore = NormalizeConfidence(result.Confidence),
                DetectedLanguage = "en",
                ImageType = options?.ImageTypeHint ?? "photo",
                StructuralElements = [],
                Metadata = new ImageMetadata(),
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ImageToTextResult
            {
                ExtractedText = string.Empty,
                ConfidenceScore = 0,
                ImageType = "unknown",
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public async Task<ImageToTextResult> ExtractTextAsync(
        string imagePath,
        ImageToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(imagePath);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await _model.CaptionAsync(imagePath, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var fileInfo = new FileInfo(imagePath);

            return new ImageToTextResult
            {
                ExtractedText = result.Caption,
                ConfidenceScore = NormalizeConfidence(result.Confidence),
                DetectedLanguage = "en",
                ImageType = options?.ImageTypeHint ?? "photo",
                StructuralElements = [],
                Metadata = new ImageMetadata
                {
                    FileSize = fileInfo.Exists ? fileInfo.Length : 0,
                    Format = Path.GetExtension(imagePath).TrimStart('.').ToUpperInvariant()
                },
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ImageToTextResult
            {
                ExtractedText = string.Empty,
                ConfidenceScore = 0,
                ImageType = "unknown",
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
        }
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

    /// <summary>
    /// Normalizes the confidence score from log probability to 0-1 range.
    /// </summary>
    private static double NormalizeConfidence(float logProbability)
    {
        // Log probability is typically negative; convert to 0-1 range
        // Using sigmoid-like transformation
        return Math.Max(0, Math.Min(1, (logProbability + 5) / 5));
    }

    /// <summary>
    /// Detects image format from magic bytes.
    /// </summary>
    private static string DetectImageFormat(byte[] imageData)
    {
        if (imageData.Length < 8)
            return "UNKNOWN";

        // PNG
        if (imageData[0] == 0x89 && imageData[1] == 0x50 && imageData[2] == 0x4E && imageData[3] == 0x47)
            return "PNG";

        // JPEG
        if (imageData[0] == 0xFF && imageData[1] == 0xD8 && imageData[2] == 0xFF)
            return "JPEG";

        // GIF
        if (imageData[0] == 0x47 && imageData[1] == 0x49 && imageData[2] == 0x46)
            return "GIF";

        // BMP
        if (imageData[0] == 0x42 && imageData[1] == 0x4D)
            return "BMP";

        // WebP
        if (imageData.Length >= 12 &&
            imageData[0] == 0x52 && imageData[1] == 0x49 && imageData[2] == 0x46 && imageData[3] == 0x46 &&
            imageData[8] == 0x57 && imageData[9] == 0x45 && imageData[10] == 0x42 && imageData[11] == 0x50)
            return "WEBP";

        return "UNKNOWN";
    }
}
