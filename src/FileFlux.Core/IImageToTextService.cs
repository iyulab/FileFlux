using FileFlux.Domain;

namespace FileFlux;

/// <summary>
/// 이미지에서 텍스트를 추출하는 서비스 인터페이스
/// 소비 애플리케이션에서 원하는 AI 서비스를 구현 (Azure Vision, OpenAI Vision, 로컬 OCR 등)
/// </summary>
public interface IImageToTextService
{
    /// <summary>
    /// 이미지 바이트 배열에서 텍스트를 추출합니다.
    /// </summary>
    /// <param name="imageData">이미지 바이트 배열</param>
    /// <param name="options">텍스트 추출 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>추출된 텍스트 및 메타데이터</returns>
    Task<ImageToTextResult> ExtractTextAsync(
        byte[] imageData, 
        ImageToTextOptions? options = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 이미지 스트림에서 텍스트를 추출합니다.
    /// </summary>
    /// <param name="imageStream">이미지 스트림</param>
    /// <param name="options">텍스트 추출 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>추출된 텍스트 및 메타데이터</returns>
    Task<ImageToTextResult> ExtractTextAsync(
        Stream imageStream, 
        ImageToTextOptions? options = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 파일 경로의 이미지에서 텍스트를 추출합니다.
    /// </summary>
    /// <param name="imagePath">이미지 파일 경로</param>
    /// <param name="options">텍스트 추출 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>추출된 텍스트 및 메타데이터</returns>
    Task<ImageToTextResult> ExtractTextAsync(
        string imagePath, 
        ImageToTextOptions? options = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 지원하는 이미지 형식 목록
    /// </summary>
    IEnumerable<string> SupportedImageFormats { get; }

    /// <summary>
    /// 서비스 제공자명 (예: "AzureVision", "OpenAIVision", "TesseractOCR")
    /// </summary>
    string ProviderName { get; }
}

/// <summary>
/// 이미지에서 텍스트 추출 옵션
/// </summary>
public class ImageToTextOptions
{
    /// <summary>
    /// 추출할 텍스트 언어 (기본값: "auto" - 자동 감지)
    /// </summary>
    public string Language { get; set; } = "auto";

    /// <summary>
    /// 이미지 타입 힌트 (chart, table, document, photo 등)
    /// AI 서비스가 최적화된 처리를 할 수 있도록 도움
    /// </summary>
    public string? ImageTypeHint { get; set; }

    /// <summary>
    /// 텍스트 추출 품질 수준 (low, medium, high)
    /// </summary>
    public string Quality { get; set; } = "medium";

    /// <summary>
    /// 구조적 정보 추출 여부 (테이블, 리스트 구조 보존)
    /// </summary>
    public bool ExtractStructure { get; set; } = true;

    /// <summary>
    /// 이미지 메타데이터 추출 여부
    /// </summary>
    public bool ExtractMetadata { get; set; } = true;

    /// <summary>
    /// 커스텀 옵션 (서비스별 특화 설정)
    /// </summary>
    public Dictionary<string, object> CustomOptions { get; set; } = new();
}

/// <summary>
/// 이미지에서 텍스트 추출 결과
/// </summary>
public class ImageToTextResult
{
    /// <summary>
    /// 추출된 텍스트 내용
    /// </summary>
    public string ExtractedText { get; set; } = string.Empty;

    /// <summary>
    /// 이미지 내 텍스트의 신뢰도 점수 (0.0 ~ 1.0)
    /// </summary>
    public double ConfidenceScore { get; set; }

    /// <summary>
    /// 감지된 언어
    /// </summary>
    public string DetectedLanguage { get; set; } = "unknown";

    /// <summary>
    /// 이미지 타입 (chart, table, document, photo 등)
    /// </summary>
    public string ImageType { get; set; } = "unknown";

    /// <summary>
    /// 구조적 요소 정보 (테이블, 리스트 등)
    /// </summary>
    public List<StructuralElement> StructuralElements { get; set; } = new();

    /// <summary>
    /// 이미지 메타데이터
    /// </summary>
    public ImageMetadata Metadata { get; set; } = new();

    /// <summary>
    /// 처리 시간 (밀리초)
    /// </summary>
    public long ProcessingTimeMs { get; set; }

    /// <summary>
    /// 오류 메시지 (성공시 null)
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 이미지 내 구조적 요소
/// </summary>
public class StructuralElement
{
    /// <summary>
    /// 요소 타입 (table, list, heading, paragraph 등)
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 요소의 텍스트 내용
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 이미지 내 위치 정보 (픽셀 좌표)
    /// </summary>
    public BoundingBox BoundingBox { get; set; } = new();

    /// <summary>
    /// 신뢰도 점수 (0.0 ~ 1.0)
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// 이미지 내 요소의 위치 정보
/// </summary>
public class BoundingBox
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>
/// 이미지 메타데이터
/// </summary>
public class ImageMetadata
{
    /// <summary>
    /// 이미지 폭 (픽셀)
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// 이미지 높이 (픽셀)
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// 이미지 형식 (PNG, JPEG, GIF 등)
    /// </summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// 파일 크기 (바이트)
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// DPI (Dots Per Inch)
    /// </summary>
    public int Dpi { get; set; }

    /// <summary>
    /// 색상 공간 (RGB, CMYK, Grayscale 등)
    /// </summary>
    public string ColorSpace { get; set; } = string.Empty;
}