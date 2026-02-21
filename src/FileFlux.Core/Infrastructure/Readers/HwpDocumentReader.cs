using System.Text.RegularExpressions;
using FileFlux.Core;
using Unhwp;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// HWP/HWPX document reader using Unhwp (Rust FFI).
/// High-performance native library for Korean word processor document extraction.
/// </summary>
public sealed partial class HwpDocumentReader : IDocumentReader
{
    public string ReaderType => "HwpReader";

    public IEnumerable<string> SupportedExtensions => [".hwp", ".hwpx"];

    public bool CanRead(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension is ".hwp" or ".hwpx";
    }

    // ========================================
    // Stage 0: Read (Document Structure)
    // ========================================

    public async Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"HWP document not found: {filePath}");

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        var startTime = DateTime.UtcNow;
        var fileInfo = new FileInfo(filePath);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        try
        {
            var result = new ReadResult
            {
                File = new SourceFileInfo
                {
                    Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                    Extension = extension,
                    Size = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    ModifiedAt = fileInfo.LastWriteTimeUtc
                },
                ReaderType = ReaderType
            };

            result.DocumentProps["hwp_format"] = extension == ".hwpx" ? "HWPX" : "HWP5";
            result.DocumentProps["file_type"] = "hwp_document";

            using var doc = UnhwpDocument.ParseFile(filePath);
            result.DocumentProps["section_count"] = doc.SectionCount;
            result.DocumentProps["resource_count"] = doc.ResourceCount;

            // HWP documents are treated as single logical page
            result.Pages.Add(new PageInfo
            {
                Number = 1,
                HasContent = fileInfo.Length > 0,
                Props =
                {
                    ["file_type"] = "hwp_document",
                    ["format"] = extension == ".hwpx" ? "HWPX" : "HWP5"
                }
            });

            result.Duration = DateTime.UtcNow - startTime;
            return await Task.FromResult(result).ConfigureAwait(false);
        }
        catch (UnhwpException ex)
        {
            throw new DocumentProcessingException(filePath, $"Failed to read HWP document: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to read HWP document: {ex.Message}", ex);
        }
    }

    public async Task<ReadResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!CanRead(fileName))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        var startTime = DateTime.UtcNow;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        try
        {
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            var bytes = memoryStream.ToArray();

            var result = new ReadResult
            {
                File = new SourceFileInfo
                {
                    Name = fileName,
                    Extension = extension,
                    Size = bytes.Length,
                    CreatedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow
                },
                ReaderType = ReaderType
            };

            result.DocumentProps["hwp_format"] = extension == ".hwpx" ? "HWPX" : "HWP5";
            result.DocumentProps["file_type"] = "hwp_document";

            using var doc = UnhwpDocument.ParseBytes(bytes);
            result.DocumentProps["section_count"] = doc.SectionCount;
            result.DocumentProps["resource_count"] = doc.ResourceCount;

            result.Pages.Add(new PageInfo
            {
                Number = 1,
                HasContent = bytes.Length > 0,
                Props = { ["file_type"] = "hwp_document" }
            });

            result.Duration = DateTime.UtcNow - startTime;
            return result;
        }
        catch (UnhwpException ex)
        {
            throw new DocumentProcessingException(fileName, $"Failed to read HWP document from stream: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(fileName, $"Failed to read HWP document from stream: {ex.Message}", ex);
        }
    }

    // ========================================
    // Stage 1: Extract (Raw Content)
    // ========================================

    public async Task<RawContent> ExtractAsync(string filePath, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"HWP document not found: {filePath}");

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        try
        {
            return await Task.Run(() => ExtractHwpContent(filePath, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (UnhwpException ex)
        {
            throw new DocumentProcessingException(filePath, $"Failed to extract HWP document: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to extract HWP document: {ex.Message}", ex);
        }
    }

    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!CanRead(fileName))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        try
        {
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            var bytes = memoryStream.ToArray();

            return await Task.Run(() => ExtractHwpContentFromBytes(bytes, fileName, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (UnhwpException ex)
        {
            throw new DocumentProcessingException(fileName, $"Failed to extract HWP document from stream: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(fileName, $"Failed to extract HWP document from stream: {ex.Message}", ex);
        }
    }

    private static RawContent ExtractHwpContent(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var extractedImages = new List<ImageInfo>();

        // Parse and convert to Markdown using Unhwp native library
        using var doc = UnhwpDocument.ParseFile(filePath);
        var markdown = doc.ToMarkdown(new MarkdownOptions
        {
            IncludeFrontmatter = false,
            EscapeSpecialChars = false,
            ParagraphSpacing = false
        });

        // Remove null bytes
        markdown = TextSanitizer.RemoveNullBytes(markdown);

        structuralHints["hwp_format"] = extension == ".hwpx" ? "HWPX" : "HWP5";
        structuralHints["section_count"] = doc.SectionCount;

        // Extract embedded resources (images)
        var resourceIds = doc.GetResourceIds();
        foreach (var id in resourceIds)
        {
            var data = doc.GetResourceData(id);
            if (data != null)
            {
                var imageInfo = new ImageInfo
                {
                    Id = id,
                    MimeType = GuessMimeType(id),
                    Data = data,
                    OriginalSize = data.Length,
                    SourceUrl = $"embedded:{id}"
                };
                extractedImages.Add(imageInfo);
            }
        }

        structuralHints["file_type"] = "hwp_document";
        structuralHints["character_count"] = markdown.Length;
        structuralHints["word_count"] = CountWords(markdown);
        structuralHints["conversion_method"] = "unhwp_native";

        if (extractedImages.Count > 0)
        {
            structuralHints["image_count"] = extractedImages.Count;
            structuralHints["has_images"] = true;
        }

        // Detect structural elements
        var hasHeaders = HeaderRegex().IsMatch(markdown);
        var hasTables = markdown.Contains("|---", StringComparison.Ordinal) ||
                       markdown.Contains("| ---", StringComparison.Ordinal);
        var hasLists = ListRegex().IsMatch(markdown);

        if (hasHeaders) structuralHints["has_headers"] = true;
        if (hasTables) structuralHints["has_tables"] = true;
        if (hasLists) structuralHints["has_lists"] = true;

        return new RawContent
        {
            Text = markdown.Trim(),
            File = new SourceFileInfo
            {
                Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                Extension = extension,
                Size = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc,
                ModifiedAt = fileInfo.LastWriteTimeUtc
            },
            Hints = structuralHints,
            Warnings = warnings,
            Images = extractedImages,
            ReaderType = "HwpReader"
        };
    }

    private static RawContent ExtractHwpContentFromBytes(byte[] bytes, string fileName, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var extractedImages = new List<ImageInfo>();

        // Parse and convert to Markdown using Unhwp native library
        using var doc = UnhwpDocument.ParseBytes(bytes);
        var markdown = doc.ToMarkdown(new MarkdownOptions
        {
            IncludeFrontmatter = false,
            EscapeSpecialChars = false,
            ParagraphSpacing = false
        });

        // Remove null bytes
        markdown = TextSanitizer.RemoveNullBytes(markdown);

        structuralHints["hwp_format"] = extension == ".hwpx" ? "HWPX" : "HWP5";
        structuralHints["section_count"] = doc.SectionCount;

        // Extract embedded resources (images)
        var resourceIds = doc.GetResourceIds();
        foreach (var id in resourceIds)
        {
            var data = doc.GetResourceData(id);
            if (data != null)
            {
                var imageInfo = new ImageInfo
                {
                    Id = id,
                    MimeType = GuessMimeType(id),
                    Data = data,
                    OriginalSize = data.Length,
                    SourceUrl = $"embedded:{id}"
                };
                extractedImages.Add(imageInfo);
            }
        }

        structuralHints["file_type"] = "hwp_document";
        structuralHints["character_count"] = markdown.Length;
        structuralHints["word_count"] = CountWords(markdown);
        structuralHints["conversion_method"] = "unhwp_native";

        if (extractedImages.Count > 0)
        {
            structuralHints["image_count"] = extractedImages.Count;
            structuralHints["has_images"] = true;
        }

        // Detect structural elements
        var hasHeaders = HeaderRegex().IsMatch(markdown);
        var hasTables = markdown.Contains("|---", StringComparison.Ordinal) ||
                       markdown.Contains("| ---", StringComparison.Ordinal);
        var hasLists = ListRegex().IsMatch(markdown);

        if (hasHeaders) structuralHints["has_headers"] = true;
        if (hasTables) structuralHints["has_tables"] = true;
        if (hasLists) structuralHints["has_lists"] = true;

        return new RawContent
        {
            Text = markdown.Trim(),
            File = new SourceFileInfo
            {
                Name = fileName,
                Extension = extension,
                Size = bytes.Length,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            },
            Hints = structuralHints,
            Warnings = warnings,
            Images = extractedImages,
            ReaderType = "HwpReader"
        };
    }

    private static string GuessMimeType(string resourceId)
    {
        var lower = resourceId.ToLowerInvariant();
        if (lower.EndsWith(".png")) return "image/png";
        if (lower.EndsWith(".jpg") || lower.EndsWith(".jpeg")) return "image/jpeg";
        if (lower.EndsWith(".gif")) return "image/gif";
        if (lower.EndsWith(".webp")) return "image/webp";
        if (lower.EndsWith(".bmp")) return "image/bmp";
        return "application/octet-stream";
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        return text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
    }

    // Generated regex patterns for performance
    [GeneratedRegex(@"^#{1,6}\s", RegexOptions.Multiline)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"^[\*\-\+]\s|^\d+\.\s", RegexOptions.Multiline)]
    private static partial Regex ListRegex();
}
