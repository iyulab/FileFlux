using LMSupply.Captioner;
using LMSupply.Ocr;
using Microsoft.Extensions.Logging;

namespace FileFlux.Infrastructure.Services;

/// <summary>
/// Adapts LMSupply.Ocr and LMSupply.Captioner to FileFlux's IImageToTextService.
/// OCR extracts text from images/scanned documents.
/// Captioner generates descriptions for images (optional, enriches search).
/// </summary>
public sealed partial class LMSupplyImageToTextService : IImageToTextService, IAsyncDisposable
{
    private readonly IOcr _ocr;
    private readonly ICaptionerModel? _captioner;
    private readonly ILogger<LMSupplyImageToTextService> _logger;

    private static readonly HashSet<string> SupportedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".tif", ".gif", ".webp"
    };

    /// <summary>
    /// Creates a new LMSupply-based image-to-text service.
    /// </summary>
    /// <param name="ocr">LMSupply OCR engine for text extraction</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="captioner">Optional captioner for image description generation</param>
    public LMSupplyImageToTextService(
        IOcr ocr,
        ILogger<LMSupplyImageToTextService> logger,
        ICaptionerModel? captioner = null)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        ArgumentNullException.ThrowIfNull(logger);

        _ocr = ocr;
        _logger = logger;
        _captioner = captioner;
    }

    /// <inheritdoc />
    public IEnumerable<string> SupportedImageFormats => SupportedFormats;

    /// <inheritdoc />
    public string ProviderName => _captioner is not null ? "LMSupply.Ocr+Captioner" : "LMSupply.Ocr";

    /// <inheritdoc />
    public async Task<ImageToTextResult> ExtractTextAsync(
        byte[] imageData,
        ImageToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Run OCR to extract text
            var ocrResult = await _ocr.RecognizeAsync(imageData, cancellationToken);
            var ocrText = ocrResult.FullText;

            LogOcrCompleted(_logger, ocrResult.Regions.Count, ocrText.Length);

            // Optionally generate caption
            string? caption = null;
            if (_captioner is not null)
            {
                try
                {
                    var captionResult = await _captioner.CaptionAsync(imageData, cancellationToken);
                    caption = captionResult.Caption;
                    LogCaptionGenerated(_logger, caption);
                }
                catch (Exception ex)
                {
                    LogCaptionFailed(_logger, ex);
                }
            }

            // Combine: caption as context prefix + OCR text
            var extractedText = BuildExtractedText(caption, ocrText);

            sw.Stop();
            return new ImageToTextResult
            {
                ExtractedText = extractedText,
                ConfidenceScore = ocrResult.Regions.Count > 0
                    ? ocrResult.Regions.Average(r => r.Confidence)
                    : 0.0,
                DetectedLanguage = options?.Language ?? "auto",
                ImageType = "document",
                ProcessingTimeMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogExtractionFailed(_logger, ex);
            return new ImageToTextResult
            {
                ErrorMessage = ex.Message,
                ProcessingTimeMs = sw.ElapsedMilliseconds
            };
        }
    }

    /// <inheritdoc />
    public async Task<ImageToTextResult> ExtractTextAsync(
        Stream imageStream,
        ImageToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await imageStream.CopyToAsync(ms, cancellationToken);
        return await ExtractTextAsync(ms.ToArray(), options, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ImageToTextResult> ExtractTextAsync(
        string imagePath,
        ImageToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var imageData = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        return await ExtractTextAsync(imageData, options, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_ocr is IAsyncDisposable ocrDisposable)
            await ocrDisposable.DisposeAsync();

        if (_captioner is IAsyncDisposable captionerDisposable)
            await captionerDisposable.DisposeAsync();
    }

    internal static string BuildExtractedText(string? caption, string ocrText)
    {
        if (string.IsNullOrWhiteSpace(caption) && string.IsNullOrWhiteSpace(ocrText))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(caption))
            return ocrText;

        if (string.IsNullOrWhiteSpace(ocrText))
            return $"[Image: {caption}]";

        return $"[Image: {caption}]\n\n{ocrText}";
    }

    #region LoggerMessage

    [LoggerMessage(Level = LogLevel.Information, Message = "OCR completed: {RegionCount} regions, {TextLength} chars")]
    private static partial void LogOcrCompleted(ILogger logger, int regionCount, int textLength);

    [LoggerMessage(Level = LogLevel.Information, Message = "Caption generated: '{Caption}'")]
    private static partial void LogCaptionGenerated(ILogger logger, string caption);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Caption generation failed, continuing with OCR only")]
    private static partial void LogCaptionFailed(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Image text extraction failed")]
    private static partial void LogExtractionFailed(ILogger logger, Exception? exception);

    #endregion
}
