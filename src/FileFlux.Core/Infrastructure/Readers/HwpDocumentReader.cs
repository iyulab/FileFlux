using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FileFlux.Core.Infrastructure.Interop;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// HWP/HWPX document reader using unhwp native library.
/// Downloads native library lazily from GitHub releases on first use.
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

            // Detect format using native library (if available)
            var loader = UnhwpNativeLoader.Instance;
            if (loader.TryLoadSync() && loader.DetectFormat != null)
            {
                var format = loader.DetectFormat(filePath);
                result.DocumentProps["hwp_format"] = format switch
                {
                    1 => "HWP5",
                    2 => "HWPX",
                    _ => "Unknown"
                };
            }
            else
            {
                // Fallback: detect by extension
                result.DocumentProps["hwp_format"] = extension == ".hwpx" ? "HWPX" : "HWP5";
            }

            result.DocumentProps["file_type"] = "hwp_document";

            // HWP documents are treated as single logical page
            result.Pages.Add(new PageInfo
            {
                Number = 1,
                HasContent = fileInfo.Length > 0,
                Props = { ["file_type"] = "hwp_document" }
            });

            result.Duration = DateTime.UtcNow - startTime;
            return result;
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

            result.DocumentProps["hwp_format"] = extension == ".hwpx" ? "HWPX" : "HWP5";
            result.DocumentProps["file_type"] = "hwp_document";

            result.Pages.Add(new PageInfo
            {
                Number = 1,
                HasContent = memoryStream.Length > 0,
                Props = { ["file_type"] = "hwp_document" }
            });

            result.Duration = DateTime.UtcNow - startTime;
            return result;
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
            // Ensure native library is loaded
            var loader = UnhwpNativeLoader.Instance;
            await loader.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            return await Task.Run(() => ExtractHwpContent(filePath, loader, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to process HWP document: {ex.Message}", ex);
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
            var loader = UnhwpNativeLoader.Instance;
            await loader.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            // Read stream to byte array
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            var data = memoryStream.ToArray();

            return await Task.Run(() => ExtractHwpContentFromBytes(data, fileName, loader, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(fileName, $"Failed to process HWP document from stream: {ex.Message}", ex);
        }
    }

    private static RawContent ExtractHwpContent(string filePath, UnhwpNativeLoader loader, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var images = new List<ImageInfo>();

        try
        {
            string markdown;
            int imageCount = 0;
            int sectionCount = 0;
            int paragraphCount = 0;

            // HYBRID APPROACH: Use Structured API for images + Simple API for text
            // Reason: Structured API's unhwp_result_get_markdown returns truncated content
            // See: claudedocs/issues/unhwp-structured-api-incomplete-markdown.md

            // Step 1: Extract images using Structured API (works correctly)
            if (loader.Parse != null && loader.ResultGetImageCount != null && loader.ResultGetImage != null && loader.ResultFree != null)
            {
                var resultHandle = loader.Parse(filePath, IntPtr.Zero, IntPtr.Zero);

                if (resultHandle != IntPtr.Zero)
                {
                    try
                    {
                        // Check for errors
                        bool hasError = false;
                        if (loader.ResultGetError != null)
                        {
                            var errorPtr = loader.ResultGetError(resultHandle);
                            if (errorPtr != IntPtr.Zero)
                            {
                                var errorMsg = Marshal.PtrToStringUTF8(errorPtr) ?? "Unknown error";
                                warnings.Add($"HWP parse warning (images): {errorMsg}");
                                hasError = true;
                            }
                        }

                        if (!hasError)
                        {
                            // Extract images
                            imageCount = loader.ResultGetImageCount(resultHandle);
                            for (int i = 0; i < imageCount; i++)
                            {
                                if (loader.ResultGetImage(resultHandle, i, out var imageData) == 0)
                                {
                                    var imageName = imageData.Name != IntPtr.Zero
                                        ? Marshal.PtrToStringUTF8(imageData.Name) ?? $"image_{i}"
                                        : $"image_{i}";

                                    var dataLen = (int)imageData.DataLen;
                                    if (dataLen > 0 && imageData.Data != IntPtr.Zero)
                                    {
                                        var data = new byte[dataLen];
                                        Marshal.Copy(imageData.Data, data, 0, dataLen);
                                        images.Add(new ImageInfo
                                        {
                                            Id = $"hwp_img_{i}",
                                            Caption = imageName,
                                            Data = data,
                                            MimeType = DetectImageMimeType(data),
                                            OriginalSize = dataLen,
                                            SourceUrl = $"embedded:hwp_img_{i}"
                                        });
                                    }
                                }
                            }

                            // Get section and paragraph counts
                            if (loader.ResultGetSectionCount != null)
                                sectionCount = loader.ResultGetSectionCount(resultHandle);
                            if (loader.ResultGetParagraphCount != null)
                                paragraphCount = loader.ResultGetParagraphCount(resultHandle);
                        }
                    }
                    finally
                    {
                        loader.ResultFree(resultHandle);
                    }
                }
            }

            // Step 2: Get full text using Simple API (Structured API returns truncated text)
            if (loader.ToMarkdownWithCleanup != null || loader.ToMarkdown != null)
            {
                int result;
                IntPtr markdownPtr, errorPtr;

                if (loader.ToMarkdownWithCleanup != null)
                {
                    result = loader.ToMarkdownWithCleanup(filePath, out markdownPtr, out errorPtr);
                }
                else
                {
                    result = loader.ToMarkdown!(filePath, out markdownPtr, out errorPtr);
                }

                if (result != 0 || errorPtr != IntPtr.Zero)
                {
                    var errorMsg = errorPtr != IntPtr.Zero
                        ? Marshal.PtrToStringUTF8(errorPtr) ?? "Unknown error"
                        : $"Conversion failed with code {result}";

                    if (errorPtr != IntPtr.Zero)
                        loader.FreeString?.Invoke(errorPtr);

                    warnings.Add($"HWP conversion error: {errorMsg}");
                    return CreateEmptyResult(fileInfo, warnings, structuralHints);
                }

                if (markdownPtr == IntPtr.Zero)
                {
                    warnings.Add("Native library returned null result");
                    return CreateEmptyResult(fileInfo, warnings, structuralHints);
                }

                markdown = Marshal.PtrToStringUTF8(markdownPtr) ?? string.Empty;
                loader.FreeString?.Invoke(markdownPtr);
            }
            else
            {
                throw new InvalidOperationException("Native library not properly loaded - ToMarkdown not available");
            }

            // Post-process markdown
            markdown = PostProcessMarkdown(markdown);

            // Extract structural hints
            structuralHints["file_type"] = "hwp_document";
            structuralHints["hwp_format"] = extension == ".hwpx" ? "HWPX" : "HWP5";
            structuralHints["character_count"] = markdown.Length;
            structuralHints["word_count"] = CountWords(markdown);
            structuralHints["conversion_method"] = "unhwp";
            structuralHints["image_count"] = imageCount;
            if (sectionCount > 0) structuralHints["section_count"] = sectionCount;
            if (paragraphCount > 0) structuralHints["paragraph_count"] = paragraphCount;

            // Detect structural elements
            var hasHeaders = HeaderRegex().IsMatch(markdown);
            var hasTables = markdown.Contains("|---", StringComparison.Ordinal) ||
                           markdown.Contains("| ---", StringComparison.Ordinal);
            var hasLists = ListRegex().IsMatch(markdown);
            var hasLinks = LinkRegex().IsMatch(markdown);
            var hasImages = ImageRegex().IsMatch(markdown) || imageCount > 0;

            if (hasHeaders) structuralHints["has_headers"] = true;
            if (hasTables) structuralHints["has_tables"] = true;
            if (hasLists) structuralHints["has_lists"] = true;
            if (hasLinks) structuralHints["has_links"] = true;
            if (hasImages) structuralHints["has_images"] = true;

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
                ReaderType = "HwpReader"
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Error processing HWP document: {ex.Message}");
            return CreateEmptyResult(fileInfo, warnings, structuralHints);
        }
    }

    private static string DetectImageMimeType(byte[] data)
    {
        if (data.Length < 4) return "application/octet-stream";

        // Check magic bytes
        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return "image/jpeg";
        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return "image/png";
        if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46)
            return "image/gif";
        if (data[0] == 0x42 && data[1] == 0x4D)
            return "image/bmp";
        if (data.Length >= 12 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46
            && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
            return "image/webp";

        return "application/octet-stream";
    }

    private static RawContent ExtractHwpContentFromBytes(byte[] data, string fileName, UnhwpNativeLoader loader, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var images = new List<ImageInfo>();

        try
        {
            string markdown;
            int imageCount = 0;
            int sectionCount = 0;
            int paragraphCount = 0;

            // Pin the byte array for native calls
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                var dataPtr = handle.AddrOfPinnedObject();

                // HYBRID APPROACH: Use Structured API for images + Simple API for text
                // Reason: Structured API's unhwp_result_get_markdown returns truncated content
                // See: claudedocs/issues/unhwp-structured-api-incomplete-markdown.md

                // Step 1: Extract images using Structured API (works correctly)
                if (loader.ParseBytes != null && loader.ResultGetImageCount != null && loader.ResultGetImage != null && loader.ResultFree != null)
                {
                    var resultHandle = loader.ParseBytes(dataPtr, (nuint)data.Length, IntPtr.Zero, IntPtr.Zero);

                    if (resultHandle != IntPtr.Zero)
                    {
                        try
                        {
                            // Check for errors
                            bool hasError = false;
                            if (loader.ResultGetError != null)
                            {
                                var errorPtr = loader.ResultGetError(resultHandle);
                                if (errorPtr != IntPtr.Zero)
                                {
                                    var errorMsg = Marshal.PtrToStringUTF8(errorPtr) ?? "Unknown error";
                                    warnings.Add($"HWP parse warning (images): {errorMsg}");
                                    hasError = true;
                                }
                            }

                            if (!hasError)
                            {
                                // Extract images
                                imageCount = loader.ResultGetImageCount(resultHandle);
                                for (int i = 0; i < imageCount; i++)
                                {
                                    if (loader.ResultGetImage(resultHandle, i, out var imageData) == 0)
                                    {
                                        var imageName = imageData.Name != IntPtr.Zero
                                            ? Marshal.PtrToStringUTF8(imageData.Name) ?? $"image_{i}"
                                            : $"image_{i}";

                                        var dataLen = (int)imageData.DataLen;
                                        if (dataLen > 0 && imageData.Data != IntPtr.Zero)
                                        {
                                            var imgData = new byte[dataLen];
                                            Marshal.Copy(imageData.Data, imgData, 0, dataLen);
                                            images.Add(new ImageInfo
                                            {
                                                Id = $"hwp_img_{i}",
                                                Caption = imageName,
                                                Data = imgData,
                                                MimeType = DetectImageMimeType(imgData),
                                                OriginalSize = dataLen,
                                                SourceUrl = $"embedded:hwp_img_{i}"
                                            });
                                        }
                                    }
                                }

                                // Get section and paragraph counts
                                if (loader.ResultGetSectionCount != null)
                                    sectionCount = loader.ResultGetSectionCount(resultHandle);
                                if (loader.ResultGetParagraphCount != null)
                                    paragraphCount = loader.ResultGetParagraphCount(resultHandle);
                            }
                        }
                        finally
                        {
                            loader.ResultFree(resultHandle);
                        }
                    }
                }

                // Step 2: Get full text using Simple API (Structured API returns truncated text)
                if (loader.BytesToMarkdown != null)
                {
                    var result = loader.BytesToMarkdown(dataPtr, (nuint)data.Length, out var markdownPtr, out var errorPtr);

                    if (result != 0 || errorPtr != IntPtr.Zero)
                    {
                        var errorMsg = errorPtr != IntPtr.Zero
                            ? Marshal.PtrToStringUTF8(errorPtr) ?? "Unknown error"
                            : $"Conversion failed with code {result}";

                        if (errorPtr != IntPtr.Zero)
                            loader.FreeString?.Invoke(errorPtr);

                        warnings.Add($"HWP conversion error: {errorMsg}");
                        return CreateEmptyStreamResult(data.Length, fileName, warnings, structuralHints);
                    }

                    if (markdownPtr == IntPtr.Zero)
                    {
                        warnings.Add("Native library returned null result");
                        return CreateEmptyStreamResult(data.Length, fileName, warnings, structuralHints);
                    }

                    markdown = Marshal.PtrToStringUTF8(markdownPtr) ?? string.Empty;
                    loader.FreeString?.Invoke(markdownPtr);
                }
                else
                {
                    throw new InvalidOperationException("Native library not properly loaded - BytesToMarkdown not available");
                }
            }
            finally
            {
                handle.Free();
            }

            // Post-process markdown
            markdown = PostProcessMarkdown(markdown);

            // Extract structural hints
            structuralHints["file_type"] = "hwp_document";
            structuralHints["hwp_format"] = extension == ".hwpx" ? "HWPX" : "HWP5";
            structuralHints["character_count"] = markdown.Length;
            structuralHints["word_count"] = CountWords(markdown);
            structuralHints["conversion_method"] = "unhwp";
            structuralHints["image_count"] = imageCount;
            if (sectionCount > 0) structuralHints["section_count"] = sectionCount;
            if (paragraphCount > 0) structuralHints["paragraph_count"] = paragraphCount;

            // Detect structural elements
            var hasHeaders = HeaderRegex().IsMatch(markdown);
            var hasTables = markdown.Contains("|---", StringComparison.Ordinal) ||
                           markdown.Contains("| ---", StringComparison.Ordinal);
            var hasLists = ListRegex().IsMatch(markdown);
            var hasLinks = LinkRegex().IsMatch(markdown);
            var hasImages = ImageRegex().IsMatch(markdown) || imageCount > 0;

            if (hasHeaders) structuralHints["has_headers"] = true;
            if (hasTables) structuralHints["has_tables"] = true;
            if (hasLists) structuralHints["has_lists"] = true;
            if (hasLinks) structuralHints["has_links"] = true;
            if (hasImages) structuralHints["has_images"] = true;

            return new RawContent
            {
                Text = markdown.Trim(),
                File = new SourceFileInfo
                {
                    Name = fileName,
                    Extension = extension,
                    Size = data.Length,
                    CreatedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow
                },
                Images = images,
                Hints = structuralHints,
                Warnings = warnings,
                ReaderType = "HwpReader"
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Error processing HWP document from stream: {ex.Message}");
            return CreateEmptyStreamResult(data.Length, fileName, warnings, structuralHints);
        }
    }

    private static string PostProcessMarkdown(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return markdown;

        // Remove null bytes
        markdown = TextSanitizer.RemoveNullBytes(markdown);

        // Normalize multiple consecutive newlines to max 2
        markdown = ExcessiveNewlinesRegex().Replace(markdown, "\n\n");

        // Ensure proper spacing around headers
        markdown = HeaderSpacingRegex().Replace(markdown, "\n\n$1");

        return markdown.Trim();
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        // Count both Korean characters and English words
        var koreanChars = text.Count(c => c >= '\uAC00' && c <= '\uD7A3');
        var englishWords = text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Count(w => w.Any(c => char.IsLetter(c) && c < '\uAC00'));

        return koreanChars + englishWords;
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
            ReaderType = "HwpReader"
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
            ReaderType = "HwpReader"
        };
    }

    // Generated regex patterns
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
}
