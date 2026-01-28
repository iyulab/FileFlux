using FileFlux.Core;
using System.Text.RegularExpressions;
using Unpdf;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// PDF document reader using Unpdf (Rust FFI).
/// High-performance native library for PDF content extraction to Markdown.
/// </summary>
public partial class PdfDocumentReader : IDocumentReader
{
    public string ReaderType => "PdfReader";

    public IEnumerable<string> SupportedExtensions => [".pdf"];

    public bool CanRead(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension == ".pdf";
    }

    // ========================================
    // Stage 0: Read (Document Structure)
    // ========================================

    public async Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PDF file not found: {filePath}");

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
                    Extension = ".pdf",
                    Size = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    ModifiedAt = fileInfo.LastWriteTimeUtc
                },
                ReaderType = ReaderType
            };

            // Get document info using Unpdf
            var docInfo = Pdf.GetInfo(filePath);

            if (!string.IsNullOrWhiteSpace(docInfo.Title))
                result.DocumentProps["title"] = docInfo.Title;
            if (!string.IsNullOrWhiteSpace(docInfo.Author))
                result.DocumentProps["author"] = docInfo.Author;
            if (!string.IsNullOrWhiteSpace(docInfo.Subject))
                result.DocumentProps["subject"] = docInfo.Subject;
            if (!string.IsNullOrWhiteSpace(docInfo.Creator))
                result.DocumentProps["creator"] = docInfo.Creator;
            if (!string.IsNullOrWhiteSpace(docInfo.Producer))
                result.DocumentProps["producer"] = docInfo.Producer;
            if (!string.IsNullOrWhiteSpace(docInfo.PdfVersion))
                result.DocumentProps["pdf_version"] = docInfo.PdfVersion;

            result.DocumentProps["page_count"] = docInfo.PageCount;
            result.DocumentProps["encrypted"] = docInfo.Encrypted;

            // Add page info
            for (int i = 1; i <= docInfo.PageCount; i++)
            {
                result.Pages.Add(new PageInfo
                {
                    Number = i,
                    HasContent = true,
                    Props = { ["file_type"] = "pdf_document" }
                });
            }

            result.Duration = DateTime.UtcNow - startTime;
            return await Task.FromResult(result).ConfigureAwait(false);
        }
        catch (UnpdfException ex)
        {
            throw new DocumentProcessingException(filePath, $"Failed to read PDF document: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to read PDF document: {ex.Message}", ex);
        }
    }

    public async Task<ReadResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!CanRead(fileName))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        // Unpdf requires file path, so save stream to temp file
        var tempPath = Path.Combine(Path.GetTempPath(), $"unpdf_{Guid.NewGuid():N}.pdf");
        try
        {
            await using (var fileStream = File.Create(tempPath))
            {
                await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            var result = await ReadAsync(tempPath, cancellationToken).ConfigureAwait(false);

            // Update file info to reflect original stream
            result.File = new SourceFileInfo
            {
                Name = fileName,
                Extension = ".pdf",
                Size = new FileInfo(tempPath).Length,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            };

            return result;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* ignore cleanup errors */ }
            }
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
            throw new FileNotFoundException($"PDF file not found: {filePath}");

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        try
        {
            return await Task.Run(() => ExtractPdfContent(filePath, options, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (UnpdfException ex)
        {
            throw new DocumentProcessingException(filePath, $"Failed to extract PDF document: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to extract PDF document: {ex.Message}", ex);
        }
    }

    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!CanRead(fileName))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        // Unpdf requires file path, so save stream to temp file
        var tempPath = Path.Combine(Path.GetTempPath(), $"unpdf_{Guid.NewGuid():N}.pdf");
        try
        {
            await using (var fileStream = File.Create(tempPath))
            {
                await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            var result = await Task.Run(() => ExtractPdfContent(tempPath, options, cancellationToken), cancellationToken).ConfigureAwait(false);

            // Update file info to reflect original stream
            result.File = new SourceFileInfo
            {
                Name = fileName,
                Extension = ".pdf",
                Size = new FileInfo(tempPath).Length,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            };

            return result;
        }
        catch (UnpdfException ex)
        {
            throw new DocumentProcessingException(fileName, $"Failed to extract PDF document from stream: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(fileName, $"Failed to extract PDF document from stream: {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* ignore cleanup errors */ }
            }
        }
    }

    private static RawContent ExtractPdfContent(string filePath, ExtractOptions? options, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();

        cancellationToken.ThrowIfCancellationRequested();

        // Convert to Markdown using Unpdf native library
        var markdown = Pdf.ToMarkdown(filePath);

        // Remove null bytes
        markdown = TextSanitizer.RemoveNullBytes(markdown);

        // Get document metadata
        var docInfo = Pdf.GetInfo(filePath);

        if (!string.IsNullOrWhiteSpace(docInfo.Title))
            structuralHints["document_title"] = docInfo.Title;
        if (!string.IsNullOrWhiteSpace(docInfo.Author))
            structuralHints["author"] = docInfo.Author;

        structuralHints["page_count"] = docInfo.PageCount;

        // Update structural hints
        structuralHints["file_type"] = "pdf_document";
        structuralHints["character_count"] = markdown.Length;
        structuralHints["word_count"] = CountWords(markdown);
        structuralHints["paragraph_count"] = CountParagraphs(markdown);
        structuralHints["conversion_method"] = "unpdf_native";

        // Detect structural elements from markdown
        var hasHeaders = HeaderRegex().IsMatch(markdown);
        var hasTables = markdown.Contains("|---", StringComparison.Ordinal) ||
                       markdown.Contains("| ---", StringComparison.Ordinal);
        var hasLists = ListRegex().IsMatch(markdown);
        var hasLinks = LinkRegex().IsMatch(markdown);
        var hasImages = ImageRegex().IsMatch(markdown);

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
                Extension = ".pdf",
                Size = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc,
                ModifiedAt = fileInfo.LastWriteTimeUtc
            },
            Hints = structuralHints,
            Warnings = warnings,
            ReaderType = "PdfReader"
        };
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

    [GeneratedRegex(@"!\[.*?\]\(.+?\)")]
    private static partial Regex ImageRegex();
}
