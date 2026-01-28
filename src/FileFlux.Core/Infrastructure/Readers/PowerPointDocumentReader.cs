using FileFlux.Core;
using Undoc;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Microsoft PowerPoint document (.pptx) reader using Undoc (Rust FFI).
/// High-performance native library for Office document extraction.
/// </summary>
public class PowerPointDocumentReader : IDocumentReader
{
    public string ReaderType => "PowerPointReader";

    public IEnumerable<string> SupportedExtensions => [".pptx"];

    public bool CanRead(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension == ".pptx";
    }

    // ========================================
    // Stage 0: Read (Document Structure)
    // ========================================

    public async Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PowerPoint document not found: {filePath}");

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
                    Extension = ".pptx",
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

            result.DocumentProps["slide_count"] = doc.SectionCount;

            // Each section represents a slide
            for (int i = 1; i <= doc.SectionCount; i++)
            {
                result.Pages.Add(new PageInfo
                {
                    Number = i,
                    HasContent = true,
                    Props =
                    {
                        ["slide_number"] = i,
                        ["file_type"] = "powerpoint_slide"
                    }
                });
            }

            result.Duration = DateTime.UtcNow - startTime;
            return await Task.FromResult(result).ConfigureAwait(false);
        }
        catch (UndocException ex)
        {
            throw new DocumentProcessingException(filePath, $"Failed to read PowerPoint document: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to read PowerPoint document: {ex.Message}", ex);
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
                    Extension = ".pptx",
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

            result.DocumentProps["slide_count"] = doc.SectionCount;

            for (int i = 1; i <= doc.SectionCount; i++)
            {
                result.Pages.Add(new PageInfo
                {
                    Number = i,
                    HasContent = true,
                    Props =
                    {
                        ["slide_number"] = i,
                        ["file_type"] = "powerpoint_slide"
                    }
                });
            }

            result.Duration = DateTime.UtcNow - startTime;
            return result;
        }
        catch (UndocException ex)
        {
            throw new DocumentProcessingException(fileName, $"Failed to read PowerPoint document from stream: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(fileName, $"Failed to read PowerPoint document from stream: {ex.Message}", ex);
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
            throw new FileNotFoundException($"PowerPoint document not found: {filePath}");

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        try
        {
            return await Task.Run(() => ExtractPowerPointContent(filePath, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (UndocException ex)
        {
            throw new DocumentProcessingException(filePath, $"Failed to extract PowerPoint document: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to extract PowerPoint document: {ex.Message}", ex);
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

            return await Task.Run(() => ExtractPowerPointContentFromBytes(bytes, fileName, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (UndocException ex)
        {
            throw new DocumentProcessingException(fileName, $"Failed to extract PowerPoint document from stream: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(fileName, $"Failed to extract PowerPoint document from stream: {ex.Message}", ex);
        }
    }

    private static RawContent ExtractPowerPointContent(string filePath, CancellationToken cancellationToken)
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
            ParagraphSpacing = true  // Better readability for slides
        });

        // Remove null bytes
        markdown = TextSanitizer.RemoveNullBytes(markdown);

        // Extract metadata
        if (!string.IsNullOrWhiteSpace(doc.Title))
            structuralHints["document_title"] = doc.Title;
        if (!string.IsNullOrWhiteSpace(doc.Author))
            structuralHints["author"] = doc.Author;

        structuralHints["slide_count"] = doc.SectionCount;

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

        structuralHints["file_type"] = "powerpoint_presentation";
        structuralHints["character_count"] = markdown.Length;
        structuralHints["conversion_method"] = "undoc_native";

        if (extractedImages.Count > 0)
        {
            structuralHints["image_count"] = extractedImages.Count;
            structuralHints["has_images"] = true;
        }

        // PowerPoint often has headers/sections
        structuralHints["has_headers"] = true;

        return new RawContent
        {
            Text = markdown.Trim(),
            File = new SourceFileInfo
            {
                Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                Extension = ".pptx",
                Size = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc,
                ModifiedAt = fileInfo.LastWriteTimeUtc
            },
            Hints = structuralHints,
            Warnings = warnings,
            Images = extractedImages,
            ReaderType = "PowerPointReader"
        };
    }

    private static RawContent ExtractPowerPointContentFromBytes(byte[] bytes, string fileName, CancellationToken cancellationToken)
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
            ParagraphSpacing = true
        });

        // Remove null bytes
        markdown = TextSanitizer.RemoveNullBytes(markdown);

        // Extract metadata
        if (!string.IsNullOrWhiteSpace(doc.Title))
            structuralHints["document_title"] = doc.Title;
        if (!string.IsNullOrWhiteSpace(doc.Author))
            structuralHints["author"] = doc.Author;

        structuralHints["slide_count"] = doc.SectionCount;

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

        structuralHints["file_type"] = "powerpoint_presentation";
        structuralHints["character_count"] = markdown.Length;
        structuralHints["conversion_method"] = "undoc_native";

        if (extractedImages.Count > 0)
        {
            structuralHints["image_count"] = extractedImages.Count;
            structuralHints["has_images"] = true;
        }

        structuralHints["has_headers"] = true;

        return new RawContent
        {
            Text = markdown.Trim(),
            File = new SourceFileInfo
            {
                Name = fileName,
                Extension = ".pptx",
                Size = bytes.Length,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            },
            Hints = structuralHints,
            Warnings = warnings,
            Images = extractedImages,
            ReaderType = "PowerPointReader"
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
}
