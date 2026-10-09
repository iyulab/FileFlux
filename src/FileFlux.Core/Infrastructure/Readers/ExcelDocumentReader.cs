using System.Text;
using FileFlux.Core;
using Undoc;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Microsoft Excel workbook reader (.xlsx, and the binary .xls of Excel 2.x-2003) using Undoc (Rust FFI).
/// High-performance native library for Office document extraction.
/// </summary>
public class ExcelDocumentReader : IDocumentReader
{
    public string ReaderType => "ExcelReader";

    // The OOXML package and its binary (BIFF) predecessors: a compound file holding a Workbook (Excel 97-2003) or Book
    // (Excel 5.0/95) stream, or a bare BIFF2-4 stream (Excel 2.x-4.0). The Office parser reads all of them.
    private static readonly string[] Workbooks = [".xlsx", ".xls"];

    public IEnumerable<string> SupportedExtensions => Workbooks;

    /// <summary>
    /// The extension of what was parsed: the content, not the file name. A bare BIFF2-4 stream is no Office container,
    /// so the container alone cannot tell; what <see cref="FormatSignature"/> recognised decides, and the container is
    /// the fallback for content it leaves undecided.
    /// </summary>
    private static string ExtensionOf(string? detected, OfficeContainer container)
        => detected is ".xls" or ".xlsx" ? detected
            : container == OfficeContainer.CompoundFile ? ".xls" : ".xlsx";

