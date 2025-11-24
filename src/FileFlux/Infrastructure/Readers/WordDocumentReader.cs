using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FileFlux.Domain;
using FileFlux.Exceptions;
using FileFlux.Infrastructure.Utils;
using Mammoth;
using ReverseMarkdown;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Readers;

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

    public async Task<RawContent> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
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

    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
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

                // Convert HTML to Markdown
                markdown = MarkdownConverter.Convert(result.Value);

                // Count images in HTML
                imageCount = CountImages(result.Value);
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

            // Convert HTML to Markdown
            var markdown = MarkdownConverter.Convert(result.Value);

            // Count images in HTML
            var imageCount = CountImages(result.Value);

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
                ReaderType = "WordReader"
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Error processing Word document from stream: {ex.Message}");
            return CreateEmptyStreamResult(stream, fileName, warnings, structuralHints);
        }
    }

    private static string PostProcessMarkdown(string markdown)
    {
        // Clean up excessive blank lines
        markdown = ExcessiveNewlinesRegex().Replace(markdown, "\n\n");

        // Ensure proper spacing around headers
        markdown = HeaderSpacingRegex().Replace(markdown, "\n\n$1");

        // Clean up table formatting if needed
        markdown = markdown.Replace("| \n", "|\n");

        return markdown.Trim();
    }

    private static int CountImages(string html)
    {
        return ImageTagRegex().Matches(html).Count;
    }

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
}
