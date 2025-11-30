namespace FileFlux.Core;

/// <summary>
/// Interface for extracting text from images.
/// Consumer applications implement this with their preferred AI service (Azure Vision, OpenAI Vision, local OCR, etc.)
/// </summary>
public interface IImageToTextService
{
    /// <summary>
    /// Extracts text from an image byte array.
    /// </summary>
    /// <param name="imageData">Image byte array</param>
    /// <param name="options">Text extraction options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Extracted text and metadata</returns>
    Task<ImageToTextResult> ExtractTextAsync(
        byte[] imageData,
        ImageToTextOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts text from an image stream.
    /// </summary>
    /// <param name="imageStream">Image stream</param>
    /// <param name="options">Text extraction options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Extracted text and metadata</returns>
    Task<ImageToTextResult> ExtractTextAsync(
        Stream imageStream,
        ImageToTextOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts text from an image file path.
    /// </summary>
    /// <param name="imagePath">Image file path</param>
    /// <param name="options">Text extraction options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Extracted text and metadata</returns>
    Task<ImageToTextResult> ExtractTextAsync(
        string imagePath,
        ImageToTextOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List of supported image formats
    /// </summary>
    IEnumerable<string> SupportedImageFormats { get; }

    /// <summary>
    /// Service provider name (e.g., "AzureVision", "OpenAIVision", "TesseractOCR")
    /// </summary>
    string ProviderName { get; }
}

/// <summary>
/// Options for image text extraction
/// </summary>
public class ImageToTextOptions
{
    /// <summary>
    /// Language for text extraction (default: "auto" - auto-detect)
    /// </summary>
    public string Language { get; set; } = "auto";

    /// <summary>
    /// Image type hint (chart, table, document, photo, etc.)
    /// Helps AI service optimize processing
    /// </summary>
    public string? ImageTypeHint { get; set; }

    /// <summary>
    /// Text extraction quality level (low, medium, high)
    /// </summary>
    public string Quality { get; set; } = "medium";

    /// <summary>
    /// Whether to extract structural information (preserve table, list structures)
    /// </summary>
    public bool ExtractStructure { get; set; } = true;

    /// <summary>
    /// Whether to extract image metadata
    /// </summary>
    public bool ExtractMetadata { get; set; } = true;

    /// <summary>
    /// Custom prompt (null uses provider's default prompt)
    /// Set when consumer app wants to use a completely different prompt
    /// </summary>
    public string? CustomPrompt { get; set; }

    /// <summary>
    /// Custom options (service-specific settings)
    /// </summary>
    public Dictionary<string, object> CustomOptions { get; } = new();
}

/// <summary>
/// Result of image text extraction
/// </summary>
public class ImageToTextResult
{
    /// <summary>
    /// Extracted text content
    /// </summary>
    public string ExtractedText { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score for text in image (0.0 ~ 1.0)
    /// </summary>
    public double ConfidenceScore { get; set; }

    /// <summary>
    /// Detected language
    /// </summary>
    public string DetectedLanguage { get; set; } = "unknown";

    /// <summary>
    /// Image type (chart, table, document, photo, etc.)
    /// </summary>
    public string ImageType { get; set; } = "unknown";

    /// <summary>
    /// Structural element information (tables, lists, etc.)
    /// </summary>
    public List<StructuralElement> StructuralElements { get; set; } = new();

    /// <summary>
    /// Image metadata
    /// </summary>
    public ImageMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Processing time (milliseconds)
    /// </summary>
    public long ProcessingTimeMs { get; set; }

    /// <summary>
    /// Error message (null on success)
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Structural element within an image
/// </summary>
public class StructuralElement
{
    /// <summary>
    /// Element type (table, list, heading, paragraph, etc.)
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Text content of the element
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Position information within image (pixel coordinates)
    /// </summary>
    public BoundingBox BoundingBox { get; set; } = new();

    /// <summary>
    /// Confidence score (0.0 ~ 1.0)
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// Position information for an element within an image
/// </summary>
public class BoundingBox
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>
/// Image metadata
/// </summary>
public class ImageMetadata
{
    /// <summary>
    /// Image width (pixels)
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Image height (pixels)
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Image format (PNG, JPEG, GIF, etc.)
    /// </summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// File size (bytes)
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// DPI (Dots Per Inch)
    /// </summary>
    public int Dpi { get; set; }

    /// <summary>
    /// Color space (RGB, CMYK, Grayscale, etc.)
    /// </summary>
    public string ColorSpace { get; set; } = string.Empty;
}
