using FileFlux;
using System.Drawing;

namespace FileFlux.Infrastructure.Services;

/// <summary>
/// 테스트를 위한 Mock 이미지-텍스트 변환 서비스
/// 실제 AI 서비스 없이도 FileFlux의 멀티모달 기능을 테스트할 수 있도록 함
/// </summary>
public class MockImageToTextService : IImageToTextService
{
    public IEnumerable<string> SupportedImageFormats => new[]
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp"
    };

    public string ProviderName => "MockImageToText";

    public async Task<ImageToTextResult> ExtractTextAsync(
        byte[] imageData, 
        ImageToTextOptions? options = null, 
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(imageData);

        await Task.Delay(200, cancellationToken); // 실제 AI 서비스 호출 시뮬레이션

        var startTime = DateTime.UtcNow;
        
        // 이미지 크기 정보 추출 (실제 구현에서는 이미지 헤더 파싱)
        var metadata = ExtractImageMetadata(imageData);
        
        // Mock 텍스트 추출 시뮬레이션
        var result = GenerateMockResult(metadata, options);
        result.ProcessingTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
        
        return result;
    }

    public async Task<ImageToTextResult> ExtractTextAsync(
        Stream imageStream, 
        ImageToTextOptions? options = null, 
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(imageStream);

        // 스트림을 바이트 배열로 변환
        using var memoryStream = new MemoryStream();
        await imageStream.CopyToAsync(memoryStream, cancellationToken);
        var imageData = memoryStream.ToArray();

        return await ExtractTextAsync(imageData, options, cancellationToken);
    }

    public async Task<ImageToTextResult> ExtractTextAsync(
        string imagePath, 
        ImageToTextOptions? options = null, 
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("Image path cannot be null or empty", nameof(imagePath));

        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"Image file not found: {imagePath}");

        var imageData = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        return await ExtractTextAsync(imageData, options, cancellationToken);
    }

    /// <summary>
    /// 이미지 메타데이터 추출 (Mock 구현)
    /// </summary>
    private static ImageMetadata ExtractImageMetadata(byte[] imageData)
    {
        // 실제 구현에서는 이미지 헤더를 파싱하여 메타데이터 추출
        // Mock에서는 기본값 제공
        var format = DetectImageFormat(imageData);
        
        return new ImageMetadata
        {
            Width = 1024,  // Mock 크기
            Height = 768,  // Mock 크기
            Format = format,
            FileSize = imageData.Length,
            Dpi = 96,
            ColorSpace = "RGB"
        };
    }

    /// <summary>
    /// 이미지 형식 감지 (Magic Number 기반)
    /// </summary>
    private static string DetectImageFormat(byte[] imageData)
    {
        if (imageData.Length < 8) return "unknown";

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (imageData[0] == 0x89 && imageData[1] == 0x50 && imageData[2] == 0x4E && imageData[3] == 0x47)
            return "PNG";

        // JPEG: FF D8 FF
        if (imageData[0] == 0xFF && imageData[1] == 0xD8 && imageData[2] == 0xFF)
            return "JPEG";

        // GIF: GIF87a or GIF89a
        if (imageData[0] == 0x47 && imageData[1] == 0x49 && imageData[2] == 0x46)
            return "GIF";

        // BMP: 42 4D
        if (imageData[0] == 0x42 && imageData[1] == 0x4D)
            return "BMP";

        return "unknown";
    }

    /// <summary>
    /// Mock 텍스트 추출 결과 생성
    /// </summary>
    private static ImageToTextResult GenerateMockResult(ImageMetadata metadata, ImageToTextOptions? options)
    {
        var imageType = DetermineImageType(options?.ImageTypeHint);
        var mockText = GenerateMockText(imageType);
        var structuralElements = GenerateMockStructuralElements(imageType);

        return new ImageToTextResult
        {
            ExtractedText = mockText,
            ConfidenceScore = GenerateConfidenceScore(imageType),
            DetectedLanguage = options?.Language == "auto" ? "en" : options?.Language ?? "en",
            ImageType = imageType,
            StructuralElements = structuralElements,
            Metadata = metadata
        };
    }

    /// <summary>
    /// 이미지 타입 결정 (Mock 로직)
    /// </summary>
    private static string DetermineImageType(string? hint)
    {
        return hint switch
        {
            "chart" => "chart",
            "table" => "table", 
            "document" => "document",
            "photo" => "photo",
            _ => "document" // 기본값
        };
    }

    /// <summary>
    /// 이미지 타입별 Mock 텍스트 생성
    /// </summary>
    private static string GenerateMockText(string imageType)
    {
        return imageType switch
        {
            "chart" => GenerateChartText(),
            "table" => GenerateTableText(),
            "document" => GenerateDocumentText(),
            "photo" => GeneratePhotoText(),
            _ => "<!-- IMAGE_START -->\nExtracted text content from image.\n<!-- IMAGE_END -->"
        };
    }

    private static string GenerateChartText()
    {
        return """
            <!-- IMAGE_START -->
            Chart Title: Sales Performance Q1-Q4 2024
            
            Data Points:
            - Q1 2024: $125,000
            - Q2 2024: $150,000
            - Q3 2024: $175,000
            - Q4 2024: $200,000
            
            Chart Type: Bar Chart
            Trend: Consistent upward growth of 16.7% per quarter
            <!-- IMAGE_END -->
            """;
    }

    private static string GenerateTableText()
    {
        return """
            <!-- IMAGE_START -->
            <!-- TABLE_START -->
            | Product | Q1 Sales | Q2 Sales | Total |
            |---------|----------|----------|-------|
            | Product A | 45,000 | 52,000 | 97,000 |
            | Product B | 38,000 | 41,000 | 79,000 |
            | Product C | 42,000 | 57,000 | 99,000 |
            | Total | 125,000 | 150,000 | 275,000 |
            <!-- TABLE_END -->
            <!-- IMAGE_END -->
            """;
    }

    private static string GenerateDocumentText()
    {
        return """
            <!-- IMAGE_START -->
            Document Title: Project Requirements Specification
            
            Section 1: Overview
            This document outlines the technical requirements for the new system implementation.
            
            Section 2: Key Features
            - User authentication and authorization
            - Real-time data processing
            - Scalable architecture design
            - Comprehensive reporting capabilities
            
            Section 3: Technical Specifications
            The system must support concurrent users and provide 99.9% uptime.
            <!-- IMAGE_END -->
            """;
    }

    private static string GeneratePhotoText()
    {
        return """
            <!-- IMAGE_START -->
            Image Description: Office environment with multiple workstations
            
            Visible Text Elements:
            - Computer monitor displaying charts
            - Whiteboard with project timeline
            - Sign: "Meeting Room A - Capacity 12"
            - Documents on desk partially visible
            
            Context: Professional office setting with business documents
            <!-- IMAGE_END -->
            """;
    }

    /// <summary>
    /// Mock 구조적 요소 생성
    /// </summary>
    private static List<StructuralElement> GenerateMockStructuralElements(string imageType)
    {
        return imageType switch
        {
            "chart" => new List<StructuralElement>
            {
                new() { Type = "title", Content = "Sales Performance Q1-Q4 2024", BoundingBox = new BoundingBox { X = 100, Y = 50, Width = 300, Height = 40 }, Confidence = 0.95 },
                new() { Type = "data", Content = "Q1: $125,000", BoundingBox = new BoundingBox { X = 50, Y = 200, Width = 100, Height = 30 }, Confidence = 0.92 }
            },
            "table" => new List<StructuralElement>
            {
                new() { Type = "table", Content = "Sales data table", BoundingBox = new BoundingBox { X = 50, Y = 100, Width = 500, Height = 200 }, Confidence = 0.88 }
            },
            "document" => new List<StructuralElement>
            {
                new() { Type = "heading", Content = "Project Requirements Specification", BoundingBox = new BoundingBox { X = 50, Y = 30, Width = 400, Height = 30 }, Confidence = 0.93 },
                new() { Type = "paragraph", Content = "Overview section content", BoundingBox = new BoundingBox { X = 50, Y = 80, Width = 450, Height = 60 }, Confidence = 0.89 }
            },
            _ => new List<StructuralElement>()
        };
    }

    /// <summary>
    /// 신뢰도 점수 생성 (이미지 타입별 차이)
    /// </summary>
    private static double GenerateConfidenceScore(string imageType)
    {
        return imageType switch
        {
            "document" => 0.92,  // 텍스트 문서는 높은 신뢰도
            "table" => 0.88,     // 구조화된 표는 중간-높은 신뢰도
            "chart" => 0.85,     // 차트는 중간 신뢰도  
            "photo" => 0.75,     // 사진은 낮은-중간 신뢰도
            _ => 0.80
        };
    }
}