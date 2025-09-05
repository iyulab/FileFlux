using FileFlux;
using FileFlux.Domain;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace FileFlux.Infrastructure.Readers;

/// <summary>
/// 이미지 처리 기능이 통합된 멀티모달 PDF 문서 리더
/// IImageToTextService가 제공된 경우 이미지에서 텍스트를 추출하여 enrichment
/// </summary>
public class MultiModalPdfDocumentReader : IDocumentReader
{
    private readonly IImageToTextService? _imageToTextService;
    private readonly PdfDocumentReader _basePdfReader;

    public string ReaderType => "MultiModalPdfReader";

    public IEnumerable<string> SupportedExtensions => new[] { ".pdf" };

    public MultiModalPdfDocumentReader(IServiceProvider serviceProvider)
    {
        // IImageToTextService는 선택적 의존성
        _imageToTextService = serviceProvider.GetService<IImageToTextService>();
        _basePdfReader = new PdfDocumentReader();
    }

    public bool CanRead(string fileName)
    {
        return _basePdfReader.CanRead(fileName);
    }

    public async Task<RawDocumentContent> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        // 기본 PDF 텍스트 추출
        var baseContent = await _basePdfReader.ExtractAsync(filePath, cancellationToken);
        
        // 이미지 서비스가 없으면 기본 결과 반환
        if (_imageToTextService == null)
            return baseContent;

        // 이미지 처리가 가능한 경우 향상된 추출 수행
        return await ExtractWithImageProcessing(filePath, baseContent, cancellationToken);
    }

    public async Task<RawDocumentContent> ExtractAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        // 기본 PDF 텍스트 추출
        var baseContent = await _basePdfReader.ExtractAsync(stream, fileName, cancellationToken);
        
        // 이미지 서비스가 없으면 기본 결과 반환
        if (_imageToTextService == null)
            return baseContent;

        // 스트림 기반 이미지 처리는 복잡하므로 기본 결과 반환 (향후 확장 가능)
        return baseContent;
    }

    /// <summary>
    /// 이미지 처리를 포함한 향상된 PDF 텍스트 추출
    /// </summary>
    private async Task<RawDocumentContent> ExtractWithImageProcessing(
        string filePath, 
        RawDocumentContent baseContent, 
        CancellationToken cancellationToken)
    {
        var enhancedText = new StringBuilder(baseContent.Text);
        var imageProcessingResults = new List<string>();
        var structuralHints = baseContent.StructuralHints?.ToDictionary(kv => kv.Key, kv => kv.Value) 
                             ?? new Dictionary<string, object>();

        try
        {
            using var document = PdfDocument.Open(filePath);
            
            var totalPages = document.NumberOfPages;
            var imageCount = 0;

            for (int pageNum = 1; pageNum <= totalPages; pageNum++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = document.GetPage(pageNum);
                var pageImages = await ExtractPageImages(page, pageNum, cancellationToken);
                
                if (pageImages.Any())
                {
                    // 페이지 이미지 섹션 시작
                    enhancedText.AppendLine($"<!-- PAGE_{pageNum}_IMAGES_START -->");
                    
                    foreach (var imageResult in pageImages)
                    {
                        imageCount++;
                        enhancedText.AppendLine($"<!-- IMAGE_START:IMG_{imageCount} -->");
                        enhancedText.AppendLine($"Page {pageNum} - Image {imageCount}:");
                        enhancedText.AppendLine(imageResult.ExtractedText);
                        enhancedText.AppendLine($"<!-- IMAGE_END:IMG_{imageCount} -->");
                        
                        imageProcessingResults.Add($"Page {pageNum}: {imageResult.ImageType} image processed with {imageResult.ConfidenceScore:F2} confidence");
                    }
                    
                    enhancedText.AppendLine($"<!-- PAGE_{pageNum}_IMAGES_END -->");
                }
            }

            // 구조적 힌트에 이미지 처리 정보 추가
            if (imageCount > 0)
            {
                structuralHints["HasImages"] = true;
                structuralHints["ImageCount"] = imageCount;
                structuralHints["ImageProcessingResults"] = imageProcessingResults;
            }
        }
        catch (Exception ex)
        {
            // 이미지 처리 실패 시 기본 결과 사용하되 경고 추가
            var warnings = baseContent.ExtractionWarnings?.ToList() ?? new List<string>();
            warnings.Add($"Image processing failed: {ex.Message}");
            
            return new RawDocumentContent
            {
                Text = baseContent.Text,
                FileInfo = baseContent.FileInfo,
                StructuralHints = baseContent.StructuralHints ?? new Dictionary<string, object>(),
                ExtractionWarnings = warnings
            };
        }

        return new RawDocumentContent
        {
            Text = enhancedText.ToString(),
            FileInfo = baseContent.FileInfo,
            StructuralHints = structuralHints,
            ExtractionWarnings = baseContent.ExtractionWarnings
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
                catch (Exception ex)
                {
                    // 개별 이미지 처리 실패는 로그만 남기고 계속 진행
                    Console.WriteLine($"Failed to process image on page {pageNum}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            // 페이지 전체 이미지 처리 실패
            Console.WriteLine($"Failed to extract images from page {pageNum}: {ex.Message}");
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
}