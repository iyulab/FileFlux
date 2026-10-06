using ExcelDataReader;
using FileFlux.Core;
using System.Globalization;
using System.Text;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Legacy Microsoft Excel (.xls, BIFF binary) reader using ExcelDataReader.
/// Serializes each worksheet as a markdown table (same output contract as the
/// .xlsx ExcelDocumentReader and CsvDocumentReader) to preserve table semantics.
/// BIFF5/7 files without an explicit codepage fall back to CP949 (EUC-KR),
/// consistent with CsvDocumentReader's Korean legacy-document handling.
/// </summary>
public class LegacyExcelDocumentReader : IDocumentReader
{
    static LegacyExcelDocumentReader()
    {
        // BIFF codepage strings require the CodePages provider on .NET (idempotent)
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public string ReaderType => "LegacyExcelReader";

    public IEnumerable<string> SupportedExtensions => [".xls"];

    public bool CanRead(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension == ".xls";
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
            var result = new ReadResult
            {
                File = new SourceFileInfo
                {
                    Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                    Extension = ".xls",
                    Size = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    ModifiedAt = fileInfo.LastWriteTimeUtc
                },
                ReaderType = ReaderType
            };

            using var stream = File.OpenRead(filePath);
            using var reader = CreateReader(stream);

            var sheetNumber = 0;
            do
            {
                sheetNumber++;
                result.Pages.Add(new PageInfo
                {
                    Number = sheetNumber,
                    HasContent = reader.RowCount > 0,
                    Props =
                    {
                        ["sheet_name"] = reader.Name ?? $"Sheet{sheetNumber}",
                        ["file_type"] = "excel_worksheet"
                    }
                });
            } while (reader.NextResult());

            result.DocumentProps["section_count"] = sheetNumber;
            result.Duration = DateTime.UtcNow - startTime;
            return await Task.FromResult(result).ConfigureAwait(false);
        }
        catch (ExcelDataReader.Exceptions.InvalidPasswordException ex)
        {
            // A legacy .xls protected the old way carries a BIFF FILEPASS record rather than an
            // EncryptedPackage stream, so the container probe cannot see it - ExcelDataReader is
            // where it becomes knowable. Same condition as the OOXML case, so the same answer:
            // permanent, and named as encryption rather than as a generic read failure.
            throw new EncryptedDocumentException(filePath, ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to read legacy Excel document: {ex.Message}", ex);
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
            memoryStream.Position = 0;

            var result = new ReadResult
            {
                File = new SourceFileInfo
                {
                    Name = fileName,
                    Extension = ".xls",
                    Size = memoryStream.Length,
                    CreatedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow
                },
                ReaderType = ReaderType
            };

            using var reader = CreateReader(memoryStream);

            var sheetNumber = 0;
            do
            {
                sheetNumber++;
                result.Pages.Add(new PageInfo
                {
                    Number = sheetNumber,
                    HasContent = reader.RowCount > 0,
                    Props =
                    {
                        ["sheet_name"] = reader.Name ?? $"Sheet{sheetNumber}",
                        ["file_type"] = "excel_worksheet"
                    }
                });
            } while (reader.NextResult());

            result.DocumentProps["section_count"] = sheetNumber;
            result.Duration = DateTime.UtcNow - startTime;
            return result;
        }
        catch (ExcelDataReader.Exceptions.InvalidPasswordException ex)
        {
            // A legacy .xls protected the old way carries a BIFF FILEPASS record rather than an
            // EncryptedPackage stream, so the container probe cannot see it - ExcelDataReader is
            // where it becomes knowable. Same condition as the OOXML case, so the same answer:
            // permanent, and named as encryption rather than as a generic read failure.
            throw new EncryptedDocumentException(fileName, ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(fileName, $"Failed to read legacy Excel document from stream: {ex.Message}", ex);
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
            // The mirror of the .xlsx-named compound file: an OOXML package saved with a .xls name.
            // Renaming happens in both directions, so trusting the extension fails both ways.
            if (ContainerSignature.DetectFile(filePath) == OfficeContainer.Zip)
            {
                return await Task.Run(
                    () => ExcelDocumentReader.ExtractExcelContent(filePath, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }

            var fileInfo = new FileInfo(filePath);
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);

            return ExtractRawFromBytes(
                bytes,
                new SourceFileInfo
                {
                    Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                    Extension = ".xls",
                    Size = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    ModifiedAt = fileInfo.LastWriteTimeUtc
                },
                cancellationToken);
        }
        catch (ExcelDataReader.Exceptions.InvalidPasswordException ex)
        {
            // A legacy .xls protected the old way carries a BIFF FILEPASS record rather than an
            // EncryptedPackage stream, so the container probe cannot see it - ExcelDataReader is
            // where it becomes knowable. Same condition as the OOXML case, so the same answer:
            // permanent, and named as encryption rather than as a generic read failure.
            throw new EncryptedDocumentException(filePath, ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to extract legacy Excel document: {ex.Message}", ex);
        }
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

            // Same routing as the file path above.
            if (ContainerSignature.Detect(bytes) == OfficeContainer.Zip)
            {
                return await Task.Run(
                    () => ExcelDocumentReader.ExtractExcelContentFromBytes(bytes, fileName, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }

            return ExtractRawFromBytes(
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
        catch (ExcelDataReader.Exceptions.InvalidPasswordException ex)
        {
            // A legacy .xls protected the old way carries a BIFF FILEPASS record rather than an
            // EncryptedPackage stream, so the container probe cannot see it - ExcelDataReader is
            // where it becomes knowable. Same condition as the OOXML case, so the same answer:
            // permanent, and named as encryption rather than as a generic read failure.
            throw new EncryptedDocumentException(fileName, ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(fileName, $"Failed to extract legacy Excel document from stream: {ex.Message}", ex);
        }
    }

    // ========================================
    // Internals
    // ========================================

    /// <summary>
    /// Extracts without checking the declared extension, for use when the caller has already
    /// established from the content that this is a compound-file workbook. The public entry points
    /// keep their extension check so direct callers still get told when they picked the wrong reader.
    /// </summary>
    internal static RawContent ExtractRawFromBytes(
        byte[] bytes,
        SourceFileInfo file,
        CancellationToken cancellationToken)
    {
        var extraction = ExtractWorkbook(bytes, cancellationToken);

        return new RawContent
        {
            Text = extraction.Markdown,
            File = file,
            Tables = extraction.Tables,
            Spans = extraction.Spans,
            Hints = extraction.Hints,
            Warnings = extraction.Warnings,
            ReaderType = "LegacyExcelReader"
        };
    }

    private sealed record WorkbookExtraction(
        string Markdown,
        List<TableData> Tables,
        List<SourceSpan> Spans,
        Dictionary<string, object> Hints,
        List<string> Warnings);

    private static IExcelDataReader CreateReader(Stream stream)
    {
        return ExcelReaderFactory.CreateBinaryReader(stream, new ExcelReaderConfiguration
        {
            // BIFF5/7 files carry a codepage; when absent, prefer CP949 (EUC-KR)
            // for parity with CsvDocumentReader's legacy Korean document fallback.
            FallbackEncoding = Encoding.GetEncoding(949)
        });
    }

    /// <summary>
    /// The workbook as text and tables, in the same shape as the .xlsx reader: each non-empty sheet is a <c>## name</c>
    /// heading followed by its cells as one table (<see cref="TableMarkdown"/>), sheets separated by a rule. The table is
    /// also returned as <see cref="TableData"/> (first row as header, merged ranges as <see cref="MergedCell"/>, the
    /// sheet's 1-based position as <see cref="TableData.PageNumber"/>) and each sheet's text is a span.
    /// </summary>
    private static WorkbookExtraction ExtractWorkbook(byte[] bytes, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var hints = new Dictionary<string, object>
        {
            ["file_type"] = "excel_workbook",
            ["conversion_method"] = "exceldatareader"
        };

        using var stream = new MemoryStream(bytes);
        using var reader = CreateReader(stream);

        var sb = new StringBuilder();
        var tables = new List<TableData>();
        var spans = new List<SourceSpan>();
        var sheetCount = 0;
        var emptySheets = 0;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            sheetCount++;
            var sheetName = reader.Name ?? $"Sheet{sheetCount}";

            var rows = new List<string[]>();
            while (reader.Read())
            {
                var cells = new string[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    cells[i] = FormatCell(reader.GetValue(i));
                }
                rows.Add(cells);
            }

            var merges = (reader.MergeCells ?? []).Select(range => (range.FromRow, range.FromColumn, range.ToRow, range.ToColumn)).ToList();
            var table = ToTable(rows, merges, sheetCount, sheetName, tables.Count);
            if (table is null)
            {
                emptySheets++;
                continue;
            }

            if (sb.Length > 0)
                sb.Append("\n\n---\n\n");

            var start = sb.Length;
            sb.Append("## ").Append(sheetName).Append("\n\n").Append(TableMarkdown.Render(table));
            spans.Add(new SourceSpan(start, sb.Length) { Page = sheetCount });
            tables.Add(table);
        } while (reader.NextResult());

        hints["worksheet_count"] = sheetCount;
        hints["has_tables"] = tables.Count > 0;

        if (emptySheets > 0)
            warnings.Add($"{emptySheets} of {sheetCount} worksheet(s) contain no data.");

        if (sheetCount == emptySheets)
            warnings.Add("Workbook contains no extractable cell data.");

        var markdown = sb.ToString();
        hints["character_count"] = markdown.Length;

        return new WorkbookExtraction(markdown, tables, spans, hints, warnings);
    }

    /// <summary>
    /// One sheet's cells as a table, or <c>null</c> when the sheet has no data. Fully empty trailing rows and columns are
    /// trimmed; a merged range keeps its value at its top-left cell (where the file stores it).
    /// </summary>
    internal static TableData? ToTable(
        List<string[]> rows,
        IReadOnlyList<(int FromRow, int FromColumn, int ToRow, int ToColumn)> mergeCells,
        int sheetNumber,
        string sheetName,
        int order)
    {
        while (rows.Count > 0 && rows[^1].All(string.IsNullOrWhiteSpace))
            rows.RemoveAt(rows.Count - 1);

        var columns = rows.Count == 0 ? 0 : rows.Max(row => row.Length);
        while (columns > 0 && rows.All(row => row.Length < columns || string.IsNullOrWhiteSpace(row[columns - 1])))
            columns--;

        if (rows.Count == 0 || columns == 0)
            return null;

        var cells = rows
            .Select(row => Enumerable.Range(0, columns).Select(c => c < row.Length ? row[c].Trim() : string.Empty).ToArray())
            .ToArray();

        var merged = mergeCells
            .Where(range => range.FromRow < cells.Length && range.FromColumn < columns)
            .Select(range => new MergedCell
            {
                StartRow = range.FromRow,
                EndRow = Math.Min(range.ToRow, cells.Length - 1),
                StartCol = range.FromColumn,
                EndCol = Math.Min(range.ToColumn, columns - 1),
                Content = cells[range.FromRow][range.FromColumn]
            })
            .Where(cell => cell.EndRow > cell.StartRow || cell.EndCol > cell.StartCol)
            .ToList();

        var table = new TableData
        {
            Cells = cells,
            HasHeader = true,
            MergedCells = merged,
            DetectionMethod = TableDetectionMethod.Structured,
            Confidence = 1.0,
            PageNumber = sheetNumber,
            Order = order
        };
        table.Props["header_rows"] = 1;
        table.Props["section_name"] = sheetName;
        return table;
    }

    private static string FormatCell(object? value) => value switch
    {
        null => string.Empty,
        DateTime dt when dt.TimeOfDay == TimeSpan.Zero => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        double d => d.ToString("0.############", CultureInfo.InvariantCulture),
        bool b => b ? "TRUE" : "FALSE",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };
}
