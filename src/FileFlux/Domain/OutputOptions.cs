namespace FileFlux.Domain;

/// <summary>
/// Options for output generation
/// </summary>
public class OutputOptions
{
    /// <summary>
    /// Output directory path. If null, defaults to {input}.{command}/
    /// </summary>
    public string? OutputDirectory { get; set; }

    /// <summary>
    /// Output format (md, json, jsonl)
    /// </summary>
    public string Format { get; set; } = "md";

    /// <summary>
    /// Extract and save images to files
    /// </summary>
    public bool ExtractImages { get; set; } = true;

    /// <summary>
    /// Minimum image file size in bytes (default: 5000)
    /// </summary>
    public int MinImageSize { get; set; } = 5000;

    /// <summary>
    /// Minimum image dimension in pixels (default: 100)
    /// </summary>
    public int MinImageDimension { get; set; } = 100;

    /// <summary>
    /// Enable AI for image analysis
    /// </summary>
    public bool EnableAI { get; set; }

    /// <summary>
    /// Generate default output directory based on input file
    /// </summary>
    public static string GetDefaultOutputDirectory(string inputPath, string command)
    {
        return $"{inputPath}.{command}";
    }
}

/// <summary>
/// Processed image information
/// </summary>
public class ProcessedImage
{
    /// <summary>
    /// Image file name
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Image width in pixels
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Image height in pixels
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Image file size in bytes
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// AI-generated description
    /// </summary>
    public string? AIDescription { get; set; }

    /// <summary>
    /// AI analysis error message
    /// </summary>
    public string? AIError { get; set; }
}

/// <summary>
/// Result of extraction operation
/// </summary>
public class ExtractionResult
{
    /// <summary>
    /// Parsed document content
    /// </summary>
    public required ParsedContent ParsedContent { get; set; }

    /// <summary>
    /// Processed text with image references replaced
    /// </summary>
    public required string ProcessedText { get; set; }

    /// <summary>
    /// Extracted images
    /// </summary>
    public List<ProcessedImage> Images { get; set; } = new();

    /// <summary>
    /// Number of skipped images (too small or invalid)
    /// </summary>
    public int SkippedImageCount { get; set; }

    /// <summary>
    /// AI provider used (if any)
    /// </summary>
    public string? AIProvider { get; set; }

    /// <summary>
    /// Images directory path
    /// </summary>
    public string? ImagesDirectory { get; set; }

    /// <summary>
    /// Output directory path
    /// </summary>
    public string? OutputDirectory { get; set; }
}

/// <summary>
/// Result of chunking operation
/// </summary>
public class ChunkingResult
{
    /// <summary>
    /// Document chunks
    /// </summary>
    public required DocumentChunk[] Chunks { get; set; }

    /// <summary>
    /// Extraction result
    /// </summary>
    public required ExtractionResult Extraction { get; set; }

    /// <summary>
    /// Output directory path
    /// </summary>
    public string? OutputDirectory { get; set; }

    /// <summary>
    /// Chunking options used
    /// </summary>
    public required ChunkingOptions Options { get; set; }
}

/// <summary>
/// Processing information for output
/// </summary>
public class ProcessingInfo
{
    /// <summary>
    /// Command name (extract, chunk, process)
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Output format
    /// </summary>
    public string Format { get; set; } = "md";

    /// <summary>
    /// Chunking strategy used
    /// </summary>
    public string? Strategy { get; set; }

    /// <summary>
    /// Maximum chunk size
    /// </summary>
    public int? MaxChunkSize { get; set; }

    /// <summary>
    /// Overlap size
    /// </summary>
    public int? OverlapSize { get; set; }

    /// <summary>
    /// AI provider used
    /// </summary>
    public string? AIProvider { get; set; }

    /// <summary>
    /// Whether enrichment was enabled
    /// </summary>
    public bool EnrichmentEnabled { get; set; }
}
