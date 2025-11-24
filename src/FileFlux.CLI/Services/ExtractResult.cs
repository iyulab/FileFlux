using FileFlux.Domain;

namespace FileFlux.CLI.Services;

/// <summary>
/// Result of document extraction - shared between extract and chunk commands
/// </summary>
public class ExtractResult
{
    /// <summary>
    /// Parsed document content (text with images processed)
    /// </summary>
    public required ParsedContent ParsedContent { get; init; }

    /// <summary>
    /// Processed content with image references replaced
    /// </summary>
    public required string ProcessedText { get; init; }

    /// <summary>
    /// Information about processed images
    /// </summary>
    public List<ProcessedImage> Images { get; init; } = new();

    /// <summary>
    /// Number of skipped images (icons/decorations)
    /// </summary>
    public int SkippedImageCount { get; init; }

    /// <summary>
    /// AI provider used (if any)
    /// </summary>
    public string? AIProvider { get; init; }

    /// <summary>
    /// Images directory path (if images were extracted)
    /// </summary>
    public string? ImagesDirectory { get; init; }
}

/// <summary>
/// AI analysis metadata for info.json
/// </summary>
public class AIAnalysisInfo
{
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public int ImagesAnalyzed { get; set; }
    public int TotalTokens { get; set; }
    public List<ImageAnalysisInfo> ImageAnalyses { get; set; } = new();
}

/// <summary>
/// Individual image analysis info
/// </summary>
public class ImageAnalysisInfo
{
    public string FileName { get; set; } = string.Empty;
    public string Dimensions { get; set; } = string.Empty;
    public int FileSize { get; set; }
    public string? Description { get; set; }
    public string? Error { get; set; }
}
