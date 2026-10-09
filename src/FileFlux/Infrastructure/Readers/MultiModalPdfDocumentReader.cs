using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Unpdf;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace FileFlux.Infrastructure.Readers;

/// <summary>
/// Multimodal PDF document reader with integrated image processing.
/// When IImageToTextService is provided, extracts text from images for enrichment.
/// When IImageRelevanceEvaluator is provided, selectively includes relevant images.
/// </summary>
public class MultiModalPdfDocumentReader : IDocumentReader
{
    private static readonly ImageDescriptionFormat s_format = new()
    {
        DocumentType = "PDF",
        ImageTypeHint = "document",
        // PDF descriptions follow the text without a section marker.
        SectionMarker = null,
        Label = (_, number) => string.Create(CultureInfo.InvariantCulture, $"Image {number}:"),
        ResultSubject = (number, imageType) => string.Create(CultureInfo.InvariantCulture, $"Image {number}: {imageType}"),
    };

    private readonly IImageToTextService? _imageToTextService;
    private readonly IImageRelevanceEvaluator? _relevanceEvaluator;
    private readonly PdfDocumentReader _basePdfReader;

    public string ReaderType => "MultiModalPdfReader";

    public IEnumerable<string> SupportedExtensions => new[] { ".pdf" };

    public MultiModalPdfDocumentReader(IServiceProvider serviceProvider)
    {
        // IImageToTextService is an optional dependency
        _imageToTextService = serviceProvider.GetService<IImageToTextService>();
        // IImageRelevanceEvaluator is an optional dependency
        _relevanceEvaluator = serviceProvider.GetService<IImageRelevanceEvaluator>();
        _basePdfReader = new PdfDocumentReader();
    }

    public bool CanRead(string fileName)
    {
        return _basePdfReader.CanRead(fileName);
    }

    // ========================================
    // Stage 0: Read (Document Structure)
    // ========================================

    public Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return _basePdfReader.ReadAsync(filePath, cancellationToken);
    }

    public Task<ReadResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        return _basePdfReader.ReadAsync(stream, fileName, cancellationToken);
    }

    // ========================================
    // Stage 1: Extract (Raw Content)
    // ========================================

    /// <summary>
    /// The PDF's text, with the pages <see cref="ExtractOptions.PageReading"/> selects read, and a description of each
    /// image the base reader extracted appended, when an <see cref="IImageToTextService"/> is registered. The extraction
    /// options (<see cref="ExtractOptions.ExtractImages"/>, <see cref="ExtractOptions.MaxImageSize"/>) decide which images
    /// there are; an image on a page replaced by its read is not described again. The stream path does the same.
    /// </summary>
    public async Task<RawContent> ExtractAsync(string filePath, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        var baseContent = await _basePdfReader.ExtractAsync(filePath, options, cancellationToken).ConfigureAwait(false);

        if (options?.PageReading is { SelectPages: not null } pageReading)
        {
            using var doc = UnpdfDocument.ParseFile(filePath);
            await ReadPagesAsync(doc, baseContent, pageReading, cancellationToken).ConfigureAwait(false);
        }

        return await DescribeImagesAsync(baseContent, Path.GetFileNameWithoutExtension(filePath), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="ExtractAsync(string, ExtractOptions?, CancellationToken)"/>
    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        RawContent baseContent;
        if (options?.PageReading is { SelectPages: not null } pageReading)
        {
            // Page reading renders from the same bytes the text was extracted from.
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            buffer.Position = 0;
            baseContent = await _basePdfReader.ExtractAsync(buffer, fileName, options, cancellationToken).ConfigureAwait(false);
            using var doc = UnpdfDocument.ParseBytes(buffer.ToArray());
            await ReadPagesAsync(doc, baseContent, pageReading, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            baseContent = await _basePdfReader.ExtractAsync(stream, fileName, options, cancellationToken).ConfigureAwait(false);
        }

        return await DescribeImagesAsync(baseContent, Path.GetFileNameWithoutExtension(fileName), cancellationToken).ConfigureAwait(false);
    }

    private Task<RawContent> DescribeImagesAsync(RawContent baseContent, string title, CancellationToken cancellationToken) =>
        ImageDescriptions.AppendAsync(baseContent, title, ReaderType, s_format, _imageToTextService, _relevanceEvaluator, cancellationToken);

    /// <summary>
    /// <see cref="ExtractOptions.PageReading"/>: renders the selected pages of <paramref name="doc"/> and reads them through
    /// the registered <see cref="IImageToTextService"/>. Without one, the content says so in its warnings.
    /// </summary>
    private async Task ReadPagesAsync(UnpdfDocument doc, RawContent content, PageReadingOptions options, CancellationToken cancellationToken)
    {
        if (_imageToTextService is null)
        {
            content.Warnings.Add("Page reading was requested but no IImageToTextService is registered; no page was rendered");
            return;
        }

        await PageVisionReading.ApplyAsync(content, options, (page, dpi) =>
        {
            var rendered = doc.RenderPage(page, new RenderPageOptions { Dpi = dpi });
            var gaps = rendered.Gaps;
            return new RenderedPageImage(
                rendered.Png,
                Saturate(gaps.TextRuns),
                Saturate((long)gaps.Images + gaps.InlineImages),
                Saturate(gaps.UndecodableContentStreams));
        }, _imageToTextService, cancellationToken).ConfigureAwait(false);

        static int Saturate(long value) => value > int.MaxValue ? int.MaxValue : (int)value;
    }
}
