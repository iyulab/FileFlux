using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using A = DocumentFormat.OpenXml.Drawing;

namespace FileFlux.Infrastructure.Readers;

/// <summary>
/// 이미지 처리 기능이 통합된 멀티모달 PowerPoint 문서 리더
/// IImageToTextService가 제공된 경우 이미지에서 텍스트를 추출하여 enrichment
/// IImageRelevanceEvaluator가 제공된 경우 관련성 평가 후 선택적 포함
/// </summary>
public class MultiModalPowerPointDocumentReader : IDocumentReader
{
    private readonly IImageToTextService? _imageToTextService;
    private readonly IImageRelevanceEvaluator? _relevanceEvaluator;
    private readonly PowerPointDocumentReader _basePowerPointReader;

    public string ReaderType => "MultiModalPowerPointReader";

    public IEnumerable<string> SupportedExtensions => new[] { ".pptx" };

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
        // 기본 PowerPoint 텍스트 추출
        var baseContent = await _basePowerPointReader.ExtractAsync(filePath, options, cancellationToken);

        // 이미지 서비스가 없으면 기본 결과 반환
        if (_imageToTextService == null)
            return baseContent;

        // 이미지 처리가 가능한 경우 향상된 추출 수행
        return await ExtractWithImageProcessing(filePath, baseContent, cancellationToken);
    }

    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        // 기본 PowerPoint 텍스트 추출
        var baseContent = await _basePowerPointReader.ExtractAsync(stream, fileName, options, cancellationToken);

        // 이미지 서비스가 없으면 기본 결과 반환
        if (_imageToTextService == null)
            return baseContent;

        // 스트림 기반 이미지 처리는 복잡하므로 기본 결과 반환 (향후 확장 가능)
        return baseContent;
    }

    /// <summary>
    /// 이미지 처리를 포함한 향상된 PowerPoint 텍스트 추출
    /// </summary>
    private async Task<RawContent> ExtractWithImageProcessing(
        string filePath,
        RawContent baseContent,
        CancellationToken cancellationToken)
    {
        var enhancedText = new StringBuilder(baseContent.Text);
        var imageProcessingResults = new List<string>();
        var structuralHints = baseContent.Hints?.ToDictionary(kv => kv.Key, kv => kv.Value)
                             ?? new Dictionary<string, object>();

        // 문서 컨텍스트 준비 (관련성 평가용)
        var documentContext = PrepareDocumentContext(baseContent, filePath);

        try
        {
            using var presentationDocument = PresentationDocument.Open(filePath, false);
            var presentationPart = presentationDocument.PresentationPart;

            if (presentationPart?.Presentation == null)
            {
                return baseContent;
            }

            var slideIds = presentationPart.Presentation.SlideIdList?.Elements<SlideId>().ToList()
                          ?? new List<SlideId>();

            var imageCount = 0;
            var includedImageCount = 0;
            var excludedImageCount = 0;

            for (int slideIndex = 0; slideIndex < slideIds.Count; slideIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var slideId = slideIds[slideIndex];
                if (slideId.RelationshipId?.Value == null) continue;

                var slidePart = (SlidePart)presentationPart.GetPartById(slideId.RelationshipId.Value);
                documentContext.PageNumber = slideIndex + 1;

                // 슬라이드 텍스트 추출 (컨텍스트용)
                var slideText = ExtractSlideText(slidePart.Slide);
                if (!string.IsNullOrEmpty(slideText))
                {
                    documentContext.SurroundingText = TruncateText(slideText, 500);
                }

                var slideImages = await ExtractSlideImages(slidePart, slideIndex + 1, cancellationToken);

                if (slideImages.Any())
                {
                    // 관련성 평가가 활성화된 경우 배치 평가 수행
                    List<ImageRelevanceResult>? relevanceResults = null;
                    if (_relevanceEvaluator != null)
                    {
                        var imageTexts = slideImages.Select(img => img.ExtractedText).ToList();
                        relevanceResults = (await _relevanceEvaluator.EvaluateBatchAsync(
                            imageTexts, documentContext, cancellationToken)).ToList();
                    }

                    // 슬라이드 이미지 섹션 시작
                    var hasRelevantImages = false;
                    var slideImageTexts = new StringBuilder();

                    for (int i = 0; i < slideImages.Count; i++)
                    {
                        var imageResult = slideImages[i];
                        imageCount++;

                        // 관련성 평가 결과 확인
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
                                slideImageTexts.AppendLine($"<!-- SLIDE_{slideIndex + 1}_IMAGES_START -->");
                                hasRelevantImages = true;
                            }

                            slideImageTexts.AppendLine($"<!-- IMAGE_START:IMG_{imageCount} -->");
                            slideImageTexts.AppendLine($"Slide {slideIndex + 1} - Image {imageCount}:");
                            slideImageTexts.AppendLine(processedText);
                            slideImageTexts.AppendLine($"<!-- IMAGE_END:IMG_{imageCount} -->");

                            includedImageCount++;
                            imageProcessingResults.Add($"Slide {slideIndex + 1}: {imageResult.ImageType} image INCLUDED - {inclusionReason}");
                        }
                        else
                        {
                            excludedImageCount++;
                            imageProcessingResults.Add($"Slide {slideIndex + 1}: {imageResult.ImageType} image EXCLUDED - {inclusionReason}");
                        }
                    }

                    if (hasRelevantImages)
                    {
                        slideImageTexts.AppendLine($"<!-- SLIDE_{slideIndex + 1}_IMAGES_END -->");
                        enhancedText.AppendLine(slideImageTexts.ToString());
                    }
                }
            }

            // 구조적 힌트에 이미지 처리 정보 추가
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
        catch (Exception ex)
        {
            // 이미지 처리 실패 시 기본 결과 사용하되 경고 추가

            var warnings = baseContent.Warnings?.ToList() ?? new List<string>();
            warnings.Add($"Image processing failed: {ex.Message}");

            return new RawContent
            {
                Text = baseContent.Text,
                Blocks = baseContent.Blocks,  // Preserve extracted blocks
                Tables = baseContent.Tables,  // Preserve extracted tables
                Images = baseContent.Images,  // Preserve extracted images
                File = baseContent.File,
                Hints = baseContent.Hints ?? new Dictionary<string, object>(),
                Warnings = warnings,
                ReaderType = ReaderType
            };
        }

        return new RawContent
        {
            Text = enhancedText.ToString(),
            Blocks = baseContent.Blocks,  // Preserve extracted blocks
            Tables = baseContent.Tables,  // Preserve extracted tables
            Images = baseContent.Images,  // Preserve extracted images
            File = baseContent.File,
            Hints = structuralHints,
            Warnings = baseContent.Warnings,
            ReaderType = ReaderType
        };
    }

    /// <summary>
    /// 슬라이드에서 이미지를 추출하고 텍스트 변환 처리
    /// </summary>
    private async Task<List<ImageToTextResult>> ExtractSlideImages(
        SlidePart slidePart,
        int slideNumber,
        CancellationToken cancellationToken)
    {
        var results = new List<ImageToTextResult>();

        if (_imageToTextService == null)
        {
            return results;
        }

        try
        {
            // OpenXml의 ImagePart에서 이미지 추출
            var imageParts = slidePart.ImageParts;
            var imagePartsList = imageParts.ToList();

            foreach (var imagePart in imagePartsList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // 이미지 데이터 추출
                    var imageBytes = await ExtractImageBytes(imagePart);
                    if (imageBytes == null || imageBytes.Length == 0)
                        continue;

                    // 이미지 크기 확인
                    var (width, height) = GetImageDimensions(imageBytes);

                    if (ImageProcessingConstants.IsDecorativeImage(width, height))
                    {
                        // 작은 이미지(아이콘, 로고, 장식) 제외
                        continue;
                    }

                    // 이미지 타입 힌트 결정
                    var options = new ImageToTextOptions
                    {
                        ImageTypeHint = "presentation", // PowerPoint 이미지는 주로 프레젠테이션/차트
                        Quality = "medium",
                        ExtractStructure = true
                    };

                    var result = await _imageToTextService.ExtractTextAsync(imageBytes, options, cancellationToken);

                    if (!string.IsNullOrWhiteSpace(result.ExtractedText))
                    {
                        results.Add(result);
                    }
                }
                catch
                {
                    // 개별 이미지 처리 실패는 무시하고 계속 진행
                }
            }
        }
        catch
        {
            // 슬라이드 전체 이미지 처리 실패
        }

        return results;
    }

    /// <summary>
    /// ImagePart에서 바이트 배열 추출
    /// </summary>
    private static async Task<byte[]?> ExtractImageBytes(ImagePart imagePart)
    {
        try
        {
            using var stream = imagePart.GetStream();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            return memoryStream.ToArray();
        }
        catch (Exception)
        {
            // 이미지 추출 실패
            return null;
        }
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
                // JPEG 크기 파싱은 복잡하므로 기본값 반환 (충분히 큰 값)
                return (1000, 1000);
            }

            // 기타 포맷은 처리하도록 기본값 반환
            return (1000, 1000);
        }
        catch
        {
            // 파싱 실패 시 기본값
            return (1000, 1000);
        }
    }

    /// <summary>
    /// 슬라이드에서 텍스트만 추출 (컨텍스트용)
    /// </summary>
    private static string ExtractSlideText(Slide slide)
    {
        var textBuilder = new StringBuilder();

        try
        {
            var shapes = slide.CommonSlideData?.ShapeTree?.Elements<Shape>().ToList()
                        ?? new List<Shape>();

            foreach (var shape in shapes)
            {
                var textBody = shape.TextBody;
                if (textBody == null) continue;

                foreach (var paragraph in textBody.Elements<A.Paragraph>())
                {
                    foreach (var run in paragraph.Elements<A.Run>())
                    {
                        var text = run.Elements<A.Text>().FirstOrDefault();
                        if (text?.Text != null)
                        {
                            textBuilder.Append(text.Text);
                            textBuilder.Append(" ");
                        }
                    }
                }
            }
        }
        catch
        {
            // 텍스트 추출 실패는 무시
        }

        return textBuilder.ToString().Trim();
    }

    /// <summary>
    /// 문서 컨텍스트 준비 (관련성 평가용)
    /// </summary>
    private DocumentContext PrepareDocumentContext(RawContent baseContent, string filePath)
    {
        var context = new DocumentContext
        {
            DocumentType = "PowerPoint",
            DocumentText = TruncateText(baseContent.Text, 1000)
        };

        // 파일명에서 제목 추출
        context.Title = System.IO.Path.GetFileNameWithoutExtension(filePath);

        // 구조적 힌트에서 메타데이터 추출
        if (baseContent.Hints != null)
        {
            foreach (var hint in baseContent.Hints)
            {
                context.Metadata[hint.Key.ToString()] = hint.Value?.ToString() ?? "";
            }
        }

        // 간단한 키워드 추출 (공백으로 분리된 단어 중 길이가 5 이상인 것들)
        var words = baseContent.Text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
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
    private string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return string.Concat(text.AsSpan(0, maxLength), "...");
    }
}
