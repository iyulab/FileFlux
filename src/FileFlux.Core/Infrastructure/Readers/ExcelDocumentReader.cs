using FileFlux.Core;
using Undoc;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Microsoft Excel document (.xlsx) reader using Undoc (Rust FFI).
/// High-performance native library for Office document extraction.
/// </summary>
public class ExcelDocumentReader : IDocumentReader
{
    public string ReaderType => "ExcelReader";

    public IEnumerable<string> SupportedExtensions => [".xlsx"];

    public bool CanRead(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension == ".xlsx";
    }

    // ========================================
    // Stage 0: Read (Document Structure)
    // ========================================

    public async Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Excel document not found: {filePath}");

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
                    Extension = ".xlsx",
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

            // Each section represents a worksheet
            for (int i = 1; i <= doc.SectionCount; i++)
            {
                result.Pages.Add(new PageInfo
                {
                    Number = i,
                    HasContent = true,
                    Props =
                    {
                        ["sheet_name"] = $"Sheet{i}",
                        ["file_type"] = "excel_worksheet"
                    }
                });
            }

            result.Duration = DateTime.UtcNow - startTime;
            return await Task.FromResult(result).ConfigureAwait(false);
        }
        catch (UndocException ex)
        {
            throw new DocumentProcessingException(filePath, $"Failed to read Excel document: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to read Excel document: {ex.Message}", ex);
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
                    Extension = ".xlsx",
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

            for (int i = 1; i <= doc.SectionCount; i++)
            {
                result.Pages.Add(new PageInfo
                {
                    Number = i,
                    HasContent = true,
                    Props =
                    {
                        ["sheet_name"] = $"Sheet{i}",
                        ["file_type"] = "excel_worksheet"
                    }
                });
            }

            result.Duration = DateTime.UtcNow - startTime;
            return result;
        }
        catch (UndocException ex)
        {
            throw new DocumentProcessingException(fileName, $"Failed to read Excel document from stream: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(fileName, $"Failed to read Excel document from stream: {ex.Message}", ex);
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
            throw new FileNotFoundException($"Excel document not found: {filePath}");

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        try
        {
            return await Task.Run(() => ExtractExcelContent(filePath, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (UndocException ex)
        {
            throw new DocumentProcessingException(
                filePath, DescribeExtractionFailure(ContainerSignature.DetectFile(filePath), ex.Message), ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(
                filePath, DescribeExtractionFailure(ContainerSignature.DetectFile(filePath), ex.Message), ex);
        }
    }

    /// <summary>
    /// Tells a mislabelled file apart from a damaged one, so the message does not send its reader
    /// after corruption that is not there.
    /// </summary>
    private static string DescribeExtractionFailure(OfficeContainer container, string message)
        => ContainerSignature.AnnotateFailure(
            $"Failed to extract Excel document: {message}",
            container,
            OfficeContainer.Zip,
            OfficeContainer.CompoundFile);

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

            return await Task.Run(() => ExtractExcelContentFromBytes(bytes, fileName, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (UndocException ex)
        {
            throw new DocumentProcessingException(fileName, $"Failed to extract Excel document from stream: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(fileName, $"Failed to extract Excel document from stream: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Routes on the container the bytes actually are, not on the declared extension.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A legacy compound-file workbook saved with an <c>.xlsx</c> name reaches this reader, and the
    /// OOXML parser reports "could not find EOCD" — an accurate statement about a ZIP package that
    /// reads to a user as "your file is corrupt". The file is valid and a reader for it already
    /// exists, so the only thing wrong is which reader was chosen.
    /// </para>
    /// <para>
    /// The routing lives here rather than in the reader factory because the factory selects from a
    /// file name alone, and several call paths hand it only an extension. Deciding between the two
    /// Excel containers is also this reader's own subject matter: both are Excel.
    /// </para>
    /// </remarks>
    internal static RawContent ExtractExcelContent(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);

        if (ContainerSignature.DetectFile(filePath) == OfficeContainer.CompoundFile)
        {
            var bytes = File.ReadAllBytes(filePath);
            return LegacyExcelDocumentReader.ExtractRawFromBytes(
                bytes,
                new SourceFileInfo
                {
                    Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                    // The container, not the file name: a consumer routing on this must see what was
                    // actually parsed, or it inherits the same mislabelling.
                    Extension = ".xls",
                    Size = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    ModifiedAt = fileInfo.LastWriteTimeUtc
                },
                cancellationToken);
        }

        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();

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

        structuralHints["worksheet_count"] = doc.SectionCount;
        structuralHints["file_type"] = "excel_workbook";
        structuralHints["character_count"] = markdown.Length;
        structuralHints["conversion_method"] = "undoc_native";

        // Excel typically has tables
        structuralHints["has_tables"] = true;

        return new RawContent
        {
            Text = markdown.Trim(),
            File = new SourceFileInfo
            {
                Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                Extension = ".xlsx",
                Size = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc,
                ModifiedAt = fileInfo.LastWriteTimeUtc
            },
            Hints = structuralHints,
            Warnings = warnings,
            ReaderType = "ExcelReader"
        };
    }

    internal static RawContent ExtractExcelContentFromBytes(byte[] bytes, string fileName, CancellationToken cancellationToken)
    {
        // Same routing as the file path — see ExtractExcelContent.
        if (ContainerSignature.Detect(bytes) == OfficeContainer.CompoundFile)
        {
            return LegacyExcelDocumentReader.ExtractRawFromBytes(
                bytes,
                new SourceFileInfo
                {
                    Name = fileName,
                    Extension = ".xls",
                    Size = bytes.Length,
                    CreatedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow
                },
                cancellationToken);
        }

        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();

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

        structuralHints["worksheet_count"] = doc.SectionCount;
        structuralHints["file_type"] = "excel_workbook";
        structuralHints["character_count"] = markdown.Length;
        structuralHints["conversion_method"] = "undoc_native";

        // Excel typically has tables
        structuralHints["has_tables"] = true;

        return new RawContent
        {
            Text = markdown.Trim(),
            File = new SourceFileInfo
            {
                Name = fileName,
                Extension = ".xlsx",
                Size = bytes.Length,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            },
            Hints = structuralHints,
            Warnings = warnings,
            ReaderType = "ExcelReader"
        };
    }
}
