using FileFlux.Core;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileFlux.CLI.Output;

/// <summary>
/// Writes processing metadata info file alongside main output
/// </summary>
public static class ProcessingInfoWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Write processing info JSON file
    /// </summary>
    public static async Task WriteInfoAsync(
        string outputPath,
        string inputPath,
        IEnumerable<DocumentChunk> chunks,
        ProcessingInfo info,
        CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        var infoPath = GetInfoPath(outputPath);

        // Get first chunk for document-level metadata
        var firstChunk = chunkList.FirstOrDefault();
        var sourceInfo = firstChunk?.SourceInfo;
        var metadata = firstChunk?.Metadata;

        var infoData = new
        {
            timestamp = DateTime.UtcNow.ToString("o"),
            input = new
            {
                path = Path.GetFileName(inputPath),
                fullPath = inputPath,
                size = new FileInfo(inputPath).Length
            },
            output = new
            {
                path = Path.GetFileName(outputPath),
                format = info.Format
            },
            processing = new
            {
                command = info.Command,
                strategy = info.Strategy,
                maxChunkSize = info.MaxChunkSize,
                overlapSize = info.OverlapSize,
                aiProvider = info.AIProvider,
                enrichmentEnabled = info.EnrichmentEnabled
            },
            document = metadata != null ? new
            {
                fileName = metadata.FileName,
                fileType = metadata.FileType,
                title = metadata.Title,
                author = metadata.Author,
                language = metadata.Language,
                languageConfidence = metadata.LanguageConfidence,
                pageCount = metadata.PageCount,
                wordCount = metadata.WordCount,
                createdAt = metadata.CreatedAt?.ToString("o"),
                modifiedAt = metadata.ModifiedAt?.ToString("o"),
                processedAt = metadata.ProcessedAt.ToString("o")
            } : null,
            source = sourceInfo != null ? new
            {
                sourceId = sourceInfo.SourceId,
                sourceType = sourceInfo.SourceType,
                title = sourceInfo.Title,
                language = sourceInfo.Language,
                languageConfidence = sourceInfo.LanguageConfidence,
                wordCount = sourceInfo.WordCount,
                pageCount = sourceInfo.PageCount
            } : null,
            statistics = new
            {
                totalChunks = chunkList.Count,
                totalCharacters = chunkList.Sum(c => c.Content.Length),
                totalTokens = chunkList.Sum(c => c.Tokens),
                averageChunkSize = chunkList.Count > 0 ? chunkList.Sum(c => c.Content.Length) / chunkList.Count : 0,
                minChunkSize = chunkList.Count > 0 ? chunkList.Min(c => c.Content.Length) : 0,
                maxChunkSize = chunkList.Count > 0 ? chunkList.Max(c => c.Content.Length) : 0,
                varianceRatio = CalculateVarianceRatio(chunkList),
                isBalanced = IsBalanced(chunkList, info.MaxChunkSize ?? 1024),
                enrichedChunks = chunkList.Count(c => ChunkPropsKeys.HasEnrichment(c.Props)),
                skippedEnrichments = chunkList.Count(c => c.Props.TryGetValue(ChunkPropsKeys.EnrichmentSkipped, out var v) && v is true)
            },
            quality = chunkList.Count > 0 ? new
            {
                averageQuality = chunkList.Average(c => c.Quality),
                averageImportance = chunkList.Average(c => c.Importance),
                averageDensity = chunkList.Average(c => c.Density),
                averageContextDependency = chunkList.Average(c => c.ContextDependency)
            } : null,
            version = typeof(ProcessingInfoWriter).Assembly.GetName().Version?.ToString() ?? "unknown"
        };

        var json = JsonSerializer.Serialize(infoData, JsonOptions);
        await File.WriteAllTextAsync(infoPath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Calculate the variance ratio (max/min) for chunk sizes.
    /// </summary>
    private static double CalculateVarianceRatio(List<DocumentChunk> chunks)
    {
        if (chunks.Count == 0) return 0;
        var min = chunks.Min(c => c.Content.Length);
        var max = chunks.Max(c => c.Content.Length);
        return min > 0 ? Math.Round((double)max / min, 2) : 0;
    }

    /// <summary>
    /// Check if chunks are well-balanced (variance ratio <= 5 and no extreme sizes).
    /// </summary>
    private static bool IsBalanced(List<DocumentChunk> chunks, int maxChunkSize)
    {
        if (chunks.Count == 0) return true;
        var minSize = maxChunkSize / 10; // 10% of max as minimum threshold
        var min = chunks.Min(c => c.Content.Length);
        var max = chunks.Max(c => c.Content.Length);
        var ratio = min > 0 ? (double)max / min : 0;
        return ratio <= 5.0 && min >= minSize && max <= maxChunkSize * 1.5;
    }

    /// <summary>
    /// Get the info file path from output path
    /// </summary>
    public static string GetInfoPath(string outputPath)
    {
        // If outputPath is a directory (for chunked output), put info.json inside it
        if (Directory.Exists(outputPath))
        {
            return Path.Combine(outputPath, "info.json");
        }

        var dir = Path.GetDirectoryName(outputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(dir, $"{name}.info.json");
    }

    /// <summary>
    /// Write processing info for directory-based chunked output
    /// </summary>
    public static async Task WriteChunkedInfoAsync(
        string outputDir,
        string inputPath,
        IEnumerable<DocumentChunk> chunks,
        ProcessingInfo info,
        CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        var infoPath = Path.Combine(outputDir, "info.json");

        // Get first chunk for document-level metadata
        var firstChunk = chunkList.FirstOrDefault();
        var sourceInfo = firstChunk?.SourceInfo;
        var metadata = firstChunk?.Metadata;

        // Create chunk manifest
        var chunkManifest = chunkList.Select(c => new
        {
            index = c.Index,
            file = $"chunk_{c.Index + 1}{(info.Format == "json" ? ".json" : ".md")}",
            characters = c.Content.Length,
            tokens = c.Tokens,
            quality = c.Quality
        }).ToList();

        var infoData = new
        {
            timestamp = DateTime.UtcNow.ToString("o"),
            input = new
            {
                path = Path.GetFileName(inputPath),
                fullPath = inputPath,
                size = new FileInfo(inputPath).Length
            },
            output = new
            {
                directory = outputDir,
                format = info.Format,
                chunks = chunkManifest
            },
            processing = new
            {
                command = info.Command,
                strategy = info.Strategy,
                maxChunkSize = info.MaxChunkSize,
                overlapSize = info.OverlapSize,
                aiProvider = info.AIProvider,
                enrichmentEnabled = info.EnrichmentEnabled
            },
            document = metadata != null ? new
            {
                fileName = metadata.FileName,
                fileType = metadata.FileType,
                title = metadata.Title,
                author = metadata.Author,
                language = metadata.Language,
                languageConfidence = metadata.LanguageConfidence,
                pageCount = metadata.PageCount,
                wordCount = metadata.WordCount,
                createdAt = metadata.CreatedAt?.ToString("o"),
                modifiedAt = metadata.ModifiedAt?.ToString("o"),
                processedAt = metadata.ProcessedAt.ToString("o")
            } : null,
            source = sourceInfo != null ? new
            {
                sourceId = sourceInfo.SourceId,
                sourceType = sourceInfo.SourceType,
                title = sourceInfo.Title,
                language = sourceInfo.Language,
                languageConfidence = sourceInfo.LanguageConfidence,
                wordCount = sourceInfo.WordCount,
                pageCount = sourceInfo.PageCount
            } : null,
            statistics = new
            {
                totalChunks = chunkList.Count,
                totalCharacters = chunkList.Sum(c => c.Content.Length),
                totalTokens = chunkList.Sum(c => c.Tokens),
                averageChunkSize = chunkList.Count > 0 ? chunkList.Sum(c => c.Content.Length) / chunkList.Count : 0,
                minChunkSize = chunkList.Count > 0 ? chunkList.Min(c => c.Content.Length) : 0,
                maxChunkSize = chunkList.Count > 0 ? chunkList.Max(c => c.Content.Length) : 0,
                varianceRatio = CalculateVarianceRatio(chunkList),
                isBalanced = IsBalanced(chunkList, info.MaxChunkSize ?? 1024),
                enrichedChunks = chunkList.Count(c => ChunkPropsKeys.HasEnrichment(c.Props)),
                skippedEnrichments = chunkList.Count(c => c.Props.TryGetValue(ChunkPropsKeys.EnrichmentSkipped, out var v) && v is true)
            },
            quality = chunkList.Count > 0 ? new
            {
                averageQuality = chunkList.Average(c => c.Quality),
                averageImportance = chunkList.Average(c => c.Importance),
                averageDensity = chunkList.Average(c => c.Density),
                averageContextDependency = chunkList.Average(c => c.ContextDependency)
            } : null,
            version = typeof(ProcessingInfoWriter).Assembly.GetName().Version?.ToString() ?? "unknown"
        };

        var json = JsonSerializer.Serialize(infoData, JsonOptions);
        await File.WriteAllTextAsync(infoPath, json, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Processing information for info file
/// </summary>
public record ProcessingInfo
{
    public required string Command { get; init; }
    public required string Format { get; init; }
    public string? Strategy { get; init; }
    public int? MaxChunkSize { get; init; }
    public int? OverlapSize { get; init; }
    public string? AIProvider { get; init; }
    public bool EnrichmentEnabled { get; init; }
}
