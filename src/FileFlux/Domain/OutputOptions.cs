namespace FileFlux.Domain;

/// <summary>
/// Output directory structure for FileFlux processing stages
/// </summary>
public class OutputDirectories
{
    /// <summary>
    /// Base output directory (.fileflux/filename_output/)
    /// </summary>
    public string Base { get; set; } = string.Empty;

    /// <summary>
    /// Extract stage output directory
    /// </summary>
    public string Extract { get; set; } = string.Empty;

    /// <summary>
    /// Refine stage output directory
    /// </summary>
    public string Refine { get; set; } = string.Empty;

    /// <summary>
    /// Chunks stage output directory
    /// </summary>
    public string Chunks { get; set; } = string.Empty;

    /// <summary>
    /// Enrich stage output directory
    /// </summary>
    public string Enrich { get; set; } = string.Empty;

    /// <summary>
    /// Images output directory
    /// </summary>
    public string Images { get; set; } = string.Empty;

    /// <summary>
    /// Ensure all directories exist
    /// </summary>
    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(Base);
        Directory.CreateDirectory(Extract);
        Directory.CreateDirectory(Refine);
        Directory.CreateDirectory(Chunks);
        Directory.CreateDirectory(Enrich);
        Directory.CreateDirectory(Images);
    }
}

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
    /// Generate default base output directory for a file (filename_output/)
    /// When no custom output is specified, creates folder next to input file.
    /// </summary>
    public static string GetDefaultBaseDirectory(string inputPath)
    {
        var directory = Path.GetDirectoryName(inputPath) ?? ".";
        var fileName = Path.GetFileName(inputPath);
        return Path.Combine(directory, $"{fileName}_output");
    }

    /// <summary>
    /// Generate default output directory for a specific stage
    /// </summary>
    public static string GetDefaultOutputDirectory(string inputPath, string stage)
    {
        return Path.Combine(GetDefaultBaseDirectory(inputPath), stage);
    }

    /// <summary>
    /// Get all stage directories for a file.
    /// If customOutput is specified, creates filename_output subfolder within it.
    /// </summary>
    public static OutputDirectories GetOutputDirectories(string inputPath, string? customOutput = null)
    {
        string baseDir;
        if (customOutput != null)
        {
            // -o specified: create filename_output subfolder within custom path
            var fileName = Path.GetFileName(inputPath);
            baseDir = Path.Combine(customOutput, $"{fileName}_output");
        }
        else
        {
            // No -o: create filename_output next to input file
            baseDir = GetDefaultBaseDirectory(inputPath);
        }

        return new OutputDirectories
        {
            Base = baseDir,
            Extract = Path.Combine(baseDir, "extract"),
            Refine = Path.Combine(baseDir, "refine"),
            Chunks = Path.Combine(baseDir, "chunks"),
            Enrich = Path.Combine(baseDir, "enrich"),
            Images = Path.Combine(baseDir, "images")
        };
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
