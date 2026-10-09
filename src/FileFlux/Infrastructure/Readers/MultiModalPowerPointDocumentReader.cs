using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace FileFlux.Infrastructure.Readers;

/// <summary>
/// 이미지 처리 기능이 통합된 멀티모달 PowerPoint 문서 리더
/// IImageToTextService가 제공된 경우 이미지에서 텍스트를 추출하여 enrichment
/// IImageRelevanceEvaluator가 제공된 경우 관련성 평가 후 선택적 포함
/// </summary>
public class MultiModalPowerPointDocumentReader : IDocumentReader
{
    private static readonly ImageDescriptionFormat s_format = new()
    {
        DocumentType = "PowerPoint",
        ImageTypeHint = "slide",
        SectionMarker = "PRESENTATION_IMAGES",
        Label = Label,
        ResultSubject = (_, imageType) => $"Presentation: {imageType} image",
    };

    private readonly IImageToTextService? _imageToTextService;
    private readonly IImageRelevanceEvaluator? _relevanceEvaluator;
    private readonly PowerPointDocumentReader _basePowerPointReader;

    public string ReaderType => "MultiModalPowerPointReader";

    public IEnumerable<string> SupportedExtensions => _basePowerPointReader.SupportedExtensions;

    public MultiModalPowerPointDocumentReader(IServiceProvider serviceProvider)
    {
        // IImageToTextService는 선택적 의존성
        _imageToTextService = serviceProvider.GetService<IImageToTextService>();
        // IImageRelevanceEvaluator는 선택적 의존성
        _relevanceEvaluator = serviceProvider.GetService<IImageRelevanceEvaluator>();
        _basePowerPointReader = new PowerPointDocumentReader();
    }

    public bool CanRead(string fileName)
    {
        return _basePowerPointReader.CanRead(fileName);
    }

    // ========================================
    // Stage 0: Read (Document Structure)
    // ========================================

    public Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return _basePowerPointReader.ReadAsync(filePath, cancellationToken);
    }

    public Task<ReadResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        return _basePowerPointReader.ReadAsync(stream, fileName, cancellationToken);
    }

    // ========================================
    // Stage 1: Extract (Raw Content)
    // ========================================

    /// <summary>
    /// The presentation's text, with a description of each image the base reader extracted — embedded pictures and
    /// rendered slides (<see cref="ExtractOptions.SlideRendering"/>) — appended with the slides it is on, when an
    /// <see cref="IImageToTextService"/> is registered. The extraction options decide which images there are.
    /// </summary>
    public async Task<RawContent> ExtractAsync(string filePath, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        var baseContent = await _basePowerPointReader.ExtractAsync(filePath, options, cancellationToken).ConfigureAwait(false);
        return await DescribeImagesAsync(baseContent, Path.GetFileNameWithoutExtension(filePath), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="ExtractAsync(string, ExtractOptions?, CancellationToken)"/>
    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        var baseContent = await _basePowerPointReader.ExtractAsync(stream, fileName, options, cancellationToken).ConfigureAwait(false);
        return await DescribeImagesAsync(baseContent, Path.GetFileNameWithoutExtension(fileName), cancellationToken).ConfigureAwait(false);
    }

    private Task<RawContent> DescribeImagesAsync(RawContent baseContent, string title, CancellationToken cancellationToken) =>
        ImageDescriptions.AppendAsync(baseContent, title, ReaderType, s_format, _imageToTextService, _relevanceEvaluator, cancellationToken);

    // A rendered slide is the slide itself; an embedded picture names the slides that show it.
    private static string Label(ImageInfo image, int number)
    {
        if (image.RenderedPage is not null && image.PageNumber is { } renderedSlide)
            return string.Create(CultureInfo.InvariantCulture, $"Slide {renderedSlide} (rendered):");

        var slides = image.PageNumbers;
        return slides.Count switch
        {
            0 => string.Create(CultureInfo.InvariantCulture, $"Presentation Image {number}:"),
            1 => string.Create(CultureInfo.InvariantCulture, $"Presentation Image {number} (slide {slides[0]}):"),
            _ => string.Create(CultureInfo.InvariantCulture, $"Presentation Image {number} (slides {string.Join(", ", slides)}):"),
        };
    }
}