    public bool CanRead(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension is ".xlsx" or ".xls";
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

        if (!FormatSignature.Accepts(SupportedExtensions, filePath, () => FormatSignature.DetectFile(filePath)))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        var startTime = DateTime.UtcNow;
        var fileInfo = new FileInfo(filePath);

        try
        {
            var detected = FormatSignature.DetectFile(filePath);
            FormatSignature.ThrowIfAnotherReadersFormat(detected, Workbooks, filePath, "Failed to read Excel document");

            var result = new ReadResult
            {
                File = new SourceFileInfo
                {
                    Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                    Extension = ExtensionOf(detected, ContainerSignature.DetectFile(filePath)),
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
            var sheetNames = ParserTableJson.ReadSections(doc.ToJson(compact: true)).Select(sheet => sheet.Name).ToList();

            // Each section represents a worksheet
            for (int i = 1; i <= doc.SectionCount; i++)
            {
                result.Pages.Add(new PageInfo
                {
                    Number = i,
                    HasContent = true,
                    Props =
                    {
                        ["sheet_name"] = sheetNames.ElementAtOrDefault(i - 1) ?? $"Sheet{i}",
                        ["file_type"] = "excel_worksheet"
                    }
                });
            }

            result.Duration = DateTime.UtcNow - startTime;
            return await Task.FromResult(result).ConfigureAwait(false);
        }
        catch (UndocException ex)
        {
            throw UndocErrorKindFormatting.ToFailure(ex, filePath, $"Failed to read Excel document: {ex.Message}");
        }
        catch (Exception ex) when (ex is not FileFluxException && (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
        {
            throw new DocumentProcessingException(filePath, $"Failed to read Excel document: {ex.Message}", ex);
        }
    }

    public async Task<ReadResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!FormatSignature.Accepts(SupportedExtensions, fileName, () => FormatSignature.DetectStream(stream)))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        var startTime = DateTime.UtcNow;

        try
        {
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            var bytes = memoryStream.ToArray();

            var detected = FormatSignature.DetectBytes(bytes);
            FormatSignature.ThrowIfAnotherReadersFormat(detected, Workbooks, fileName, "Failed to read Excel document");

            var result = new ReadResult
            {
                File = new SourceFileInfo
                {
                    Name = fileName,
                    Extension = ExtensionOf(detected, ContainerSignature.Detect(bytes)),
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
            var sheetNames = ParserTableJson.ReadSections(doc.ToJson(compact: true)).Select(sheet => sheet.Name).ToList();

            for (int i = 1; i <= doc.SectionCount; i++)
            {
                result.Pages.Add(new PageInfo
                {
                    Number = i,
                    HasContent = true,
                    Props =
                    {
                        ["sheet_name"] = sheetNames.ElementAtOrDefault(i - 1) ?? $"Sheet{i}",
                        ["file_type"] = "excel_worksheet"
                    }
                });
            }

            result.Duration = DateTime.UtcNow - startTime;
            return result;
        }
        catch (UndocException ex)
        {
            throw UndocErrorKindFormatting.ToFailure(ex, fileName, $"Failed to read Excel document from stream: {ex.Message}");
        }
        catch (Exception ex) when (ex is not FileFluxException && (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
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

        if (!FormatSignature.Accepts(SupportedExtensions, filePath, () => FormatSignature.DetectFile(filePath)))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        try
        {
            return ImageExtractionPolicy.Apply(await Task.Run(() => ExtractExcelContent(filePath, cancellationToken), cancellationToken).ConfigureAwait(false), options);
        }
        catch (UndocException ex)
        {
            throw UndocErrorKindFormatting.ToFailure(ex, filePath, DescribeExtractionFailure(filePath, ex.Message));
        }
        catch (Exception ex) when (ex is not FileFluxException && (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
        {
            throw new DocumentProcessingException(
                filePath, DescribeExtractionFailure(filePath, ex.Message), ex);
        }
    }

    /// <summary>
    /// Tells a mislabelled file apart from a damaged one, so the message does not send its reader
    /// after corruption that is not there.
    /// </summary>
    /// <remarks>
    /// A workbook this reader parses keeps the parser's diagnosis whatever its container: a bare BIFF2-4 stream is
    /// neither Office container, yet it is no mislabelled file.
    /// </remarks>
    private static string DescribeExtractionFailure(string filePath, string message)
    {
        var failure = $"Failed to extract Excel document: {message}";
        var detected = FormatSignature.DetectFile(filePath);
        return detected is not null && Workbooks.Contains(detected, StringComparer.OrdinalIgnoreCase)
            ? failure
            : ContainerSignature.AnnotateFailure(
                failure, ContainerSignature.DetectFile(filePath), detected, OfficeContainer.Zip, OfficeContainer.CompoundFile);
    }

    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!FormatSignature.Accepts(SupportedExtensions, fileName, () => FormatSignature.DetectStream(stream)))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        try
        {
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            var bytes = memoryStream.ToArray();

            return ImageExtractionPolicy.Apply(await Task.Run(() => ExtractExcelContentFromBytes(bytes, fileName, cancellationToken), cancellationToken).ConfigureAwait(false), options);
        }
        catch (UndocException ex)
        {
            throw UndocErrorKindFormatting.ToFailure(ex, fileName, $"Failed to extract Excel document from stream: {ex.Message}");
        }
        catch (Exception ex) when (ex is not FileFluxException && (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
        {
            throw new DocumentProcessingException(fileName, $"Failed to extract Excel document from stream: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// The workbook at <paramref name="filePath"/>, whichever container it is: an OOXML package, a compound file
    /// (Excel 5.0-2003) or a bare BIFF2-4 stream (Excel 2.x-4.0) go through the same parser and come out in the same
    /// layout.
    /// </summary>
    private static RawContent ExtractExcelContent(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var container = ContainerSignature.DetectFile(filePath);

        // An encrypted OOXML workbook is a compound file wrapping the real package; without this it reaches the parser
        // looking like an ordinary container mismatch. It is a more specific condition with a different remedy. A
        // workbook protected the 97-2003 way carries no such stream; the parser reports that one as encrypted.
        if (container == OfficeContainer.CompoundFile)
            CompoundFileEncryption.ThrowIfEncrypted(File.ReadAllBytes(filePath), FileNameHelper.ExtractSafeFileName(fileInfo));

        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();

        var detected = FormatSignature.DetectFile(filePath);
        FormatSignature.ThrowIfAnotherReadersFormat(detected, Workbooks, filePath, "Failed to extract Excel document");
        cancellationToken.ThrowIfCancellationRequested();
        using var doc = UndocDocument.ParseFile(filePath);

        var workbook = RenderWorkbook(doc);
        var markdown = workbook.Text;

        // Extract metadata
        if (!string.IsNullOrWhiteSpace(doc.Title))
            structuralHints["document_title"] = doc.Title;
        if (!string.IsNullOrWhiteSpace(doc.Author))
            structuralHints["author"] = doc.Author;

        structuralHints["worksheet_count"] = doc.SectionCount;
        structuralHints["file_type"] = "excel_workbook";
        structuralHints["character_count"] = markdown.Length;
        structuralHints["conversion_method"] = "undoc_native";

        // A workbook whose sheets are all empty has no table.
        structuralHints["has_tables"] = workbook.Tables.Count > 0;
        var images = ExtractImages(doc, structuralHints);

        return new RawContent
        {
            Text = markdown,
            Tables = workbook.Tables,
            Spans = workbook.Spans,
            File = new SourceFileInfo
            {
                Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                // The content, not the file name: a consumer routing on this must see what was actually parsed, or it
                // inherits a mislabelling.
                Extension = ExtensionOf(detected, container),
                Size = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc,
                ModifiedAt = fileInfo.LastWriteTimeUtc
            },
            Hints = structuralHints,
            Warnings = warnings,
            Images = images,
            ReaderType = "ExcelReader"
        };
    }

    private static RawContent ExtractExcelContentFromBytes(byte[] bytes, string fileName, CancellationToken cancellationToken)
    {
        // Same encryption check as the file path - see ExtractExcelContent.
        var container = ContainerSignature.Detect(bytes);
        if (container == OfficeContainer.CompoundFile)
            CompoundFileEncryption.ThrowIfEncrypted(bytes, fileName);

        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();

        var detected = FormatSignature.DetectBytes(bytes);
        FormatSignature.ThrowIfAnotherReadersFormat(detected, Workbooks, fileName, "Failed to extract Excel document");
        cancellationToken.ThrowIfCancellationRequested();
        using var doc = UndocDocument.ParseBytes(bytes);

        var workbook = RenderWorkbook(doc);
        var markdown = workbook.Text;

        // Extract metadata
        if (!string.IsNullOrWhiteSpace(doc.Title))
            structuralHints["document_title"] = doc.Title;
        if (!string.IsNullOrWhiteSpace(doc.Author))
            structuralHints["author"] = doc.Author;

        structuralHints["worksheet_count"] = doc.SectionCount;
        structuralHints["file_type"] = "excel_workbook";
        structuralHints["character_count"] = markdown.Length;
        structuralHints["conversion_method"] = "undoc_native";

        // A workbook whose sheets are all empty has no table.
        structuralHints["has_tables"] = workbook.Tables.Count > 0;
        var images = ExtractImages(doc, structuralHints);

        return new RawContent
        {
            Text = markdown,
            Tables = workbook.Tables,
            Spans = workbook.Spans,
            File = new SourceFileInfo
            {
                Name = fileName,
                Extension = ExtensionOf(detected, container),
                Size = bytes.Length,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            },
            Hints = structuralHints,
            Warnings = warnings,
            Images = images,
            ReaderType = "ExcelReader"
        };
    }

    /// <summary>
    /// The pictures the workbook shows, each with the sheets that show it (<see cref="ImageInfo.PageNumbers"/>, the
    /// sheet's 1-based position, when the parser places it) and its alt text; sets <c>image_count</c>/<c>has_images</c>.
    /// The text does not reference them.
    /// </summary>
    private static List<ImageInfo> ExtractImages(UndocDocument doc, Dictionary<string, object> structuralHints)
    {
        var images = new List<ImageInfo>();
        var sheets = UndocImageResources.SectionsOfResources(doc);
        foreach (var (resourceId, altText) in UndocImageResources.Shown(doc))
        {
            var resourceData = doc.GetResourceData(resourceId);
            if (resourceData is not { Length: > 0 })
                continue;

            var image = new ImageInfo
            {
                Id = resourceId,
                MimeType = ImageMimeTypeDetector.Detect(resourceData, resourceId),
                Data = resourceData,
                OriginalSize = resourceData.Length,
                SourceUrl = $"embedded:{resourceId}",
                PageNumbers = sheets.TryGetValue(resourceId, out var shownOn) ? shownOn : []
            };
            ImageAltText.Attach(image, altText);
            images.Add(image);
        }

        if (images.Count > 0)
        {
            structuralHints["image_count"] = images.Count;
            structuralHints["has_images"] = true;
        }

        return images;
    }

    /// <summary>
    /// The workbook as text, written from Undoc's JSON rather than its Markdown: each sheet is a <c>## name</c> heading
    /// followed by its tables (<see cref="TableMarkdown"/>) and text blocks, sheets separated by a rule. Undoc's own
    /// Markdown pads a vertically merged cell twice and shifts the values to its right (undoc #792); its JSON places
    /// every value. The tables come back as <see cref="TableData"/> too, and each sheet's text is a span whose page is
    /// the sheet's 1-based position.
    /// </summary>
    internal static (string Text, List<TableData> Tables, List<SourceSpan> Spans) RenderWorkbook(UndocDocument doc)
    {
        var sections = ParserTableJson.ReadSections(doc.ToJson(compact: true));
        if (sections.Count == 0)
        {
            var fallback = ImageAltText.CleanMarkdown(TextSanitizer.RemoveNullBytes(doc.ToMarkdown(new MarkdownOptions
            {
                IncludeFrontmatter = false,
                EscapeSpecialChars = false,
                ParagraphSpacing = false
            }))).Trim();
            return (fallback, [], []);
        }

        var sb = new StringBuilder();
        var tables = new List<TableData>();
        var spans = new List<SourceSpan>();

        foreach (var sheet in sections)
        {
            if (sb.Length > 0)
                sb.Append("\n\n---\n\n");

            var start = sb.Length;
            sb.Append("## ").Append(TextSanitizer.RemoveNullBytes(sheet.Name ?? $"Sheet{sheet.Index + 1}"));
            foreach (var item in sheet.Items)
            {
                sb.Append("\n\n");
                if (item is TableData table)
                {
                    table.Cells = table.Cells.Select(row => row.Select(TextSanitizer.RemoveNullBytes).ToArray()).ToArray();
                    sb.Append(TableMarkdown.Render(table));
                    tables.Add(table);
                }
                else
                {
                    sb.Append(TextSanitizer.RemoveNullBytes((string)item));
                }
            }

            spans.Add(new SourceSpan(start, sb.Length) { Page = sheet.Index + 1 });
        }

        return (sb.ToString(), tables, spans);
    }
}
