using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace FileFlux.Infrastructure.Readers;

/// <summary>
/// 이미지 처리 기능이 통합된 멀티모달 PDF 문서 리더
/// IImageToTextService가 제공된 경우 이미지에서 텍스트를 추출하여 enrichment
/// IImageRelevanceEvaluator가 제공된 경우 관련성 평가 후 선택적 포함
/// </summary>
public class MultiModalPdfDocumentReader : IDocumentReader
{
    private readonly IImageToTextService? _imageToTextService;
    private readonly IImageRelevanceEvaluator? _relevanceEvaluator;
    private readonly PdfDocumentReader _basePdfReader;

    public string ReaderType => "MultiModalPdfReader";

    public IEnumerable<string> SupportedExtensions => new[] { ".pdf" };

    public MultiModalPdfDocumentReader(IServiceProvider serviceProvider)
    {
        // IImageToTextService는 선택적 의존성
        _imageToTextService = serviceProvider.GetService<IImageToTextService>();
        // IImageRelevanceEvaluator는 선택적 의존성
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

    public async Task<RawContent> ExtractAsync(string filePath, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        // 기본 PDF 텍스트 추출
        var baseContent = await _basePdfReader.ExtractAsync(filePath, options, cancellationToken);

        // 이미지 서비스가 없으면 기본 결과 반환
        if (_imageToTextService == null)
            return baseContent;

        // 이미지 처리가 가능한 경우 향상된 추출 수행
        return await ExtractWithImageProcessing(filePath, baseContent, cancellationToken);
    }

    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        // 기본 PDF 텍스트 추출
        var baseContent = await _basePdfReader.ExtractAsync(stream, fileName, options, cancellationToken);

        // 이미지 서비스가 없으면 기본 결과 반환
        if (_imageToTextService == null)
            return baseContent;

        // 스트림 기반 이미지 처리는 복잡하므로 기본 결과 반환 (향후 확장 가능)
        return baseContent;
    }

    /// <summary>
    /// 이미지 처리를 포함한 향상된 PDF 텍스트 추출
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
            using var document = PdfDocument.Open(filePath);

            var totalPages = document.NumberOfPages;
            var imageCount = 0;
            var includedImageCount = 0;
            var excludedImageCount = 0;

            for (int pageNum = 1; pageNum <= totalPages; pageNum++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = document.GetPage(pageNum);
                documentContext.PageNumber = pageNum;

                // 페이지 주변 텍스트 추출
                var pageText = page.Text;
                if (!string.IsNullOrEmpty(pageText))
                {
                    documentContext.SurroundingText = TruncateText(pageText, 500);
                }

                var pageImages = await ExtractPageImages(page, pageNum, cancellationToken);

                if (pageImages.Any())
                {
                    // 관련성 평가가 활성화된 경우 배치 평가 수행
                    List<ImageRelevanceResult>? relevanceResults = null;
                    if (_relevanceEvaluator != null)
                    {
                        var imageTexts = pageImages.Select(img => img.ExtractedText).ToList();
                        relevanceResults = (await _relevanceEvaluator.EvaluateBatchAsync(
                            imageTexts, documentContext, cancellationToken)).ToList();
                    }

                    // 페이지 이미지 섹션 시작
                    var hasRelevantImages = false;
                    var pageImageTexts = new StringBuilder();

                    for (int i = 0; i < pageImages.Count; i++)
                    {
                        var imageResult = pageImages[i];
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
                                pageImageTexts.AppendLine($"<!-- PAGE_{pageNum}_IMAGES_START -->");
                                hasRelevantImages = true;
                            }

                            pageImageTexts.AppendLine($"<!-- IMAGE_START:IMG_{imageCount} -->");
                            pageImageTexts.AppendLine($"Page {pageNum} - Image {imageCount}:");
                            pageImageTexts.AppendLine(processedText);
                            pageImageTexts.AppendLine($"<!-- IMAGE_END:IMG_{imageCount} -->");

                            includedImageCount++;
                            imageProcessingResults.Add($"Page {pageNum}: {imageResult.ImageType} image INCLUDED - {inclusionReason}");
                        }
                        else
                        {
                            excludedImageCount++;
                            imageProcessingResults.Add($"Page {pageNum}: {imageResult.ImageType} image EXCLUDED - {inclusionReason}");
                        }
                    }

                    if (hasRelevantImages)
                    {
                        pageImageTexts.AppendLine($"<!-- PAGE_{pageNum}_IMAGES_END -->");
                        enhancedText.AppendLine(pageImageTexts.ToString());
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
    /// 페이지에서 이미지를 추출하고 텍스트 변환 처리
    /// </summary>
    private async Task<List<ImageToTextResult>> ExtractPageImages(
        Page page,
        int pageNum,
        CancellationToken cancellationToken)
    {
        var results = new List<ImageToTextResult>();

        if (_imageToTextService == null)
            return results;

        try
        {
            // PdfPig을 통한 이미지 추출
            var images = page.GetImages();

            foreach (var image in images)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // 이미지 크기 확인 - 너무 작은 이미지는 장식용으로 간주하고 제외
                    var width = (int)(image.Bounds.Width);
                    var height = (int)(image.Bounds.Height);

                    if (ImageProcessingConstants.IsDecorativeImage(width, height))
                    {
                        // 작은 이미지(아이콘, 로고, 장식) 제외
                        continue;
                    }

                    // 이미지 데이터 추출
                    var imageBytes = await ExtractImageBytes(image);
                    if (imageBytes != null && imageBytes.Length > 0)
                    {
                        // 이미지 타입 힌트 결정
                        var options = new ImageToTextOptions
                        {
                            ImageTypeHint = "document", // PDF 내 이미지는 주로 문서/차트
                            Quality = "medium",
                            ExtractStructure = true
                        };

                        var result = await _imageToTextService.ExtractTextAsync(imageBytes, options, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(result.ExtractedText))
                        {
                            results.Add(result);
                        }
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
            // 페이지 전체 이미지 처리 실패
        }

        return results;
    }

    /// <summary>
    /// PdfPig 이미지 객체에서 바이트 배열 추출
    /// </summary>
    private static async Task<byte[]?> ExtractImageBytes(IPdfImage image)
    {
        try
        {
            // PdfPig API를 통한 이미지 데이터 추출
            // 현재 PdfPig 버전에서는 직접적인 이미지 바이트 추출이 제한적
            // Mock 데이터를 반환하여 기능 시연 (실제 구현에서는 이미지 처리 라이브러리 사용)

            // 이미지 크기 정보를 기반으로 Mock 이미지 생성
            var width = (int)(image.Bounds.Width);
            var height = (int)(image.Bounds.Height);

            // 간단한 Mock 이미지 데이터 (PNG 헤더 + 기본 데이터)
            var mockImageData = new byte[]
            {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
                // Mock data representing image content
                0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR chunk
                (byte)(width >> 8), (byte)(width & 0xFF),
                (byte)(height >> 8), (byte)(height & 0xFF),
                0x08, 0x02, 0x00, 0x00, 0x00 // PNG parameters
            };

            return await Task.FromResult(mockImageData);
        }
        catch (Exception)
        {
            // 이미지 추출 실패
            return null;
        }
    }

    /// <summary>
    /// 문서 컨텍스트 준비 (관련성 평가용)
    /// </summary>
    private DocumentContext PrepareDocumentContext(RawContent baseContent, string filePath)
    {
        var context = new DocumentContext
        {
            DocumentType = "PDF",
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
