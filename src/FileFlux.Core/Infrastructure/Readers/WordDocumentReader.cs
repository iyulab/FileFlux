using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FileFlux.Core;
using Mammoth;
using ReverseMarkdown;
using System.Text.RegularExpressions;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Microsoft Word 문서(.docx) 처리를 위한 문서 Reader
/// Two-hop 전략: Mammoth (DOCX→HTML) + ReverseMarkdown (HTML→MD)
/// 리스트 번호, 테이블, 하이퍼링크를 정확하게 처리
/// </summary>
public partial class WordDocumentReader : IDocumentReader
{
    public string ReaderType => "WordReader";

    public IEnumerable<string> SupportedExtensions => new[] { ".docx" };

    private static readonly Converter MarkdownConverter = new(new Config
    {
        GithubFlavored = true,
        RemoveComments = true,
        SmartHrefHandling = true,
        TableWithoutHeaderRowHandling = Config.TableWithoutHeaderRowHandlingOption.Default,
        UnknownTags = Config.UnknownTagsOption.Bypass
    });

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

            using var wordDocument = WordprocessingDocument.Open(filePath, false);
            var coreProperties = wordDocument.PackageProperties;

            if (!string.IsNullOrWhiteSpace(coreProperties?.Title))
                result.DocumentProps["title"] = coreProperties.Title;
            if (!string.IsNullOrWhiteSpace(coreProperties?.Creator))
                result.DocumentProps["author"] = coreProperties.Creator;

            var mainPart = wordDocument.MainDocumentPart;
            if (mainPart?.Document?.Body != null)
            {
                result.DocumentProps["paragraph_count"] = mainPart.Document.Body.Elements<Paragraph>().Count();
                result.DocumentProps["table_count"] = mainPart.Document.Body.Elements<Table>().Count();
            }

            // Word documents are single-page (logical) for this simplified model
            result.Pages.Add(new PageInfo
            {
                Number = 1,
                HasContent = fileInfo.Length > 0,
                Props = { ["file_type"] = "word_document" }
            });

            result.Duration = DateTime.UtcNow - startTime;
            return result;
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
            memoryStream.Position = 0;

            var result = new ReadResult
            {
                File = new SourceFileInfo
                {
                    Name = fileName,
                    Extension = ".docx",
                    Size = memoryStream.Length,
                    CreatedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow
                },
                ReaderType = ReaderType
            };

            using var wordDocument = WordprocessingDocument.Open(memoryStream, false);
            var coreProperties = wordDocument.PackageProperties;

            if (!string.IsNullOrWhiteSpace(coreProperties?.Title))
                result.DocumentProps["title"] = coreProperties.Title;
            if (!string.IsNullOrWhiteSpace(coreProperties?.Creator))
                result.DocumentProps["author"] = coreProperties.Creator;

            var mainPart = wordDocument.MainDocumentPart;
            if (mainPart?.Document?.Body != null)
            {
                result.DocumentProps["paragraph_count"] = mainPart.Document.Body.Elements<Paragraph>().Count();
                result.DocumentProps["table_count"] = mainPart.Document.Body.Elements<Table>().Count();
            }

            result.Pages.Add(new PageInfo
            {
                Number = 1,
                HasContent = memoryStream.Length > 0,
                Props = { ["file_type"] = "word_document" }
            });

