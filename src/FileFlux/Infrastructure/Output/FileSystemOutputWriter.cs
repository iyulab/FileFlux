using FileFlux.Core;
using FileFlux.Domain;
using System.Text;
using System.Text.Json;

namespace FileFlux.Infrastructure.Output;

/// <summary>
/// File system based output writer
/// </summary>
public class FileSystemOutputWriter : IOutputWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task WriteExtractionAsync(
        ExtractionResult result,
        string outputDirectory,
        OutputOptions options,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var format = options.Format.ToLowerInvariant();
        var contentPath = Path.Combine(outputDirectory, $"content.{format}");
        var infoPath = Path.Combine(outputDirectory, "info.json");

        // Write content
        if (format == "json")
        {
            await WriteJsonContentAsync(result, contentPath, cancellationToken);
        }
        else
        {
            await WriteMarkdownContentAsync(result, contentPath, cancellationToken);
        }

        // Write info
        await WriteExtractionInfoAsync(result, infoPath, options, cancellationToken);
    }

    public async Task WriteChunkingAsync(
        ChunkingResult result,
        string outputDirectory,
        OutputOptions options,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var format = options.Format.ToLowerInvariant();

        // Write extracted content
        var contentPath = Path.Combine(outputDirectory, $"content.{format}");
        if (format == "json")
        {
            await WriteJsonContentAsync(result.Extraction, contentPath, cancellationToken);
        }
        else
        {
            await WriteMarkdownContentAsync(result.Extraction, contentPath, cancellationToken);
        }

        // Write individual chunks
        await WriteChunksAsync(result.Chunks, outputDirectory, format, cancellationToken);

        // Write info
        var infoPath = Path.Combine(outputDirectory, "info.json");
        await WriteChunkingInfoAsync(result, infoPath, options, cancellationToken);
    }

    private static async Task WriteMarkdownContentAsync(
        ExtractionResult result,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var metadata = result.ParsedContent.Metadata;

        // YAML frontmatter
        sb.AppendLine("---");
        if (!string.IsNullOrEmpty(metadata.Title))
            sb.AppendLine($"title: \"{EscapeYaml(metadata.Title)}\"");

        sb.AppendLine($"source: \"{EscapeYaml(metadata.FileName)}\"");

        if (!string.IsNullOrEmpty(metadata.Author))
            sb.AppendLine($"author: \"{EscapeYaml(metadata.Author)}\"");

        if (metadata.PageCount > 0)
            sb.AppendLine($"pages: {metadata.PageCount}");

        if (metadata.WordCount > 0)
            sb.AppendLine($"words: {metadata.WordCount}");

        if (!string.IsNullOrEmpty(metadata.Language))
            sb.AppendLine($"language: {metadata.Language}");

        sb.AppendLine($"file_type: {metadata.FileType}");
        sb.AppendLine($"file_size: {metadata.FileSize}");
        sb.AppendLine($"processed_at: {metadata.ProcessedAt:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine("---");
        sb.AppendLine();

        sb.AppendLine(result.ProcessedText);

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8, cancellationToken);
    }

    private static async Task WriteJsonContentAsync(
        ExtractionResult result,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var metadata = result.ParsedContent.Metadata;
        var data = new
        {
            metadata = new
            {
                title = metadata.Title,
                source = metadata.FileName,
                author = metadata.Author,
                pages = metadata.PageCount,
                words = metadata.WordCount,
                language = metadata.Language,
                fileType = metadata.FileType,
                fileSize = metadata.FileSize,
                processedAt = metadata.ProcessedAt.ToString("o")
            },
            content = result.ProcessedText,
            statistics = new
            {
                totalCharacters = result.ProcessedText.Length,
                totalWords = CountWords(result.ProcessedText)
            }
        };

        var json = JsonSerializer.Serialize(data, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, Encoding.UTF8, cancellationToken);
    }

    private static async Task WriteExtractionInfoAsync(
        ExtractionResult result,
        string infoPath,
        OutputOptions options,
        CancellationToken cancellationToken)
    {
        var metadata = result.ParsedContent.Metadata;
        var data = new
        {
            command = "extract",
            input = metadata.FileName,
            format = options.Format,
            statistics = new
            {
                totalCharacters = result.ProcessedText.Length,
                totalWords = metadata.WordCount,
                pageCount = metadata.PageCount,
                language = metadata.Language,
                imagesExtracted = result.Images.Count,
                imagesSkipped = result.SkippedImageCount
            },
            aiAnalysis = result.AIProvider != null ? new
            {
                provider = result.AIProvider,
                imagesAnalyzed = result.Images.Count(i => !string.IsNullOrEmpty(i.AIDescription)),
                images = result.Images.Select(i => new
                {
                    fileName = i.FileName,
                    dimensions = $"{i.Width}x{i.Height}",
                    fileSize = i.FileSize,
                    description = i.AIDescription,
                    error = i.AIError
                }).ToArray()
            } : null,
            processedAt = metadata.ProcessedAt.ToString("o")
        };

        var json = JsonSerializer.Serialize(data, JsonOptions);
        await File.WriteAllTextAsync(infoPath, json, Encoding.UTF8, cancellationToken);
    }

    private static async Task WriteChunksAsync(
        DocumentChunk[] chunks,
        string outputDirectory,
        string format,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < chunks.Length; i++)
        {
            var chunk = chunks[i];
            var index = (i + 1).ToString("D3");

            if (format == "md")
            {
                // Write markdown chunk
                var mdPath = Path.Combine(outputDirectory, $"{index}.md");
                var sb = new StringBuilder();
                sb.AppendLine("---");
                sb.AppendLine($"chunk_index: {i + 1}");
                sb.AppendLine($"chunk_id: \"{chunk.Id}\"");
                sb.AppendLine($"start_char: {chunk.Location.StartChar}");
                sb.AppendLine($"end_char: {chunk.Location.EndChar}");
                if (!string.IsNullOrEmpty(chunk.Metadata.Title))
                    sb.AppendLine($"title: \"{EscapeYaml(chunk.Metadata.Title)}\"");
                sb.AppendLine("---");
                sb.AppendLine();
                sb.AppendLine(chunk.Content);
                await File.WriteAllTextAsync(mdPath, sb.ToString(), Encoding.UTF8, cancellationToken);

                // Write JSON metadata (content is in .md file, JSON contains only chunk-specific metadata)
                var jsonPath = Path.Combine(outputDirectory, $"{index}.json");
                var chunkData = BuildChunkJsonData(chunk, i + 1);
                var json = JsonSerializer.Serialize(chunkData, JsonOptions);
                await File.WriteAllTextAsync(jsonPath, json, Encoding.UTF8, cancellationToken);
            }
            else if (format == "json")
            {
                var jsonPath = Path.Combine(outputDirectory, $"{index}.json");
                var chunkData = new
                {
                    index = i + 1,
                    id = chunk.Id,
                    content = chunk.Content,
                    location = new
                    {
                        startChar = chunk.Location.StartChar,
                        endChar = chunk.Location.EndChar
                    },
                    metadata = chunk.Metadata,
                    properties = chunk.Props
                };
                var json = JsonSerializer.Serialize(chunkData, JsonOptions);
                await File.WriteAllTextAsync(jsonPath, json, Encoding.UTF8, cancellationToken);
            }
            else if (format == "jsonl")
            {
                // For jsonl, append to single file
                var jsonlPath = Path.Combine(outputDirectory, "chunks.jsonl");
                var chunkData = new
                {
                    index = i + 1,
                    id = chunk.Id,
                    content = chunk.Content,
                    location = new
                    {
                        startChar = chunk.Location.StartChar,
                        endChar = chunk.Location.EndChar
                    },
                    metadata = chunk.Metadata
                };
                var line = JsonSerializer.Serialize(chunkData, new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                await File.AppendAllTextAsync(jsonlPath, line + Environment.NewLine, Encoding.UTF8, cancellationToken);
            }
        }
    }

    private static async Task WriteChunkingInfoAsync(
        ChunkingResult result,
        string infoPath,
        OutputOptions options,
        CancellationToken cancellationToken)
    {
        var metadata = result.Extraction.ParsedContent.Metadata;
        var chunks = result.Chunks;
        var totalChars = chunks.Sum(c => c.Content.Length);

        // Aggregate document-level summary and keywords from enriched chunks
        var documentAnalysis = AggregateDocumentAnalysis(chunks);

        var data = new
        {
            command = result.Options.Strategy == "Auto" ? "chunk" : "process",
            input = metadata.FileName,
            format = options.Format,
            // Document-level analysis (aggregated from chunks)
            document = documentAnalysis,
            chunkingOptions = new
            {
                strategy = result.Options.Strategy,
                maxChunkSize = result.Options.MaxChunkSize,
                overlapSize = result.Options.OverlapSize
            },
            statistics = new
            {
                chunkCount = chunks.Length,
                totalCharacters = totalChars,
                averageChunkSize = chunks.Length > 0 ? totalChars / chunks.Length : 0,
                minChunkSize = chunks.Length > 0 ? chunks.Min(c => c.Content.Length) : 0,
                maxChunkSize = chunks.Length > 0 ? chunks.Max(c => c.Content.Length) : 0,
                varianceRatio = CalculateVarianceRatio(chunks),
                isBalanced = IsBalanced(chunks, result.Options.MaxChunkSize),
                pageCount = metadata.PageCount,
                language = metadata.Language,
                imagesExtracted = result.Extraction.Images.Count,
                imagesSkipped = result.Extraction.SkippedImageCount,
                enrichedChunks = chunks.Count(c => ChunkPropsKeys.HasEnrichment(c.Props)),
                skippedEnrichments = chunks.Count(c => c.Props.TryGetValue(ChunkPropsKeys.EnrichmentSkipped, out var v) && v is true)
            },
            aiAnalysis = result.Extraction.AIProvider != null ? new
            {
                provider = result.Extraction.AIProvider,
                enrichedChunks = chunks.Count(c => ChunkPropsKeys.HasEnrichment(c.Props)),
                imagesAnalyzed = result.Extraction.Images.Count(i => !string.IsNullOrEmpty(i.AIDescription))
            } : null,
            processedAt = metadata.ProcessedAt.ToString("o")
        };

        var json = JsonSerializer.Serialize(data, JsonOptions);
        await File.WriteAllTextAsync(infoPath, json, Encoding.UTF8, cancellationToken);
    }

    /// <summary>
    /// Aggregates document-level analysis from enriched chunks.
    /// Extracts topics, keywords, and generates a document summary.
    /// </summary>
    private static object? AggregateDocumentAnalysis(DocumentChunk[] chunks)
    {
        if (chunks.Length == 0) return null;

        // Collect all keywords from enriched chunks
        var allKeywords = new List<string>();
        var allTopics = new List<string>();
        var summaries = new List<string>();

        foreach (var chunk in chunks)
        {
            // Extract keywords
            if (chunk.Props.TryGetValue(ChunkPropsKeys.EnrichedKeywords, out var keywordsObj))
            {
                if (keywordsObj is IEnumerable<object> keywordList)
                {
                    allKeywords.AddRange(keywordList.Select(k => k?.ToString() ?? "").Where(k => !string.IsNullOrEmpty(k)));
                }
                else if (keywordsObj is string keywordStr)
                {
                    allKeywords.AddRange(keywordStr.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()));
                }
            }

            // Extract document keywords
            if (chunk.Props.TryGetValue(ChunkPropsKeys.DocumentKeywords, out var docKeywordsObj))
            {
                if (docKeywordsObj is IEnumerable<object> docKeywordList)
                {
                    allKeywords.AddRange(docKeywordList.Select(k => k?.ToString() ?? "").Where(k => !string.IsNullOrEmpty(k)));
                }
            }

            // Extract topics
            if (chunk.Props.TryGetValue(ChunkPropsKeys.DocumentTopic, out var topicObj) && topicObj is string topic)
            {
                allTopics.Add(topic);
            }

            // Collect summaries for document-level synthesis
            if (chunk.Props.TryGetValue(ChunkPropsKeys.EnrichedSummary, out var summaryObj) && summaryObj is string summary)
            {
                summaries.Add(summary);
            }
        }

        // Deduplicate and rank keywords by frequency
        var keywordCounts = allKeywords
            .GroupBy(k => k.ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .Take(20)
            .Select(g => g.First())
            .ToList();

        // Deduplicate topics
        var uniqueTopics = allTopics
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        // Create document summary from first chunk summary or title
        var documentSummary = summaries.FirstOrDefault();
        if (string.IsNullOrEmpty(documentSummary) && chunks.Length > 0)
        {
            // Fallback: use first 200 chars of first chunk content
            var firstContent = chunks[0].Content;
            documentSummary = firstContent.Length > 200
                ? firstContent.Substring(0, 200) + "..."
                : firstContent;
        }

        if (keywordCounts.Count == 0 && uniqueTopics.Count == 0 && string.IsNullOrEmpty(documentSummary))
            return null;

        return new
        {
            summary = documentSummary,
            topics = uniqueTopics.Count > 0 ? uniqueTopics : null,
            keywords = keywordCounts.Count > 0 ? keywordCounts : null
        };
    }


    /// <summary>
    /// Builds chunk JSON data with cleaned-up metadata structure.
    /// Removes file-level metadata duplicates and organizes enrichment data.
    /// </summary>
    private static object BuildChunkJsonData(DocumentChunk chunk, int index)
    {
        // Extract enrichment data separately for cleaner structure
        var enrichment = ExtractEnrichmentData(chunk.Props);

        // Build location with optional heading path
        var location = new Dictionary<string, object>
        {
            ["startChar"] = chunk.Location.StartChar,
            ["endChar"] = chunk.Location.EndChar
        };

        if (chunk.Location.HeadingPath?.Count > 0)
        {
            location["headingPath"] = chunk.Location.HeadingPath;
        }

        if (!string.IsNullOrEmpty(chunk.Location.Section))
        {
            location["section"] = chunk.Location.Section;
        }

        // Build quality metrics if present
        var quality = ExtractQualityMetrics(chunk);

        // Build the clean chunk data structure
        var result = new Dictionary<string, object?>
        {
            ["index"] = index,
            ["id"] = chunk.Id,
            ["length"] = chunk.Content.Length,
            ["tokens"] = chunk.Tokens > 0 ? chunk.Tokens : null,
            ["location"] = location,
            ["quality"] = quality,
            ["enrichment"] = enrichment
        };

        // Remove null values for cleaner JSON
        return result.Where(kvp => kvp.Value != null).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Extracts enrichment data from chunk properties into a structured object.
    /// </summary>
    private static object? ExtractEnrichmentData(IDictionary<string, object> props)
    {
        var enrichment = new Dictionary<string, object?>();

        if (props.TryGetValue(ChunkPropsKeys.EnrichedSummary, out var summary))
            enrichment["summary"] = summary;

        if (props.TryGetValue(ChunkPropsKeys.EnrichedKeywords, out var keywords))
            enrichment["keywords"] = keywords;

        if (props.TryGetValue(ChunkPropsKeys.DocumentTopic, out var topic))
            enrichment["topic"] = topic;

        if (props.TryGetValue(ChunkPropsKeys.HierarchyPath, out var path))
            enrichment["hierarchyPath"] = path;

        if (props.TryGetValue(ChunkPropsKeys.EnrichedContextualText, out var contextual))
            enrichment["contextualText"] = contextual;

        return enrichment.Count > 0 ? enrichment : null;
    }

    /// <summary>
    /// Extracts quality metrics from chunk into a structured object.
    /// </summary>
    private static object? ExtractQualityMetrics(DocumentChunk chunk)
    {
        var quality = new Dictionary<string, object?>();

        if (chunk.Quality > 0)
            quality["overall"] = Math.Round(chunk.Quality, 3);

        if (chunk.Density > 0)
            quality["density"] = Math.Round(chunk.Density, 3);

        if (chunk.Importance > 0)
            quality["importance"] = Math.Round(chunk.Importance, 3);

        if (chunk.Props.TryGetValue(ChunkPropsKeys.QualitySemanticCompleteness, out var semantic))
            quality["semanticCompleteness"] = semantic;

        if (chunk.Props.TryGetValue(ChunkPropsKeys.QualityContextIndependence, out var independence))
            quality["contextIndependence"] = independence;

        return quality.Count > 0 ? quality : null;
    }

    /// <summary>
    /// Calculate the variance ratio (max/min) for chunk sizes.
    /// </summary>
    private static double CalculateVarianceRatio(DocumentChunk[] chunks)
    {
        if (chunks.Length == 0) return 0;
        var min = chunks.Min(c => c.Content.Length);
        var max = chunks.Max(c => c.Content.Length);
        return min > 0 ? Math.Round((double)max / min, 2) : 0;
    }

    /// <summary>
    /// Check if chunks are well-balanced (variance ratio &lt;= 5 and no extreme sizes).
    /// </summary>
    private static bool IsBalanced(DocumentChunk[] chunks, int maxChunkSize)
    {
        if (chunks.Length == 0) return true;
        var minSize = maxChunkSize / 10; // 10% of max as minimum threshold
        var min = chunks.Min(c => c.Content.Length);
        var max = chunks.Max(c => c.Content.Length);
        var ratio = min > 0 ? (double)max / min : 0;
        return ratio <= 5.0 && min >= minSize && max <= maxChunkSize * 1.5;
    }

    private static string EscapeYaml(string value)
    {
        return value.Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        return text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
