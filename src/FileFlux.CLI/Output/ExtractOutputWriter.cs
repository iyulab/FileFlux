using FileFlux.Domain;
using System.Text;
using System.Text.RegularExpressions;

namespace FileFlux.CLI.Output;

/// <summary>
/// Output writer for extract command - writes full parsed document without chunking
/// </summary>
public class ExtractOutputWriter
{
    private readonly string _format;
    private readonly bool _extractImages;
    private readonly string? _imagesDirectory;
    private readonly int _minImageSize;
    private readonly bool _verbose;

    public ExtractOutputWriter(string format = "md", bool extractImages = false, string? imagesDirectory = null, int minImageSize = 5000, bool verbose = false)
    {
        _format = format.ToLowerInvariant();
        _extractImages = extractImages;
        _imagesDirectory = imagesDirectory;
        _minImageSize = minImageSize;
        _verbose = verbose;
    }

    public string Extension => _format switch
    {
        "json" => ".json",
        _ => ".md"
    };

    /// <summary>
    /// Write parsed content directly without chunking
    /// </summary>
    public async Task WriteAsync(ParsedContent parsedContent, string outputPath, CancellationToken cancellationToken = default)
    {
        if (_format == "json")
        {
            await WriteJsonAsync(parsedContent, outputPath, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await WriteMarkdownAsync(parsedContent, outputPath, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteMarkdownAsync(ParsedContent parsedContent, string outputPath, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var metadata = parsedContent.Metadata;

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
        {
            sb.AppendLine($"language: {metadata.Language}");
            if (metadata.LanguageConfidence > 0)
                sb.AppendLine($"language_confidence: {metadata.LanguageConfidence:F2}");
        }

        sb.AppendLine($"file_type: {metadata.FileType}");
        sb.AppendLine($"file_size: {metadata.FileSize}");

        if (metadata.CreatedAt.HasValue)
            sb.AppendLine($"created_at: {metadata.CreatedAt.Value:yyyy-MM-dd}");

        if (metadata.ModifiedAt.HasValue)
            sb.AppendLine($"modified_at: {metadata.ModifiedAt.Value:yyyy-MM-dd}");

        sb.AppendLine($"processed_at: {metadata.ProcessedAt:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine("---");
        sb.AppendLine();

        // Get full content
        var content = parsedContent.Text;

        if (_verbose)
        {
            var base64Count = Regex.Matches(content, @"data:image/\w+;base64,").Count;
            Console.WriteLine($"[Verbose] Content: {content.Length} chars, {base64Count} base64 images detected");
        }

        // Extract and replace base64 images if enabled
        string? imagesDir = null;
        if (_extractImages && !string.IsNullOrEmpty(_imagesDirectory))
        {
            imagesDir = _imagesDirectory;
            if (_verbose)
            {
                Console.WriteLine($"[Verbose] Images directory: {imagesDir}");
            }

            var result = await ExtractBase64ImagesAsync(content, imagesDir, 0, _minImageSize, _verbose, cancellationToken).ConfigureAwait(false);
            content = result.Content;
        }
        else
        {
            // Remove base64 images from content for cleaner output
            content = RemoveBase64Images(content);
        }

        sb.AppendLine(content);

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteJsonAsync(ParsedContent parsedContent, string outputPath, CancellationToken cancellationToken)
    {
        var metadata = parsedContent.Metadata;
        var fullContent = RemoveBase64Images(parsedContent.Text);

        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

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
                languageConfidence = metadata.LanguageConfidence,
                fileType = metadata.FileType,
                fileSize = metadata.FileSize,
                createdAt = metadata.CreatedAt?.ToString("o"),
                modifiedAt = metadata.ModifiedAt?.ToString("o"),
                processedAt = metadata.ProcessedAt.ToString("o")
            },
            content = fullContent,
            statistics = new
            {
                totalCharacters = fullContent.Length,
                totalWords = CountWords(fullContent)
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(data, options);
        await File.WriteAllTextAsync(outputPath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(string Content, int NextIndex)> ExtractBase64ImagesAsync(string content, string imagesDir, int imageIndex, int minImageSize, bool verbose, CancellationToken cancellationToken)
    {
        // Match base64 image patterns - use atomic group to prevent catastrophic backtracking with long base64 data
        var pattern = @"!?\[([^\]]*)\]\(data:image/(\w+);base64,((?>[A-Za-z0-9+/\s]+)=*)\)";
        var regex = new Regex(pattern, RegexOptions.Compiled);

        var result = content;
        var matches = regex.Matches(content);
        var currentIndex = imageIndex;
        var skippedCount = 0;

        if (verbose)
        {
            Console.WriteLine($"[Verbose] Regex pattern: {pattern}");
            Console.WriteLine($"[Verbose] Found {matches.Count} matches, min size filter: {minImageSize} bytes");
        }

        // Get relative directory name for path references
        var dirName = Path.GetFileName(imagesDir);

        foreach (Match match in matches)
        {
            var altText = match.Groups[1].Value;
            var imageFormat = match.Groups[2].Value;
            var base64Data = match.Groups[3].Value;

            // Remove any whitespace from base64 data before decoding
            var cleanBase64 = Regex.Replace(base64Data, @"\s", "");

            // Estimate actual image size (base64 is ~33% larger than binary)
            var estimatedSize = (int)(cleanBase64.Length * 0.75);

            // Skip small images (icons, decorations)
            if (estimatedSize < minImageSize)
            {
                skippedCount++;
                if (verbose)
                {
                    Console.WriteLine($"[Verbose] Skipped: [{altText}] format={imageFormat} size={estimatedSize} bytes (below {minImageSize})");
                }
                // Remove from content completely
                result = result.Replace(match.Value, "");
                continue;
            }

            if (verbose)
            {
                Console.WriteLine($"[Verbose] Extracting: [{altText}] format={imageFormat} size={estimatedSize} bytes");
            }

            currentIndex++;
            var fileName = $"image_{currentIndex}.{imageFormat}";
            var filePath = Path.Combine(imagesDir, fileName);

            try
            {
                // Create directory on first image extraction
                if (!Directory.Exists(imagesDir))
                {
                    Directory.CreateDirectory(imagesDir);
                }

                var imageBytes = Convert.FromBase64String(cleanBase64);
                await File.WriteAllBytesAsync(filePath, imageBytes, cancellationToken).ConfigureAwait(false);

                // Replace with relative path reference using actual directory name
                var relativePath = $"./{dirName}/{fileName}";
                var replacement = string.IsNullOrEmpty(altText)
                    ? $"![Image {currentIndex}]({relativePath})"
                    : $"![{altText}]({relativePath})";

                result = result.Replace(match.Value, replacement);
            }
            catch (Exception ex)
            {
                // If extraction fails, replace with placeholder
                result = result.Replace(match.Value, $"[Image {currentIndex}: {ex.Message}]");
            }
        }

        if (verbose && skippedCount > 0)
        {
            Console.WriteLine($"[Verbose] Skipped {skippedCount} small images (icons/decorations)");
        }

        return (result, currentIndex);
    }

    private static string RemoveBase64Images(string content)
    {
        // Remove base64 image data but keep reference - use atomic group
        var pattern = @"!?\[([^\]]*)\]\(data:image/\w+;base64,(?>[A-Za-z0-9+/\s]+)=*\)";
        var regex = new Regex(pattern, RegexOptions.Compiled);

        var index = 0;
        return regex.Replace(content, match =>
        {
            index++;
            var altText = match.Groups[1].Value;
            return string.IsNullOrEmpty(altText)
                ? $"[Image {index}]"
                : $"[Image: {altText}]";
        });
    }

    private static string EscapeYaml(string value)
    {
        return value.Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
