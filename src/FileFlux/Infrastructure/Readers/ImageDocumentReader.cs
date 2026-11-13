using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;

namespace FileFlux.Infrastructure.Readers;

/// <summary>
/// Reads a picture file (a photographed receipt, a scanned page saved as an image) through a registered
/// <see cref="IImageToTextService"/>: what the service returns as text is the document. It claims only the picture
/// extensions the service says it reads, and none without a service — a consumer with no image service keeps "no reader"
/// for pictures rather than getting an empty document.
/// </summary>
/// <remarks>
/// A read that found no text is not an empty success: the extraction carries
/// <see cref="EmptyExtraction.HintKey"/> = <see cref="NoTextInImage"/>. A service that answers with an error message is a
/// failed read (<see cref="ProcessingStatus.Failed"/>, <see cref="ReadFailed"/>) unless the caller's token was cancelled, which is
/// a cancellation; a service that throws propagates, as the audio reader's does. The service is the single registered <see cref="IImageToTextService"/>, so a consumer that
/// wants OCR text and a caption composes them in its own service — the port has one text result.
/// </remarks>
public sealed class ImageDocumentReader : IDocumentReader
{
    /// <summary><see cref="EmptyExtraction.HintKey"/> value: the service read the picture and found no text.</summary>
    public const string NoTextInImage = "no_text_in_image";

    /// <summary><see cref="EmptyExtraction.HintKey"/> value and error code: the service could not read the picture.</summary>
    public const string ReadFailed = "image_read_failed";

    private static readonly string[] s_pictureExtensions = [".png", ".jpg", ".jpeg", ".webp", ".tif", ".tiff", ".bmp", ".gif"];

    private readonly IImageToTextService? _imageToText;

    /// <summary>Creates the reader over <paramref name="imageToText"/> (null: the reader claims no file).</summary>
    public ImageDocumentReader(IImageToTextService? imageToText) => _imageToText = imageToText;

    /// <summary>
    /// The picture extensions FileFlux knows that the service reads. Services spell their lists with or without a leading
    /// dot and in any case (the LMSupply OCR service answers <c>"png"</c>), so both sides are normalised; a format the
    /// service does not list (the LMSupply OCR decoder does not read TIFF) is not claimed.
    /// </summary>
    public IEnumerable<string> SupportedExtensions
    {
        get
        {
            if (_imageToText is null)
                return [];
            var formats = _imageToText.SupportedImageFormats
                .Select(f => f.Trim().ToLowerInvariant())
                .Where(f => f.Length > 0)
                .Select(f => f.StartsWith('.') ? f : "." + f)
                .ToHashSet(StringComparer.Ordinal);
            return s_pictureExtensions.Where(formats.Contains).ToList();
        }
    }

    /// <inheritdoc/>
    public string ReaderType => "ImageReader";

    /// <inheritdoc/>
    public bool CanRead(string fileName) =>
        SupportedExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

    /// <inheritdoc/>
    public Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists)
            throw new FileNotFoundException($"Image file not found: {filePath}");
        return Task.FromResult(new ReadResult { File = FileInfoOf(info), ReaderType = ReaderType });
    }

    /// <inheritdoc/>
    public Task<ReadResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Task.FromResult(new ReadResult { File = StreamInfoOf(stream, fileName), ReaderType = ReaderType });
    }

    /// <inheritdoc/>
    public async Task<RawContent> ExtractAsync(string filePath, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists)
            throw new FileNotFoundException($"Image file not found: {filePath}");
        var result = await Service().ExtractTextAsync(filePath, ReadOptions(), cancellationToken).ConfigureAwait(false);
        ImageToTextCancellation.ThrowIfCancelledBehindError(result, cancellationToken);
        return ToRawContent(result, FileInfoOf(info));
    }

    /// <inheritdoc/>
    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var file = StreamInfoOf(stream, fileName);
        var result = await Service().ExtractTextAsync(stream, ReadOptions(), cancellationToken).ConfigureAwait(false);
        ImageToTextCancellation.ThrowIfCancelledBehindError(result, cancellationToken);
        return ToRawContent(result, file);
    }

    private IImageToTextService Service() => _imageToText
        ?? throw new InvalidOperationException("No IImageToTextService is registered; picture files are not supported.");

    // The same hint the page reader gives for a scanned page: a picture handed to this reader is a document image.
    private static ImageToTextOptions ReadOptions() => new() { ImageTypeHint = "document", ExtractStructure = true };

    internal RawContent ToRawContent(ImageToTextResult result, SourceFileInfo file)
    {
        var hints = new Dictionary<string, object> { ["image_provider"] = _imageToText?.ProviderName ?? "" };
        var content = new RawContent { File = file, Hints = hints, ReaderType = ReaderType, Status = ProcessingStatus.Completed };

        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            content.Status = ProcessingStatus.Failed;
            content.Errors.Add(new ProcessingError { Code = ReadFailed, Message = result.ErrorMessage, Stage = "extract" });
            content.Warnings.Add($"The picture could not be read: {result.ErrorMessage} [{ReadFailed}]");
            hints[EmptyExtraction.HintKey] = ReadFailed;
            return content;
        }

        var text = (result.ExtractedText ?? string.Empty).Trim();
        content.Text = text;
        if (text.Length > 0)
        {
            content.Spans = [new SourceSpan(0, text.Length)];
        }
        else
        {
            hints[EmptyExtraction.HintKey] = NoTextInImage;
            content.Warnings.Add($"The picture was read but holds no text (0 characters). [{NoTextInImage}]");
        }

        if (result.Metadata is { Width: > 0, Height: > 0 } metadata)
        {
            hints["image_width"] = metadata.Width;
            hints["image_height"] = metadata.Height;
            if (metadata.Format.Length > 0)
                hints["image_format"] = metadata.Format;
        }
        // No language hint: the service's DetectedLanguage is a statistical guess over a few OCR'd lines (the LMSupply OCR
        // service answered "pt" for an English receipt line), and a wrong label is worse than none.
        if (result.ConfidenceScore > 0)
            hints["ocr_confidence"] = result.ConfidenceScore;

        return content;
    }

    private static SourceFileInfo FileInfoOf(FileInfo info) => new()
    {
        Name = info.Name,
        Extension = info.Extension,
        Size = info.Length,
        CreatedAt = info.CreationTimeUtc,
        ModifiedAt = info.LastWriteTimeUtc,
    };

    private static SourceFileInfo StreamInfoOf(Stream stream, string fileName) => new()
    {
        Name = fileName,
        Extension = Path.GetExtension(fileName),
        Size = stream.CanSeek ? stream.Length : 0,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
    };
}