            result.Duration = DateTime.UtcNow - startTime;
            return result;
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
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to process Word document: {ex.Message}", ex);
        }
    }

    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!CanRead(fileName))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        try
        {
            return await Task.Run(() => ExtractWordContentFromStream(stream, fileName, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(fileName, $"Failed to process Word document from stream: {ex.Message}", ex);
        }
    }

    private static RawContent ExtractWordContent(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var extractedImages = new List<ImageInfo>();

        try
        {
            // Two-hop conversion: DOCX → HTML → Markdown
            string markdown;
            var imageCount = 0;

            using (var fileStream = File.OpenRead(filePath))
            {
                var mammothConverter = new DocumentConverter();
                var result = mammothConverter.ConvertToHtml(fileStream);

                // Collect warnings from Mammoth
                foreach (var warning in result.Warnings)
                {
                    warnings.Add($"Mammoth: {warning}");
                }

                // Extract base64 images and replace with placeholders in HTML
                var (processedHtml, images) = ExtractBase64ImagesFromHtml(result.Value);
                extractedImages = images;

                // Convert HTML to Markdown
                markdown = MarkdownConverter.Convert(processedHtml);

                // Clean up HTML artifacts in markdown output
                markdown = CleanupMarkdown(markdown);

                // Count images (both extracted and remaining)
                imageCount = extractedImages.Count + CountImages(processedHtml);
            }

            // Extract metadata using OpenXML
            using (var wordDocument = WordprocessingDocument.Open(filePath, false))
            {
                ExtractDocumentMetadata(wordDocument, structuralHints);

                var mainPart = wordDocument.MainDocumentPart;
                if (mainPart?.Document?.Body != null)
                {
                    structuralHints["paragraph_count"] = mainPart.Document.Body.Elements<Paragraph>().Count();
                    structuralHints["table_count"] = mainPart.Document.Body.Elements<Table>().Count();
                }
            }

            // Post-process markdown
            markdown = PostProcessMarkdown(markdown);

            // Update structural hints
            structuralHints["file_type"] = "word_document";
            structuralHints["character_count"] = markdown.Length;
            structuralHints["WordCount"] = CountWords(markdown);
            structuralHints["conversion_method"] = "two-hop";

            if (imageCount > 0)
            {
                structuralHints["image_count"] = imageCount;
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
        catch (Exception ex)
        {
            warnings.Add($"Error processing Word document: {ex.Message}");
            return CreateEmptyResult(fileInfo, warnings, structuralHints);
        }
    }

    private static RawContent ExtractWordContentFromStream(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var extractedImages = new List<ImageInfo>();

        try
        {
            // Need to copy stream for multiple reads
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            memoryStream.Position = 0;

            // Two-hop conversion: DOCX → HTML → Markdown
            var mammothConverter = new DocumentConverter();
            var result = mammothConverter.ConvertToHtml(memoryStream);

            // Collect warnings from Mammoth
            foreach (var warning in result.Warnings)
            {
                warnings.Add($"Mammoth: {warning}");
            }

            // Extract base64 images and replace with placeholders in HTML
            var (processedHtml, images) = ExtractBase64ImagesFromHtml(result.Value);
            extractedImages = images;

            // Convert HTML to Markdown
            var markdown = MarkdownConverter.Convert(processedHtml);

            // Clean up HTML artifacts in markdown output
            markdown = CleanupMarkdown(markdown);

            // Count images (both extracted and remaining)
            var imageCount = extractedImages.Count + CountImages(processedHtml);

            // Extract metadata using OpenXML
            memoryStream.Position = 0;
            using (var wordDocument = WordprocessingDocument.Open(memoryStream, false))
            {
                ExtractDocumentMetadata(wordDocument, structuralHints);

                var mainPart = wordDocument.MainDocumentPart;
                if (mainPart?.Document?.Body != null)
                {
                    structuralHints["paragraph_count"] = mainPart.Document.Body.Elements<Paragraph>().Count();
                    structuralHints["table_count"] = mainPart.Document.Body.Elements<Table>().Count();
                }
            }

            // Post-process markdown
            markdown = PostProcessMarkdown(markdown);

            // Update structural hints
            structuralHints["file_type"] = "word_document";
            structuralHints["character_count"] = markdown.Length;
            structuralHints["WordCount"] = CountWords(markdown);
            structuralHints["conversion_method"] = "two-hop";

            if (imageCount > 0)
            {
                structuralHints["image_count"] = imageCount;
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
                    Size = memoryStream.Length,
                    CreatedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow
                },
                Hints = structuralHints,
                Warnings = warnings,
                Images = extractedImages,
                ReaderType = "WordReader"
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Error processing Word document from stream: {ex.Message}");
            return CreateEmptyStreamResult(stream, fileName, warnings, structuralHints);
        }
    }

    /// <summary>
    /// Extract base64 images from HTML and replace with placeholders
    /// </summary>
    private static (string ProcessedHtml, List<ImageInfo> Images) ExtractBase64ImagesFromHtml(string html)
    {
        var images = new List<ImageInfo>();
        var imageIndex = 0;

        var processedHtml = Base64ImageHtmlRegex().Replace(html, match =>
        {
            try
            {
                var mimeType = match.Groups[1].Value;
                var base64Data = match.Groups[2].Value;

                // Clean whitespace from base64
                var cleanBase64 = System.Text.RegularExpressions.Regex.Replace(base64Data, @"\s", "");
                var imageBytes = Convert.FromBase64String(cleanBase64);

                imageIndex++;
                var imageId = $"img_{imageIndex:D3}";

                var imageInfo = new ImageInfo
                {
                    Id = imageId,
                    MimeType = $"image/{mimeType}",
                    Data = imageBytes,
                    OriginalSize = imageBytes.Length,
                    SourceUrl = $"embedded:{imageId}"
                };

                images.Add(imageInfo);

                // Return placeholder image tag
                return $"<img src=\"embedded:{imageId}\" alt=\"{imageId}\" />";
            }
            catch
            {
                // If extraction fails, remove the image entirely
                return string.Empty;
            }
        });

        return (processedHtml, images);
    }

    private static string PostProcessMarkdown(string markdown)
    {
        // Clean up excessive blank lines
        markdown = ExcessiveNewlinesRegex().Replace(markdown, "\n\n");

        // Ensure proper spacing around headers
        markdown = HeaderSpacingRegex().Replace(markdown, "\n\n$1");

        // Clean up table formatting if needed
        markdown = markdown.Replace("| \n", "|\n");

        // Clean up image alt text: extract filename from full path
        // Pattern: ![C:\Users\...\file.jpg](url) => ![file.jpg](url)
        markdown = ImageAltPathRegex().Replace(markdown, match =>
        {
            var altText = match.Groups[1].Value;
            var url = match.Groups[2].Value;
            var cleanAltText = FileNameHelper.ExtractFileNameFromPathOrText(altText);
            return $"![{cleanAltText}]({url})";
        });

        return markdown.Trim();
    }

    private static int CountImages(string html)
    {
        return ImageTagRegex().Matches(html).Count;
    }

    /// <summary>
    /// Clean up HTML artifacts in markdown output
    /// </summary>
    private static string CleanupMarkdown(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return markdown;

        // Remove null bytes (invalid in UTF-8 text)
        markdown = TextSanitizer.RemoveNullBytes(markdown);

        // Convert HTML lists to markdown
        markdown = ConvertHtmlListsToMarkdown(markdown);

        // Decode common HTML entities
        markdown = DecodeHtmlEntities(markdown);

        // Replace <br> and <br/> tags with proper markdown line breaks
        markdown = BrTagRegex().Replace(markdown, "  \n");

        // Remove other common HTML tags that might slip through
        markdown = CommonHtmlTagsRegex().Replace(markdown, "");

        // Remove any remaining HTML tags
        markdown = RemainingHtmlTagsRegex().Replace(markdown, "");

        // Normalize multiple consecutive newlines to max 2
        markdown = MultipleNewlinesRegex().Replace(markdown, "\n\n");

        return markdown.Trim();
    }

    /// <summary>
    /// Converts HTML ordered and unordered lists to markdown format.
    /// </summary>
    private static string ConvertHtmlListsToMarkdown(string text)
    {
        // Remove the opening/closing list tags
        text = Regex.Replace(text, @"<ol[^>]*>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</ol>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<ul[^>]*>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</ul>", "", RegexOptions.IgnoreCase);

        // Convert all <li>...</li> to markdown list items
        text = Regex.Replace(text, @"<li[^>]*>(.*?)</li>", match =>
        {
            var content = match.Groups[1].Value.Trim();
            // Remove any nested HTML tags
            content = Regex.Replace(content, @"<[^>]+>", "");
            // Decode HTML entities in the content
            content = DecodeHtmlEntities(content);
            return $"\n- {content}";
        }, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Handle standalone <li> tags without closing tag
        text = Regex.Replace(text, @"<li[^>]*>([^<]+)(?=<|$)", match =>
        {
            var content = match.Groups[1].Value.Trim();
            return $"\n- {content}";
        }, RegexOptions.IgnoreCase);

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

        return text;
    }

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BrTagRegex();

    [GeneratedRegex(@"</?(?:span|div|p)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CommonHtmlTagsRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.Compiled)]
    private static partial Regex MultipleNewlinesRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex RemainingHtmlTagsRegex();

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        return text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static void ExtractDocumentMetadata(WordprocessingDocument document, Dictionary<string, object> structuralHints)
    {
        try
        {
            var coreProperties = document.PackageProperties;
            if (coreProperties != null)
            {
                if (!string.IsNullOrWhiteSpace(coreProperties.Title))
                    structuralHints["document_title"] = coreProperties.Title;

                if (!string.IsNullOrWhiteSpace(coreProperties.Creator))
                    structuralHints["author"] = coreProperties.Creator;

                if (!string.IsNullOrWhiteSpace(coreProperties.Subject))
                    structuralHints["subject"] = coreProperties.Subject;

                if (!string.IsNullOrWhiteSpace(coreProperties.Keywords))
                    structuralHints["keywords"] = coreProperties.Keywords
                        .Split(',', ';')
                        .Select(k => k.Trim())
                        .Where(k => !string.IsNullOrWhiteSpace(k))
                        .ToArray();

                if (!string.IsNullOrWhiteSpace(coreProperties.Description))
                    structuralHints["description"] = coreProperties.Description;

                if (coreProperties.Created.HasValue)
                    structuralHints["created_date"] = coreProperties.Created.Value;

                if (coreProperties.Modified.HasValue)
                    structuralHints["modified_date"] = coreProperties.Modified.Value;

                if (!string.IsNullOrWhiteSpace(coreProperties.LastModifiedBy))
                    structuralHints["last_modified_by"] = coreProperties.LastModifiedBy;
            }
        }
        catch
        {
            // 메타데이터 추출 실패는 무시
        }
    }

    private static RawContent CreateEmptyResult(FileInfo fileInfo, List<string> warnings, Dictionary<string, object> structuralHints)
    {
        return new RawContent
        {
            Text = string.Empty,
            File = new SourceFileInfo
            {
                Name = fileInfo.Name,
                Extension = ".docx",
                Size = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc,
                ModifiedAt = fileInfo.LastWriteTimeUtc
            },
            Hints = structuralHints,
            Warnings = warnings,
            ReaderType = "WordReader"
        };
    }

    private static RawContent CreateEmptyStreamResult(Stream stream, string fileName, List<string> warnings, Dictionary<string, object> structuralHints)
    {
        return new RawContent
        {
            Text = string.Empty,
            File = new SourceFileInfo
            {
                Name = fileName,
                Extension = ".docx",
                Size = stream.Length,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            },
            Hints = structuralHints,
            Warnings = warnings,
            ReaderType = "WordReader"
        };
    }

    // Generated regex patterns for performance
    [GeneratedRegex(@"^#{1,6}\s", RegexOptions.Multiline)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"^[\*\-\+]\s|^\d+\.\s", RegexOptions.Multiline)]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"\[.+?\]\(.+?\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessiveNewlinesRegex();

    [GeneratedRegex(@"\n(#{1,6}\s)")]
    private static partial Regex HeaderSpacingRegex();

    [GeneratedRegex(@"<img\s", RegexOptions.IgnoreCase)]
    private static partial Regex ImageTagRegex();

    [GeneratedRegex(@"!\[([^\]]*)\]\(([^)]+)\)")]
    private static partial Regex ImageAltPathRegex();

    // Match base64 images in HTML: <img src="data:image/png;base64,..." />
    [GeneratedRegex(@"<img[^>]*\ssrc\s*=\s*[""']data:image/(\w+);base64,([A-Za-z0-9+/\s=]+)[""'][^>]*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex Base64ImageHtmlRegex();
}
