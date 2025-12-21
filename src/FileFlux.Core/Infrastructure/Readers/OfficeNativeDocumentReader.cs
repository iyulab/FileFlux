using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FileFlux.Core.Infrastructure.Interop;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Microsoft Office document reader using undoc native library.
/// Supports DOCX, XLSX, PPTX with high-performance Rust-based extraction.
/// Downloads native library lazily from GitHub releases on first use.
/// </summary>
public sealed partial class OfficeNativeDocumentReader : IDocumentReader
{
    public string ReaderType => "OfficeNativeReader";

    public IEnumerable<string> SupportedExtensions => [".docx", ".xlsx", ".pptx"];

    /// <summary>
    /// Extracts all images from an Office document.
    /// Requires undoc v0.1.8+ for resource access API.
    /// </summary>
    public async Task<IReadOnlyList<ExtractedResource>> ExtractImagesAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Office document not found: {filePath}");

        var loader = UndocNativeLoader.Instance;
        await loader.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        // Check if resource API is available
        if (loader.GetResourceIds == null)
        {
            return [];
        }

        return await Task.Run(() =>
        {
            using var doc = UndocDocument.ParseFile(filePath);
            if (doc == null)
                return (IReadOnlyList<ExtractedResource>)[];

            return doc.ExtractImages().ToList().AsReadOnly();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Extracts all images from an Office document stream.
    /// Requires undoc v0.1.8+ for resource access API.
    /// </summary>
    public async Task<IReadOnlyList<ExtractedResource>> ExtractImagesAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var loader = UndocNativeLoader.Instance;
        await loader.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        // Check if resource API is available
        if (loader.GetResourceIds == null)
        {
            return [];
        }

        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
        var data = memoryStream.ToArray();

        return await Task.Run(() =>
        {
            using var doc = UndocDocument.ParseBytes(data);
            if (doc == null)
                return (IReadOnlyList<ExtractedResource>)[];

            return doc.ExtractImages().ToList().AsReadOnly();
        }, cancellationToken).ConfigureAwait(false);
    }

    public bool CanRead(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension is ".docx" or ".xlsx" or ".pptx";
    }

    // ========================================
    // Stage 0: Read (Document Structure)
    // ========================================

    public async Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Office document not found: {filePath}");

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

            // Try to get document info using native library
            var loader = UndocNativeLoader.Instance;
            if (loader.TryLoadSync() && loader.ParseFile != null)
            {
                var docHandle = loader.ParseFile(filePath);
                if (docHandle != IntPtr.Zero)
                {
                    try
                    {
                        // Get document properties
                        if (loader.GetTitle != null)
                        {
                            var titlePtr = loader.GetTitle(docHandle);
                            if (titlePtr != IntPtr.Zero)
                            {
                                var title = Marshal.PtrToStringUTF8(titlePtr);
                                if (!string.IsNullOrWhiteSpace(title))
                                    result.DocumentProps["title"] = title;
                                loader.FreeString?.Invoke(titlePtr);
                            }
                        }

                        if (loader.GetAuthor != null)
                        {
                            var authorPtr = loader.GetAuthor(docHandle);
                            if (authorPtr != IntPtr.Zero)
                            {
                                var author = Marshal.PtrToStringUTF8(authorPtr);
                                if (!string.IsNullOrWhiteSpace(author))
                                    result.DocumentProps["author"] = author;
                                loader.FreeString?.Invoke(authorPtr);
                            }
                        }

                        if (loader.SectionCount != null)
                        {
                            var sectionCount = loader.SectionCount(docHandle);
                            if (sectionCount > 0)
                                result.DocumentProps["section_count"] = sectionCount;
                        }

                        if (loader.ResourceCount != null)
                        {
                            var resourceCount = loader.ResourceCount(docHandle);
                            if (resourceCount > 0)
                                result.DocumentProps["resource_count"] = resourceCount;
                        }
                    }
                    finally
                    {
                        loader.FreeDocument?.Invoke(docHandle);
                    }
                }
            }

            result.DocumentProps["file_type"] = GetFileType(extension);
            result.DocumentProps["processor"] = "undoc";

            // Office documents are treated as single logical page
            result.Pages.Add(new PageInfo
            {
                Number = 1,
                HasContent = fileInfo.Length > 0,
                Props = { ["file_type"] = GetFileType(extension) }
            });

            result.Duration = DateTime.UtcNow - startTime;
            return result;
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to read Office document: {ex.Message}", ex);
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

            var result = new ReadResult
            {
                File = new SourceFileInfo
                {
                    Name = fileName,
                    Extension = extension,
                    Size = memoryStream.Length,
                    CreatedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow
                },
                ReaderType = ReaderType
            };

            result.DocumentProps["file_type"] = GetFileType(extension);
            result.DocumentProps["processor"] = "undoc";

            result.Pages.Add(new PageInfo
            {
                Number = 1,
                HasContent = memoryStream.Length > 0,
                Props = { ["file_type"] = GetFileType(extension) }
            });

            result.Duration = DateTime.UtcNow - startTime;
            return result;
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(fileName, $"Failed to read Office document from stream: {ex.Message}", ex);
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
            throw new FileNotFoundException($"Office document not found: {filePath}");

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        try
        {
            // Ensure native library is loaded
            var loader = UndocNativeLoader.Instance;
            await loader.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            return await Task.Run(() => ExtractOfficeContent(filePath, loader, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to process Office document: {ex.Message}", ex);
        }
    }

    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!CanRead(fileName))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        try
        {
            // Ensure native library is loaded
            var loader = UndocNativeLoader.Instance;
            await loader.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            // Read stream to byte array
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            var data = memoryStream.ToArray();

            return await Task.Run(() => ExtractOfficeContentFromBytes(data, fileName, loader, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(fileName, $"Failed to process Office document from stream: {ex.Message}", ex);
        }
    }

    private static RawContent ExtractOfficeContent(string filePath, UndocNativeLoader loader, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        try
        {
            if (loader.ParseFile == null)
                throw new InvalidOperationException("Native library not properly loaded - ParseFile not available");

            var docHandle = loader.ParseFile(filePath);
            if (docHandle == IntPtr.Zero)
            {
                var error = loader.GetLastError();
                throw new InvalidOperationException($"Failed to parse document: {error}");
            }

            try
            {
                return ExtractFromDocument(docHandle, fileInfo, extension, loader, warnings, structuralHints);
            }
            finally
            {
                loader.FreeDocument?.Invoke(docHandle);
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Error processing Office document: {ex.Message}");
            return CreateEmptyResult(fileInfo, warnings, structuralHints);
        }
    }

    private static RawContent ExtractOfficeContentFromBytes(byte[] data, string fileName, UndocNativeLoader loader, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        try
        {
            if (loader.ParseBytes == null)
                throw new InvalidOperationException("Native library not properly loaded - ParseBytes not available");

            // Pin the byte array for native call
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                var dataPtr = handle.AddrOfPinnedObject();
                var docHandle = loader.ParseBytes(dataPtr, (nuint)data.Length);

                if (docHandle == IntPtr.Zero)
                {
                    var error = loader.GetLastError();
                    throw new InvalidOperationException($"Failed to parse document: {error}");
                }

                try
                {
                    return ExtractFromDocumentBytes(docHandle, data.Length, fileName, extension, loader, warnings, structuralHints);
                }
                finally
                {
                    loader.FreeDocument?.Invoke(docHandle);
                }
            }
            finally
            {
                handle.Free();
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Error processing Office document from stream: {ex.Message}");
            return CreateEmptyStreamResult(data.Length, fileName, warnings, structuralHints);
        }
    }

    private static RawContent ExtractFromDocument(
        IntPtr docHandle,
        FileInfo fileInfo,
        string extension,
        UndocNativeLoader loader,
        List<string> warnings,
        Dictionary<string, object> structuralHints)
    {
        // Get markdown content
        if (loader.ToMarkdown == null)
            throw new InvalidOperationException("Native library not properly loaded - ToMarkdown not available");

        var markdownPtr = loader.ToMarkdown(docHandle, (int)UndocMarkdownFlags.None);
        if (markdownPtr == IntPtr.Zero)
        {
            var error = loader.GetLastError();
            warnings.Add($"Failed to convert to markdown: {error}");
            return CreateEmptyResult(fileInfo, warnings, structuralHints);
        }

        string markdown;
        try
        {
            markdown = Marshal.PtrToStringUTF8(markdownPtr) ?? string.Empty;
        }
        finally
        {
            loader.FreeString?.Invoke(markdownPtr);
        }

        // Post-process markdown
        markdown = PostProcessMarkdown(markdown);

        // Get document properties
        string? title = null;
        string? author = null;

        if (loader.GetTitle != null)
        {
            var titlePtr = loader.GetTitle(docHandle);
            if (titlePtr != IntPtr.Zero)
            {
                title = Marshal.PtrToStringUTF8(titlePtr);
                loader.FreeString?.Invoke(titlePtr);
            }
        }

        if (loader.GetAuthor != null)
        {
            var authorPtr = loader.GetAuthor(docHandle);
            if (authorPtr != IntPtr.Zero)
            {
                author = Marshal.PtrToStringUTF8(authorPtr);
                loader.FreeString?.Invoke(authorPtr);
            }
        }

        // Build structural hints
        structuralHints["file_type"] = GetFileType(extension);
        structuralHints["processor"] = "undoc";
        structuralHints["character_count"] = markdown.Length;
        structuralHints["word_count"] = CountWords(markdown);

        if (!string.IsNullOrWhiteSpace(title))
            structuralHints["title"] = title;
        if (!string.IsNullOrWhiteSpace(author))
            structuralHints["author"] = author;

        if (loader.SectionCount != null)
        {
            var sectionCount = loader.SectionCount(docHandle);
            if (sectionCount > 0)
                structuralHints["section_count"] = sectionCount;
        }

        if (loader.ResourceCount != null)
        {
            var resourceCount = loader.ResourceCount(docHandle);
            if (resourceCount > 0)
                structuralHints["resource_count"] = resourceCount;
        }

        // Detect structural elements
        DetectStructuralElements(markdown, structuralHints);

        // Extract images using new resource API (v0.1.8+)
        var images = ExtractImageInfoFromDocument(docHandle, loader);

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
            Images = images,
            Hints = structuralHints,
            Warnings = warnings,
            ReaderType = "OfficeNativeReader"
        };
    }

    private static RawContent ExtractFromDocumentBytes(
        IntPtr docHandle,
        long size,
        string fileName,
        string extension,
        UndocNativeLoader loader,
        List<string> warnings,
        Dictionary<string, object> structuralHints)
    {
        // Get markdown content
        if (loader.ToMarkdown == null)
            throw new InvalidOperationException("Native library not properly loaded - ToMarkdown not available");

        var markdownPtr = loader.ToMarkdown(docHandle, (int)UndocMarkdownFlags.None);
        if (markdownPtr == IntPtr.Zero)
        {
            var error = loader.GetLastError();
            warnings.Add($"Failed to convert to markdown: {error}");
            return CreateEmptyStreamResult(size, fileName, warnings, structuralHints);
        }

        string markdown;
        try
        {
            markdown = Marshal.PtrToStringUTF8(markdownPtr) ?? string.Empty;
        }
        finally
        {
            loader.FreeString?.Invoke(markdownPtr);
        }

        // Post-process markdown
        markdown = PostProcessMarkdown(markdown);

        // Get document properties
        string? title = null;
        string? author = null;

        if (loader.GetTitle != null)
        {
            var titlePtr = loader.GetTitle(docHandle);
            if (titlePtr != IntPtr.Zero)
            {
                title = Marshal.PtrToStringUTF8(titlePtr);
                loader.FreeString?.Invoke(titlePtr);
            }
        }

        if (loader.GetAuthor != null)
        {
            var authorPtr = loader.GetAuthor(docHandle);
            if (authorPtr != IntPtr.Zero)
            {
                author = Marshal.PtrToStringUTF8(authorPtr);
                loader.FreeString?.Invoke(authorPtr);
            }
        }

        // Build structural hints
        structuralHints["file_type"] = GetFileType(extension);
        structuralHints["processor"] = "undoc";
        structuralHints["character_count"] = markdown.Length;
        structuralHints["word_count"] = CountWords(markdown);

        if (!string.IsNullOrWhiteSpace(title))
            structuralHints["title"] = title;
        if (!string.IsNullOrWhiteSpace(author))
            structuralHints["author"] = author;

        if (loader.SectionCount != null)
        {
            var sectionCount = loader.SectionCount(docHandle);
            if (sectionCount > 0)
                structuralHints["section_count"] = sectionCount;
        }

        if (loader.ResourceCount != null)
        {
            var resourceCount = loader.ResourceCount(docHandle);
            if (resourceCount > 0)
                structuralHints["resource_count"] = resourceCount;
        }

        // Detect structural elements
        DetectStructuralElements(markdown, structuralHints);

        // Extract images using new resource API (v0.1.8+)
        var images = ExtractImageInfoFromDocument(docHandle, loader);

        return new RawContent
        {
            Text = markdown.Trim(),
            File = new SourceFileInfo
            {
                Name = fileName,
                Extension = extension,
                Size = size,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            },
            Images = images,
            Hints = structuralHints,
            Warnings = warnings,
            ReaderType = "OfficeNativeReader"
        };
    }

    /// <summary>
    /// Extracts image information from document using undoc v0.1.8+ resource API.
    /// Returns empty list if resource API is not available.
    /// </summary>
    private static List<ImageInfo> ExtractImageInfoFromDocument(IntPtr docHandle, UndocNativeLoader loader)
    {
        var images = new List<ImageInfo>();

        // Check if resource API is available (v0.1.8+)
        if (loader.GetResourceIds == null || loader.GetResourceInfo == null)
            return images;

        var idsPtr = loader.GetResourceIds(docHandle);
        if (idsPtr == IntPtr.Zero)
            return images;

        string[] ids;
        try
        {
            var idsJson = Marshal.PtrToStringUTF8(idsPtr);
            if (string.IsNullOrEmpty(idsJson))
                return images;

            ids = System.Text.Json.JsonSerializer.Deserialize<string[]>(idsJson) ?? [];
        }
        finally
        {
            loader.FreeString?.Invoke(idsPtr);
        }

        var imageIndex = 0;
        foreach (var id in ids)
        {
            var infoPtr = loader.GetResourceInfo(docHandle, id);
            if (infoPtr == IntPtr.Zero)
                continue;

            try
            {
                var infoJson = Marshal.PtrToStringUTF8(infoPtr);
                if (string.IsNullOrEmpty(infoJson))
                    continue;

                var resourceInfo = System.Text.Json.JsonSerializer.Deserialize<UndocResourceInfo>(infoJson);
                if (resourceInfo == null || resourceInfo.Type != "image")
                    continue;

                // Get binary data if available
                byte[]? data = null;
                if (loader.GetResourceData != null && loader.FreeBytes != null)
                {
                    var dataPtr = loader.GetResourceData(docHandle, id, out var len);
                    if (dataPtr != IntPtr.Zero && len > 0)
                    {
                        data = new byte[(int)len];
                        Marshal.Copy(dataPtr, data, 0, (int)len);
                        loader.FreeBytes(dataPtr, len);
                    }
                }

                var image = new ImageInfo
                {
                    Id = $"img_{imageIndex++:D3}",
                    Caption = resourceInfo.AltText,
                    MimeType = resourceInfo.MimeType ?? "image/png",
                    Data = data,
                    SourceUrl = $"embedded:{id}",
                    OriginalSize = resourceInfo.Size,
                    Position = 0 // Position in text is not tracked by undoc
                };

                if (resourceInfo.Width.HasValue)
                    image.Properties["width"] = resourceInfo.Width.Value;
                if (resourceInfo.Height.HasValue)
                    image.Properties["height"] = resourceInfo.Height.Value;
                image.Properties["resource_id"] = id;
                if (!string.IsNullOrEmpty(resourceInfo.Filename))
                    image.Properties["filename"] = resourceInfo.Filename;

                images.Add(image);
            }
            finally
            {
                loader.FreeString?.Invoke(infoPtr);
            }
        }

        return images;
    }

    private static void DetectStructuralElements(string markdown, Dictionary<string, object> hints)
    {
        var hasHeaders = HeaderRegex().IsMatch(markdown);
        var hasTables = markdown.Contains("|---", StringComparison.Ordinal) ||
                       markdown.Contains("| ---", StringComparison.Ordinal);
        var hasLists = ListRegex().IsMatch(markdown);
        var hasLinks = LinkRegex().IsMatch(markdown);
        var hasImages = ImageRegex().IsMatch(markdown);

        if (hasHeaders) hints["has_headers"] = true;
        if (hasTables) hints["has_tables"] = true;
        if (hasLists) hints["has_lists"] = true;
        if (hasLinks) hints["has_links"] = true;
        if (hasImages) hints["has_images"] = true;
    }

    private static string PostProcessMarkdown(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return markdown;

        // Remove null bytes
        markdown = TextSanitizer.RemoveNullBytes(markdown);

        // Convert HTML lists to markdown
        markdown = ConvertHtmlListsToMarkdown(markdown);

        // Convert other HTML elements
        markdown = ConvertHtmlElementsToMarkdown(markdown);

        // Decode common HTML entities
        markdown = DecodeHtmlEntities(markdown);

        // Normalize multiple consecutive newlines to max 2
        markdown = ExcessiveNewlinesRegex().Replace(markdown, "\n\n");

        // Ensure proper spacing around headers
        markdown = HeaderSpacingRegex().Replace(markdown, "\n\n$1");

        return markdown.Trim();
    }

    /// <summary>
    /// Converts HTML ordered and unordered lists to markdown format.
    /// Uses a more direct approach to handle HTML tags that may span multiple lines.
    /// </summary>
    private static string ConvertHtmlListsToMarkdown(string text)
    {
        // First, remove the opening/closing list tags
        text = Regex.Replace(text, @"<ol[^>]*>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</ol>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<ul[^>]*>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</ul>", "", RegexOptions.IgnoreCase);

        // Convert all <li>...</li> to markdown list items
        // Handle multi-line content within <li> tags
        text = Regex.Replace(text, @"<li[^>]*>(.*?)</li>", match =>
        {
            var content = match.Groups[1].Value.Trim();
            // Remove any nested HTML tags
            content = StripHtmlTags(content);
            // Decode HTML entities in the content
            content = DecodeHtmlEntities(content);
            return $"- {content}";
        }, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Handle standalone <li> tags without closing tag
        text = Regex.Replace(text, @"<li[^>]*>([^<]+)(?=<|$)", match =>
        {
            var content = match.Groups[1].Value.Trim();
            return $"- {content}";
        }, RegexOptions.IgnoreCase);

        return text;
    }

    /// <summary>
    /// Converts other HTML elements to markdown equivalents.
    /// </summary>
    private static string ConvertHtmlElementsToMarkdown(string text)
    {
        // Convert <br> and <br/> to newlines
        text = BreakTagRegex().Replace(text, "\n");

        // Convert <p> tags to paragraph breaks
        text = ParagraphStartRegex().Replace(text, "\n\n");
        text = ParagraphEndRegex().Replace(text, "\n\n");

        // Convert <strong> and <b> to bold
        text = BoldTagRegex().Replace(text, "**$1**");

        // Convert <em> and <i> to italic
        text = ItalicTagRegex().Replace(text, "*$1*");

        // Convert <code> to inline code
        text = CodeTagRegex().Replace(text, "`$1`");

        // Convert <a href="url">text</a> to [text](url)
        text = AnchorTagRegex().Replace(text, "[$2]($1)");

        // Remove any remaining HTML tags
        text = RemainingHtmlTagsRegex().Replace(text, "");

        return text;
    }

    /// <summary>
    /// Decodes common HTML entities to their character equivalents.
    /// </summary>
    private static string DecodeHtmlEntities(string text)
    {
        // Common HTML entities
        text = text.Replace("&gt;", ">");
        text = text.Replace("&lt;", "<");
        text = text.Replace("&amp;", "&");
        text = text.Replace("&quot;", "\"");
        text = text.Replace("&apos;", "'");
        text = text.Replace("&nbsp;", " ");
        text = text.Replace("&#39;", "'");
        text = text.Replace("&#x27;", "'");
        text = text.Replace("&mdash;", "—");
        text = text.Replace("&ndash;", "–");
        text = text.Replace("&bull;", "•");
        text = text.Replace("&hellip;", "…");
        text = text.Replace("&copy;", "©");
        text = text.Replace("&reg;", "®");
        text = text.Replace("&trade;", "™");

        // Numeric entities (common ones)
        text = NumericEntityRegex().Replace(text, match =>
        {
            var numStr = match.Groups[1].Value;
            if (int.TryParse(numStr, out var num) && num < 0x10000)
            {
                return ((char)num).ToString();
            }
            return match.Value;
        });

        return text;
    }

    /// <summary>
    /// Strips all HTML tags from the given text.
    /// </summary>
    private static string StripHtmlTags(string text)
    {
        return RemainingHtmlTagsRegex().Replace(text, "");
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        // Count both CJK characters and Latin words
        var cjkChars = text.Count(c =>
            (c >= '\u4E00' && c <= '\u9FFF') ||   // CJK Unified Ideographs
            (c >= '\uAC00' && c <= '\uD7A3') ||   // Korean Hangul
            (c >= '\u3040' && c <= '\u309F') ||   // Hiragana
            (c >= '\u30A0' && c <= '\u30FF'));    // Katakana

        var latinWords = text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Count(w => w.Any(c => char.IsLetter(c) && c < '\u3040'));

        return cjkChars + latinWords;
    }

    private static string GetFileType(string extension)
    {
        return extension switch
        {
            ".docx" => "word_document",
            ".xlsx" => "excel_spreadsheet",
            ".pptx" => "powerpoint_presentation",
            _ => "office_document"
        };
    }

    private static RawContent CreateEmptyResult(FileInfo fileInfo, List<string> warnings, Dictionary<string, object> structuralHints)
    {
        return new RawContent
        {
            Text = string.Empty,
            File = new SourceFileInfo
            {
                Name = fileInfo.Name,
                Extension = Path.GetExtension(fileInfo.Name).ToLowerInvariant(),
                Size = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc,
                ModifiedAt = fileInfo.LastWriteTimeUtc
            },
            Hints = structuralHints,
            Warnings = warnings,
            ReaderType = "OfficeNativeReader"
        };
    }

    private static RawContent CreateEmptyStreamResult(long size, string fileName, List<string> warnings, Dictionary<string, object> structuralHints)
    {
        return new RawContent
        {
            Text = string.Empty,
            File = new SourceFileInfo
            {
                Name = fileName,
                Extension = Path.GetExtension(fileName).ToLowerInvariant(),
                Size = size,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            },
            Hints = structuralHints,
            Warnings = warnings,
            ReaderType = "OfficeNativeReader"
        };
    }

    // Generated regex patterns for markdown detection
    [GeneratedRegex(@"^#{1,6}\s", RegexOptions.Multiline)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"^[\*\-\+]\s|^\d+\.\s", RegexOptions.Multiline)]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"\[.+?\]\(.+?\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"!\[.*?\]\(.+?\)")]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessiveNewlinesRegex();

    [GeneratedRegex(@"\n(#{1,6}\s)")]
    private static partial Regex HeaderSpacingRegex();

    // Generated regex patterns for HTML-to-Markdown conversion
    [GeneratedRegex(@"<ol[^>]*>.*?</ol>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex OrderedListRegex();

    [GeneratedRegex(@"<ul[^>]*>.*?</ul>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex UnorderedListRegex();

    [GeneratedRegex(@"<li[^>]*>(.*?)</li>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"<li[^>]*>(.*?)(?:</li>|$)", RegexOptions.IgnoreCase)]
    private static partial Regex StandaloneLiRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakTagRegex();

    [GeneratedRegex(@"<p[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphStartRegex();

    [GeneratedRegex(@"</p>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphEndRegex();

    [GeneratedRegex(@"<(?:strong|b)[^>]*>(.*?)</(?:strong|b)>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex BoldTagRegex();

    [GeneratedRegex(@"<(?:em|i)[^>]*>(.*?)</(?:em|i)>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ItalicTagRegex();

    [GeneratedRegex(@"<code[^>]*>(.*?)</code>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CodeTagRegex();

    [GeneratedRegex(@"<a\s+[^>]*href=[""']([^""']+)[""'][^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex AnchorTagRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex RemainingHtmlTagsRegex();

    [GeneratedRegex(@"&#(\d+);")]
    private static partial Regex NumericEntityRegex();
}
