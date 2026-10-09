using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Globalization;

namespace FileFlux.Infrastructure.Readers;

/// <summary>
/// 이미지 처리 기능이 통합된 멀티모달 PowerPoint 문서 리더
/// IImageToTextService가 제공된 경우 이미지에서 텍스트를 추출하여 enrichment
/// IImageRelevanceEvaluator가 제공된 경우 관련성 평가 후 선택적 포함
/// </summary>
public class MultiModalPowerPointDocumentReader : IDocumentReader
{
    private static readonly char[] s_keywordSeparators = [' ', '\n', '\r', '\t'];
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

    public async Task<RawContent> ExtractAsync(string filePath, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        var baseContent = await _basePowerPointReader.ExtractAsync(filePath, options, cancellationToken).ConfigureAwait(false);
        return await DescribeImagesAsync(baseContent, Path.GetFileNameWithoutExtension(filePath), cancellationToken).ConfigureAwait(false);
    }

    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        var baseContent = await _basePowerPointReader.ExtractAsync(stream, fileName, options, cancellationToken).ConfigureAwait(false);
        return await DescribeImagesAsync(baseContent, Path.GetFileNameWithoutExtension(fileName), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Describes the images the base reader extracted — embedded pictures and rendered slides
    /// (<see cref="ExtractOptions.SlideRendering"/>) — through the registered <see cref="IImageToTextService"/>, and appends
    /// each description with the slides it is on. Works from <see cref="RawContent.Images"/>, so the file and stream paths
    /// describe the same images and the extraction options (<see cref="ExtractOptions.ExtractImages"/>,
    /// <see cref="ExtractOptions.MaxImageSize"/>) apply. Without a service the base content is returned as is.
    /// </summary>
    private async Task<RawContent> DescribeImagesAsync(RawContent baseContent, string title, CancellationToken cancellationToken)
    {
        if (_imageToTextService == null)
            return baseContent;

        var enhancedText = new StringBuilder(baseContent.Text);
        var imageProcessingResults = new List<string>();
        var structuralHints = baseContent.Hints?.ToDictionary(kv => kv.Key, kv => kv.Value)
                             ?? new Dictionary<string, object>();

        var documentContext = PrepareDocumentContext(baseContent, title);
        documentContext.SurroundingText = TruncateText(baseContent.Text, 500);

        try
        {
            var imageCount = 0;
            var includedImageCount = 0;
            var excludedImageCount = 0;

            var documentImages = await DescribeAsync(baseContent.Images, cancellationToken).ConfigureAwait(false);

            if (documentImages.Count != 0)
            {
                List<ImageRelevanceResult>? relevanceResults = null;
                if (_relevanceEvaluator != null)
                {
                    var imageTexts = documentImages.Select(img => img.Result.ExtractedText).ToList();
                    relevanceResults = (await _relevanceEvaluator.EvaluateBatchAsync(
                        imageTexts, documentContext, cancellationToken).ConfigureAwait(false)).ToList();
                }

                var hasRelevantImages = false;
                var documentImageTexts = new StringBuilder();

                for (int i = 0; i < documentImages.Count; i++)
                {
                    var (imageResult, image) = documentImages[i];
                    imageCount++;

                    bool shouldInclude = true;
                    string? processedText = imageResult.ExtractedText;
                    string inclusionReason = "No relevance evaluation";

                    if (relevanceResults != null && i < relevanceResults.Count)
                    {
                        var relevance = relevanceResults[i];
                        shouldInclude = relevance.Recommendation != InclusionRecommendation.MustExclude &&
                                      relevance.Recommendation != InclusionRecommendation.ShouldExclude;

                        if (!string.IsNullOrEmpty(relevance.ProcessedText))
                        {
                            processedText = relevance.ProcessedText;
                        }

                        inclusionReason = $"{relevance.Category}: {relevance.Reasoning} (Score: {relevance.RelevanceScore:F2})";
                    }

                    if (shouldInclude)
                    {
                        if (!hasRelevantImages)
                        {
                            documentImageTexts.AppendLine($"<!-- PRESENTATION_IMAGES_START -->");
                            hasRelevantImages = true;
                        }

                        documentImageTexts.AppendLine(CultureInfo.InvariantCulture, $"<!-- IMAGE_START:IMG_{imageCount} -->");
                        documentImageTexts.AppendLine(Label(image, imageCount));
                        documentImageTexts.AppendLine(processedText);
                        documentImageTexts.AppendLine(CultureInfo.InvariantCulture, $"<!-- IMAGE_END:IMG_{imageCount} -->");

                        includedImageCount++;
                        imageProcessingResults.Add($"Presentation: {imageResult.ImageType} image INCLUDED - {inclusionReason}");
                    }
                    else
                    {
                        excludedImageCount++;
                        imageProcessingResults.Add($"Presentation: {imageResult.ImageType} image EXCLUDED - {inclusionReason}");
                    }
                }

                if (hasRelevantImages)
                {
                    documentImageTexts.AppendLine($"<!-- PRESENTATION_IMAGES_END -->");
                    enhancedText.AppendLine(documentImageTexts.ToString());
                }
            }

            if (imageCount > 0)
            {
                structuralHints["HasImages"] = true;
                structuralHints["TotalImageCount"] = imageCount;
                structuralHints["IncludedImageCount"] = includedImageCount;
                structuralHints["ExcludedImageCount"] = excludedImageCount;
                structuralHints["ImageProcessingResults"] = imageProcessingResults;

                if (_relevanceEvaluator != null)
                {
                    structuralHints["ImageRelevanceEvaluationEnabled"] = true;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var warnings = baseContent.Warnings?.ToList() ?? new List<string>();
            warnings.Add($"Image processing failed: {ex.Message}");

            var fallback = baseContent.WithText(baseContent.Text);
            fallback.Warnings = warnings;
            fallback.ReaderType = ReaderType;
            return fallback;
        }

        var enhanced = baseContent.WithText(enhancedText.ToString());
        enhanced.Hints = structuralHints;
        enhanced.ReaderType = ReaderType;
        return enhanced;
    }

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

    /// <summary>
    /// Describes each image that has data and is not decorative (rendered slides are never decorative). An image the service
    /// fails on is skipped; the others are still described.
    /// </summary>
    private async Task<List<(ImageToTextResult Result, ImageInfo Image)>> DescribeAsync(
        IEnumerable<ImageInfo> images,
        CancellationToken cancellationToken)
    {
        var results = new List<(ImageToTextResult Result, ImageInfo Image)>();

        foreach (var image in images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (image.Data is not { Length: > 0 } imageBytes)
                continue;

            if (image.RenderedPage is null)
            {
                var (width, height) = GetImageDimensions(imageBytes);
                if (ImageProcessingConstants.IsDecorativeImage(width, height))
                    continue;
            }

            try
            {
                var options = new ImageToTextOptions
                {
                    ImageTypeHint = "slide",
                    Quality = "medium",
                    ExtractStructure = true
                };

                var result = await _imageToTextService!.ExtractTextAsync(imageBytes, options, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(result.ExtractedText))
                    results.Add((result, image));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // One image the service cannot read does not stop the others.
            }
        }

        return results;
    }

    /// <summary>
    /// 이미지 바이트에서 크기 정보 추출
    /// </summary>
    private static (int width, int height) GetImageDimensions(byte[] imageBytes)
    {
        try
        {
            // PNG signature
            if (imageBytes.Length > 24 &&
                imageBytes[0] == 0x89 && imageBytes[1] == 0x50 &&
                imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
            {
                var width = (imageBytes[16] << 24) | (imageBytes[17] << 16) |
                           (imageBytes[18] << 8) | imageBytes[19];
                var height = (imageBytes[20] << 24) | (imageBytes[21] << 16) |
                            (imageBytes[22] << 8) | imageBytes[23];
                return (width, height);
            }

            // JPEG signature
            if (imageBytes.Length > 2 && imageBytes[0] == 0xFF && imageBytes[1] == 0xD8)
            {
                return (1000, 1000);
            }

            return (1000, 1000);
        }
        catch
        {
            return (1000, 1000);
        }
    }

    /// <summary>
    /// 문서 컨텍스트 준비 (관련성 평가용)
    /// </summary>
    private static DocumentContext PrepareDocumentContext(RawContent baseContent, string title)
    {
        var context = new DocumentContext
        {
            DocumentType = "PowerPoint",
            DocumentText = TruncateText(baseContent.Text, 1000)
        };

        // 파일명에서 제목 추출
        context.Title = title;

        // 구조적 힌트에서 메타데이터 추출
        if (baseContent.Hints != null)
        {
            foreach (var hint in baseContent.Hints)
            {
                context.Metadata[hint.Key.ToString()] = hint.Value?.ToString() ?? "";
            }
        }

        // 간단한 키워드 추출 (공백으로 분리된 단어 중 길이가 5 이상인 것들)
        var words = baseContent.Text.Split(s_keywordSeparators, StringSplitOptions.RemoveEmptyEntries);
        context.Keywords = words
            .Where(w => w.Length >= 5)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        return context;
    }

    /// <summary>
    /// 텍스트 자르기 헬퍼
    /// </summary>
    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return string.Concat(text.AsSpan(0, maxLength), "...");
    }
}
