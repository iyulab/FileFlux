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

                // Write JSON metadata (content is in .md file, JSON contains only metadata)
                var jsonPath = Path.Combine(outputDirectory, $"{index}.json");
                var chunkData = new
                {
                    index = i + 1,
                    id = chunk.Id,
                    length = chunk.Content.Length,
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

        var data = new
        {
            command = result.Options.Strategy == "Auto" ? "chunk" : "process",
            input = metadata.FileName,
            format = options.Format,
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
                pageCount = metadata.PageCount,
                language = metadata.Language,
                imagesExtracted = result.Extraction.Images.Count,
                imagesSkipped = result.Extraction.SkippedImageCount
            },
            aiAnalysis = result.Extraction.AIProvider != null ? new
            {
                provider = result.Extraction.AIProvider,
                enrichedChunks = chunks.Count(c => c.Metadata.CustomProperties.ContainsKey("enriched_keywords")),
                imagesAnalyzed = result.Extraction.Images.Count(i => !string.IsNullOrEmpty(i.AIDescription))
            } : null,
            processedAt = metadata.ProcessedAt.ToString("o")
        };

        var json = JsonSerializer.Serialize(data, JsonOptions);
        await File.WriteAllTextAsync(infoPath, json, Encoding.UTF8, cancellationToken);
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
