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
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to read legacy Excel document: {ex.Message}", ex);
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

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        try
        {
            var fileInfo = new FileInfo(filePath);
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);

            var extraction = ExtractWorkbook(bytes, cancellationToken);

            return new RawContent
            {
                Text = extraction.Markdown,
                File = new SourceFileInfo
                {
                    Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                    Extension = ".xls",
                    Size = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    ModifiedAt = fileInfo.LastWriteTimeUtc
                },
                Hints = extraction.Hints,
                Warnings = extraction.Warnings,
                ReaderType = ReaderType
            };
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to extract legacy Excel document: {ex.Message}", ex);
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

            var extraction = ExtractWorkbook(bytes, cancellationToken);

            return new RawContent
            {
                Text = extraction.Markdown,
                File = new SourceFileInfo
                {
                    Name = fileName,
                    Extension = ".xls",
                    Size = bytes.Length,
                    CreatedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow
                },
                Hints = extraction.Hints,
                Warnings = extraction.Warnings,
                ReaderType = ReaderType
            };
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(fileName, $"Failed to extract legacy Excel document from stream: {ex.Message}", ex);
        }
    }

    // ========================================
    // Internals
    // ========================================

    private sealed record WorkbookExtraction(string Markdown, Dictionary<string, object> Hints, List<string> Warnings);

    private static IExcelDataReader CreateReader(Stream stream)
    {
        return ExcelReaderFactory.CreateBinaryReader(stream, new ExcelReaderConfiguration
        {
            // BIFF5/7 files carry a codepage; when absent, prefer CP949 (EUC-KR)
            // for parity with CsvDocumentReader's legacy Korean document fallback.
            FallbackEncoding = Encoding.GetEncoding(949)
        });
    }

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

            // Trim fully-empty trailing rows; skip sheets with no content at all
            while (rows.Count > 0 && rows[^1].All(string.IsNullOrWhiteSpace))
                rows.RemoveAt(rows.Count - 1);

            if (rows.Count == 0)
            {
                emptySheets++;
                continue;
            }

            if (sb.Length > 0)
                sb.AppendLine();

            sb.Append("## ").AppendLine(sheetName);
            sb.AppendLine();

            var header = rows[0];
            var columnCount = header.Length;

            AppendMarkdownRow(sb, header, columnCount);
            sb.Append("| ");
            sb.Append(string.Join(" | ", Enumerable.Repeat("---", columnCount)));
            sb.AppendLine(" |");

            for (var r = 1; r < rows.Count; r++)
            {
                AppendMarkdownRow(sb, rows[r], columnCount);
            }
        } while (reader.NextResult());

        hints["worksheet_count"] = sheetCount;
        hints["has_tables"] = sheetCount > emptySheets;

        if (emptySheets > 0)
            warnings.Add($"{emptySheets} of {sheetCount} worksheet(s) contain no data.");

        if (sheetCount == emptySheets)
            warnings.Add("Workbook contains no extractable cell data.");

        var markdown = sb.ToString().TrimEnd();
        hints["character_count"] = markdown.Length;

        return new WorkbookExtraction(markdown, hints, warnings);
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

    private static void AppendMarkdownRow(StringBuilder sb, string[] cells, int columnCount)
    {
        sb.Append('|');
        for (var c = 0; c < Math.Max(columnCount, cells.Length); c++)
        {
            var cell = c < cells.Length ? cells[c] : string.Empty;
            sb.Append(' ').Append(EscapeMarkdownCell(cell)).Append(" |");
        }
        sb.AppendLine();
    }

    private static string EscapeMarkdownCell(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // Pipe breaks table columns; embedded line breaks break table rows
        return value
            .Replace("|", "\\|")
            .Replace("\r\n", "<br>")
            .Replace("\n", "<br>")
            .Replace("\r", "<br>")
            .Trim();
    }
}
