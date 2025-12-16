using System.Diagnostics;
using LocalAI;
using LocalAI.Ocr;

namespace FileFlux.Infrastructure.Services.LocalAI;

/// <summary>
/// IImageToTextService implementation using LocalAI.Ocr.
/// Provides optical character recognition for document and text images.
/// </summary>
public sealed class LocalAIOcrService : IImageToTextService, IAsyncDisposable
{
    private readonly IOcr _ocr;
    private bool _disposed;

    private static readonly string[] SupportedFormats = ["png", "jpg", "jpeg", "gif", "bmp", "tiff", "webp"];

    /// <summary>
    /// Creates a new instance of LocalAIOcrService with the specified OCR pipeline.
    /// </summary>
    /// <param name="ocr">The loaded OCR pipeline.</param>
    public LocalAIOcrService(IOcr ocr)
    {
        _ocr = ocr ?? throw new ArgumentNullException(nameof(ocr));
    }

    /// <summary>
    /// Creates a new LocalAIOcrService with the default models.
    /// </summary>
    /// <param name="options">Configuration options.</param>
    /// <param name="progress">Optional progress reporting for downloads.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new LocalAIOcrService instance.</returns>
    public static async Task<LocalAIOcrService> CreateAsync(
        LocalAIOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new LocalAIOptions();

        var ocrOptions = new OcrOptions
        {
            CacheDirectory = options.CacheDirectory,
            LanguageHint = options.OcrLanguageHint
        };

        var ocr = await LocalOcr.LoadAsync(
            options.OcrDetectionModel,
            options.OcrRecognitionModel,
            ocrOptions,
            progress,
            cancellationToken).ConfigureAwait(false);

        if (options.WarmupOnInit)
        {
            await ocr.WarmupAsync(cancellationToken).ConfigureAwait(false);
        }

        return new LocalAIOcrService(ocr);
    }

    /// <summary>
    /// Creates a new LocalAIOcrService for a specific language.
    /// </summary>
    /// <param name="languageCode">ISO language code (e.g., "en", "ko", "zh", "ja").</param>
    /// <param name="options">Optional configuration options.</param>
    /// <param name="progress">Optional progress reporting for downloads.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new LocalAIOcrService instance.</returns>
    public static async Task<LocalAIOcrService> CreateForLanguageAsync(
        string languageCode,
        LocalAIOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new LocalAIOptions();

        var ocrOptions = new OcrOptions
        {
            CacheDirectory = options.CacheDirectory,
            LanguageHint = languageCode
        };

        var ocr = await LocalOcr.LoadForLanguageAsync(
            languageCode,
            ocrOptions,
            progress,
            cancellationToken).ConfigureAwait(false);

        if (options.WarmupOnInit)
        {
            await ocr.WarmupAsync(cancellationToken).ConfigureAwait(false);
        }

        return new LocalAIOcrService(ocr);
    }

    /// <inheritdoc />
    public IEnumerable<string> SupportedImageFormats => SupportedFormats;

    /// <inheritdoc />
    public string ProviderName => $"LocalAI OCR ({_ocr.DetectionModelId}/{_ocr.RecognitionModelId})";

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
            var result = await _ocr.RecognizeAsync(imageData, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            return CreateResult(result, stopwatch.ElapsedMilliseconds, options, imageData.Length);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return CreateErrorResult(stopwatch.ElapsedMilliseconds, ex.Message);
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
            var result = await _ocr.RecognizeAsync(imageStream, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            return CreateResult(result, stopwatch.ElapsedMilliseconds, options, 0);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return CreateErrorResult(stopwatch.ElapsedMilliseconds, ex.Message);
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
            var result = await _ocr.RecognizeAsync(imagePath, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var fileInfo = new FileInfo(imagePath);
            var fileSize = fileInfo.Exists ? fileInfo.Length : 0;
            var format = Path.GetExtension(imagePath).TrimStart('.').ToUpperInvariant();

            return CreateResult(result, stopwatch.ElapsedMilliseconds, options, fileSize, format);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return CreateErrorResult(stopwatch.ElapsedMilliseconds, ex.Message);
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
        await _ocr.DisposeAsync().ConfigureAwait(false);
    }

    private ImageToTextResult CreateResult(
        OcrResult ocrResult,
        long processingTimeMs,
        ImageToTextOptions? options,
        long fileSize,
        string? format = null)
    {
        var structuralElements = ocrResult.Regions.Select(region => new StructuralElement
        {
            Type = "text",
            Content = region.Text,
            BoundingBox = new FileFlux.BoundingBox
            {
                X = region.BoundingBox.X,
                Y = region.BoundingBox.Y,
                Width = region.BoundingBox.Width,
                Height = region.BoundingBox.Height
            },
            Confidence = region.Confidence
        }).ToList();

        var avgConfidence = ocrResult.Regions.Count > 0
            ? ocrResult.Regions.Average(r => r.Confidence)
            : 0;

        return new ImageToTextResult
        {
            ExtractedText = options?.ExtractStructure == true
                ? ocrResult.GetTextWithLayout()
                : ocrResult.FullText,
            ConfidenceScore = avgConfidence,
            DetectedLanguage = LanguageDetector.Detect(ocrResult.FullText).Language,
            ImageType = options?.ImageTypeHint ?? "document",
            StructuralElements = structuralElements,
            Metadata = new ImageMetadata
            {
                FileSize = fileSize,
                Format = format ?? "UNKNOWN"
            },
            ProcessingTimeMs = processingTimeMs
        };
    }

    private static ImageToTextResult CreateErrorResult(long processingTimeMs, string errorMessage)
    {
        return new ImageToTextResult
        {
            ExtractedText = string.Empty,
            ConfidenceScore = 0,
            ImageType = "unknown",
            ProcessingTimeMs = processingTimeMs,
            ErrorMessage = errorMessage
        };
    }

}
