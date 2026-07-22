using CsvHelper;
using CsvHelper.Configuration;
using FileFlux.Core;
using System.Globalization;
using System.Text;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// CSV/TSV 파일 전용 Reader - 구조 인지(헤더 행 인식) 표 추출
/// 헤더 + 행을 마크다운 테이블로 직렬화하여 표 의미론 보존
/// RFC 4180 파싱(CsvHelper), UTF-8/BOM + CP949(EUC-KR) 폴백 디코딩 지원
/// </summary>
public class CsvDocumentReader : IDocumentReader
{
    static CsvDocumentReader()
    {
        // CP949(EUC-KR) 등 코드페이지 인코딩은 .NET에서 provider 등록 필요 (idempotent)
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public string ReaderType => "CsvReader";

    public IEnumerable<string> SupportedExtensions => [".csv", ".tsv"];

    public bool CanRead(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    // ========================================
    // Stage 0: Read (Document Structure)
    // ========================================

    public async Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        if (!CanRead(filePath))
            throw new NotSupportedException($"File format not supported: {Path.GetExtension(filePath)}");

        var startTime = DateTime.UtcNow;
        var fileInfo = new FileInfo(filePath);

        try
        {
            var result = new ReadResult
            {
                File = new SourceFileInfo
                {
                    Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                    Extension = Path.GetExtension(filePath).ToLowerInvariant(),
                    Size = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    ModifiedAt = fileInfo.LastWriteTimeUtc
                },
                ReaderType = ReaderType,
                Duration = DateTime.UtcNow - startTime
            };

            // CSV files are single-page tabular documents
            result.Pages.Add(new PageInfo
            {
                Number = 1,
                HasContent = fileInfo.Length > 0,
                Props = { ["file_type"] = Path.GetExtension(filePath).ToLowerInvariant().TrimStart('.') }
            });

            return await Task.FromResult(result).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to read CSV file: {ex.Message}", ex);
        }
    }

    public Task<ReadResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Filename cannot be null or empty", nameof(fileName));

        var startTime = DateTime.UtcNow;

        var result = new ReadResult
        {
            File = new SourceFileInfo
            {
                Name = fileName,
                Extension = Path.GetExtension(fileName).ToLowerInvariant(),
                Size = stream.CanSeek ? stream.Length : 0,
                CreatedAt = DateTime.UtcNow
            },
            ReaderType = ReaderType,
            Duration = DateTime.UtcNow - startTime
        };

        result.Pages.Add(new PageInfo
        {
            Number = 1,
            HasContent = !stream.CanSeek || stream.Length > 0,
            Props = { ["file_type"] = Path.GetExtension(fileName).ToLowerInvariant().TrimStart('.') }
        });

        return Task.FromResult(result);
    }

    // ========================================
    // Stage 1: Extract (Raw Content)
    // ========================================

    public async Task<RawContent> ExtractAsync(string filePath, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        if (!CanRead(filePath))
            throw new NotSupportedException($"File format not supported: {Path.GetExtension(filePath)}");

        try
        {
            var fileInfo = new FileInfo(filePath);
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);

            var extraction = ExtractTable(bytes, Path.GetExtension(filePath).ToLowerInvariant());

            return new RawContent
            {
                Text = extraction.Markdown,
                File = new SourceFileInfo
                {
                    Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                    Extension = Path.GetExtension(filePath).ToLowerInvariant(),
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
            throw new DocumentProcessingException(filePath, $"Failed to extract CSV file: {ex.Message}", ex);
        }
    }

    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Filename cannot be null or empty", nameof(fileName));

        try
        {
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            var bytes = memoryStream.ToArray();

            var extraction = ExtractTable(bytes, Path.GetExtension(fileName).ToLowerInvariant());

            return new RawContent
            {
                Text = extraction.Markdown,
                File = new SourceFileInfo
                {
                    Name = fileName,
                    Extension = Path.GetExtension(fileName).ToLowerInvariant(),
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
            throw new DocumentProcessingException(fileName, $"Failed to extract CSV file from stream: {ex.Message}", ex);
        }
    }

    // ========================================
    // Internals
    // ========================================

    private sealed record TableExtraction(string Markdown, Dictionary<string, object> Hints, List<string> Warnings);

    private static TableExtraction ExtractTable(byte[] bytes, string extension)
    {
        var warnings = new List<string>();
        var hints = new Dictionary<string, object>
        {
            ["file_type"] = extension.TrimStart('.'),
            ["conversion_method"] = "csvhelper"
        };

        if (bytes.Length == 0)
        {
            warnings.Add("CSV file is empty.");
            hints["has_tables"] = false;
            hints["row_count"] = 0;
            hints["column_count"] = 0;
            return new TableExtraction(string.Empty, hints, warnings);
        }

        var (text, encodingName) = DecodeText(bytes, warnings);
        hints["encoding"] = encodingName;

        var delimiter = extension == ".tsv" ? "\t" : ",";
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = false, // rows are consumed manually; first row is treated as header
            BadDataFound = null,
            MissingFieldFound = null,
            DetectColumnCountChanges = false
        };

        var rows = new List<string[]>();
        using (var reader = new StringReader(text))
        using (var parser = new CsvParser(reader, config))
        {
            while (parser.Read())
            {
                rows.Add(parser.Record ?? []);
            }
        }

        if (rows.Count == 0)
        {
            warnings.Add("CSV file contains no parseable rows.");
            hints["has_tables"] = false;
            hints["row_count"] = 0;
            hints["column_count"] = 0;
            return new TableExtraction(string.Empty, hints, warnings);
        }

        var header = rows[0];
        var columnCount = header.Length;
        var dataRowCount = rows.Count - 1;

        hints["has_tables"] = true;
        hints["column_count"] = columnCount;
        hints["row_count"] = dataRowCount;

        if (dataRowCount == 0)
            warnings.Add("CSV file contains a header row but no data rows.");

        var sb = new StringBuilder();
        AppendMarkdownRow(sb, header, columnCount);
        sb.Append("| ");
        sb.Append(string.Join(" | ", Enumerable.Repeat("---", columnCount)));
        sb.AppendLine(" |");

        for (var i = 1; i < rows.Count; i++)
        {
            AppendMarkdownRow(sb, rows[i], columnCount);
        }

        return new TableExtraction(sb.ToString().TrimEnd(), hints, warnings);
    }

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

    /// <summary>
    /// BOM 우선 감지 → strict UTF-8 시도 → 실패 시 CP949(EUC-KR) 폴백.
    /// 한국 현장의 레거시 Excel CSV 내보내기(CP949)를 업스트림에서 흡수한다.
    /// </summary>
    private static (string Text, string EncodingName) DecodeText(byte[] bytes, List<string> warnings)
    {
        // UTF-8 BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "utf-8-bom");

        // UTF-16 LE/BE BOM
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "utf-16le");
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "utf-16be");

        // Strict UTF-8 (ASCII is a valid subset)
        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            return (strictUtf8.GetString(bytes), "utf-8");
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8 - fall back to CP949 (EUC-KR superset, legacy Korean exports)
            var cp949 = Encoding.GetEncoding(949);
            warnings.Add("File is not valid UTF-8; decoded with CP949 (EUC-KR) fallback.");
            return (cp949.GetString(bytes), "cp949");
        }
    }
}
