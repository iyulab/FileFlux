using System.Text.RegularExpressions;
using FileFlux.Core;
using Undoc;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Microsoft Word document (.docx) reader using Undoc (Rust FFI).
/// High-performance native library for Office document extraction.
/// </summary>
public partial class WordDocumentReader : IDocumentReader
{
    public string ReaderType => "WordReader";

    public IEnumerable<string> SupportedExtensions => [".docx"];

    public bool CanRead(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension == ".docx";
    }

    // ========================================
    // Stage 0: Read (Document Structure)
    // ========================================

    public async Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Word document not found: {filePath}");

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        var startTime = DateTime.UtcNow;
        var fileInfo = new FileInfo(filePath);

        try
        {
            var result = new ReadResult
            {
                File = new SourceFileInfo
                {
                    Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                    Extension = ".docx",
                    Size = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    ModifiedAt = fileInfo.LastWriteTimeUtc
                },
                ReaderType = ReaderType
            };

            using var doc = UndocDocument.ParseFile(filePath);

            if (!string.IsNullOrWhiteSpace(doc.Title))
                result.DocumentProps["title"] = doc.Title;
            if (!string.IsNullOrWhiteSpace(doc.Author))
                result.DocumentProps["author"] = doc.Author;

            result.DocumentProps["section_count"] = doc.SectionCount;
            result.DocumentProps["resource_count"] = doc.ResourceCount;

            result.Pages.Add(new PageInfo
            {
                Number = 1,
                HasContent = fileInfo.Length > 0,
                Props = { ["file_type"] = "word_document" }
            });

            result.Duration = DateTime.UtcNow - startTime;
            return await Task.FromResult(result).ConfigureAwait(false);
        }
        catch (UndocException ex)
        {
            throw new DocumentProcessingException(filePath, $"Failed to read Word document: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to read Word document: {ex.Message}", ex);
        }
    }

    public async Task<ReadResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!CanRead(fileName))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        var startTime = DateTime.UtcNow;

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
                    Extension = ".docx",
                    Size = bytes.Length,
                    CreatedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow
                },
                ReaderType = ReaderType
            };

            using var doc = UndocDocument.ParseBytes(bytes);

            if (!string.IsNullOrWhiteSpace(doc.Title))
                result.DocumentProps["title"] = doc.Title;
            if (!string.IsNullOrWhiteSpace(doc.Author))
                result.DocumentProps["author"] = doc.Author;

            result.DocumentProps["section_count"] = doc.SectionCount;
            result.DocumentProps["resource_count"] = doc.ResourceCount;

            result.Pages.Add(new PageInfo
            {
                Number = 1,
                HasContent = bytes.Length > 0,
                Props = { ["file_type"] = "word_document" }
            });

            result.Duration = DateTime.UtcNow - startTime;
            return result;
        }
        catch (UndocException ex)
        {
            throw new DocumentProcessingException(fileName, $"Failed to read Word document from stream: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(fileName, $"Failed to read Word document from stream: {ex.Message}", ex);
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
            throw new FileNotFoundException($"Word document not found: {filePath}");

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        try
        {
            return await Task.Run(() => ExtractWordContent(filePath, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (UndocException ex)
        {
            throw new DocumentProcessingException(filePath, $"Failed to extract Word document: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to extract Word document: {ex.Message}", ex);
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

            return await Task.Run(() => ExtractWordContentFromBytes(bytes, fileName, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (UndocException ex)
        {
            throw new DocumentProcessingException(fileName, $"Failed to extract Word document from stream: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(fileName, $"Failed to extract Word document from stream: {ex.Message}", ex);
        }
    }

    private static RawContent ExtractWordContent(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var extractedImages = new List<ImageInfo>();

        using var doc = UndocDocument.ParseFile(filePath);

        // Convert to Markdown using Undoc native library
        var markdown = doc.ToMarkdown(new MarkdownOptions
        {
            IncludeFrontmatter = false,
            EscapeSpecialChars = false,
            ParagraphSpacing = false
        });

        // Remove null bytes
        markdown = TextSanitizer.RemoveNullBytes(markdown);

        // Extract metadata
        if (!string.IsNullOrWhiteSpace(doc.Title))
            structuralHints["document_title"] = doc.Title;
        if (!string.IsNullOrWhiteSpace(doc.Author))
            structuralHints["author"] = doc.Author;

        structuralHints["section_count"] = doc.SectionCount;

        // Extract embedded resources (images)
        var resourceIds = doc.GetResourceIds();
        foreach (var resourceId in resourceIds)
        {
            var resourceData = doc.GetResourceData(resourceId);
            if (resourceData != null)
            {
                var imageInfo = new ImageInfo
                {
                    Id = resourceId,
                    MimeType = GuessMimeType(resourceId),
                    Data = resourceData,
                    OriginalSize = resourceData.Length,
                    SourceUrl = $"embedded:{resourceId}"
                };
                extractedImages.Add(imageInfo);
            }
        }

        // Update structural hints
        structuralHints["file_type"] = "word_document";
        structuralHints["character_count"] = markdown.Length;
        structuralHints["word_count"] = CountWords(markdown);
        structuralHints["paragraph_count"] = CountParagraphs(markdown);
        structuralHints["conversion_method"] = "undoc_native";

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
        var hasLinks = LinkRegex().IsMatch(markdown);

        if (hasHeaders) structuralHints["has_headers"] = true;
        if (hasTables) structuralHints["has_tables"] = true;
        if (hasLists) structuralHints["has_lists"] = true;
        if (hasLinks) structuralHints["has_links"] = true;

        return new RawContent
        {
            Text = markdown.Trim(),
            File = new SourceFileInfo
            {
                Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                Extension = ".docx",
                Size = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc,
                ModifiedAt = fileInfo.LastWriteTimeUtc
            },
            Hints = structuralHints,
            Warnings = warnings,
            Images = extractedImages,
            ReaderType = "WordReader"
        };
    }

    private static RawContent ExtractWordContentFromBytes(byte[] bytes, string fileName, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var extractedImages = new List<ImageInfo>();

        using var doc = UndocDocument.ParseBytes(bytes);

        // Convert to Markdown using Undoc native library
        var markdown = doc.ToMarkdown(new MarkdownOptions
        {
            IncludeFrontmatter = false,
            EscapeSpecialChars = false,
            ParagraphSpacing = false
        });

        // Remove null bytes
        markdown = TextSanitizer.RemoveNullBytes(markdown);

        // Extract metadata
        if (!string.IsNullOrWhiteSpace(doc.Title))
            structuralHints["document_title"] = doc.Title;
        if (!string.IsNullOrWhiteSpace(doc.Author))
            structuralHints["author"] = doc.Author;

        structuralHints["section_count"] = doc.SectionCount;

        // Extract embedded resources (images)
        var resourceIds = doc.GetResourceIds();
        foreach (var resourceId in resourceIds)
        {
            var resourceData = doc.GetResourceData(resourceId);
            if (resourceData != null)
            {
                var imageInfo = new ImageInfo
                {
                    Id = resourceId,
                    MimeType = GuessMimeType(resourceId),
                    Data = resourceData,
                    OriginalSize = resourceData.Length,
                    SourceUrl = $"embedded:{resourceId}"
                };
                extractedImages.Add(imageInfo);
            }
        }

        // Update structural hints
        structuralHints["file_type"] = "word_document";
        structuralHints["character_count"] = markdown.Length;
        structuralHints["word_count"] = CountWords(markdown);
        structuralHints["paragraph_count"] = CountParagraphs(markdown);
        structuralHints["conversion_method"] = "undoc_native";

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
        var hasLinks = LinkRegex().IsMatch(markdown);

        if (hasHeaders) structuralHints["has_headers"] = true;
        if (hasTables) structuralHints["has_tables"] = true;
        if (hasLists) structuralHints["has_lists"] = true;
        if (hasLinks) structuralHints["has_links"] = true;

        return new RawContent
        {
            Text = markdown.Trim(),
            File = new SourceFileInfo
            {
                Name = fileName,
                Extension = ".docx",
                Size = bytes.Length,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            },
            Hints = structuralHints,
            Warnings = warnings,
            Images = extractedImages,
            ReaderType = "WordReader"
        };
    }

    private static string GuessMimeType(string resourceId)
    {
        var lower = resourceId.ToLowerInvariant();
        if (lower.EndsWith(".png")) return "image/png";
        if (lower.EndsWith(".jpg") || lower.EndsWith(".jpeg")) return "image/jpeg";
        if (lower.EndsWith(".gif")) return "image/gif";
        if (lower.EndsWith(".webp")) return "image/webp";
        if (lower.EndsWith(".svg")) return "image/svg+xml";
        if (lower.EndsWith(".emf")) return "image/emf";
        if (lower.EndsWith(".wmf")) return "image/wmf";
        return "application/octet-stream";
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        return text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static int CountParagraphs(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        // Count paragraphs by splitting on double newlines
        var paragraphs = text.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);
        return Math.Max(1, paragraphs.Length);
    }

    // Generated regex patterns for performance
    [GeneratedRegex(@"^#{1,6}\s", RegexOptions.Multiline)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"^[\*\-\+]\s|^\d+\.\s", RegexOptions.Multiline)]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"\[.+?\]\(.+?\)")]
    private static partial Regex LinkRegex();
}
