using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FileFlux.Core;
using System.Text;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Microsoft Excel 문서(.xlsx) 처리를 위한 문서 Reader
/// DocumentFormat.OpenXml 라이브러리를 사용하여 워크시트, 셀 데이터, 수식 추출
/// </summary>
public class ExcelDocumentReader : IDocumentReader
{
    private const int MinImageDataSize = 1000; // Minimum ~1KB to filter out icons/decorative images

    public string ReaderType => "ExcelReader";

    public IEnumerable<string> SupportedExtensions => new[] { ".xlsx" };

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
            return await Task.Run(() =>
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

                using var spreadsheetDocument = SpreadsheetDocument.Open(filePath, false);
                var workbookPart = spreadsheetDocument.WorkbookPart;

                var documentTitle = ExtractDocumentTitle(spreadsheetDocument);
                if (!string.IsNullOrEmpty(documentTitle))
                    result.DocumentProps["title"] = documentTitle;

                if (workbookPart?.Workbook?.Sheets != null)
                {
                    var sheets = workbookPart.Workbook.Sheets.Cast<Sheet>().ToList();
                    result.DocumentProps["worksheet_count"] = sheets.Count;

                    var pageNum = 1;
                    foreach (var sheet in sheets)
                    {
                        result.Pages.Add(new PageInfo
                        {
                            Number = pageNum++,
                            HasContent = true,
                            Props =
                            {
                                ["sheet_name"] = sheet.Name?.Value ?? $"Sheet{pageNum}",
                                ["file_type"] = "excel_worksheet"
                            }
                        });
                    }
                }

                result.Duration = DateTime.UtcNow - startTime;
                return result;
            }, cancellationToken).ConfigureAwait(false);
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
            var result = new ReadResult
            {
                File = new SourceFileInfo
                {
                    Name = fileName,
                    Extension = ".xlsx",
                    Size = stream.CanSeek ? stream.Length : 0,
                    CreatedAt = DateTime.UtcNow
                },
                ReaderType = ReaderType
            };

            using var spreadsheetDocument = SpreadsheetDocument.Open(stream, false);
            var workbookPart = spreadsheetDocument.WorkbookPart;

            var documentTitle = ExtractDocumentTitle(spreadsheetDocument);
            if (!string.IsNullOrEmpty(documentTitle))
                result.DocumentProps["title"] = documentTitle;

            if (workbookPart?.Workbook?.Sheets != null)
            {
                var sheets = workbookPart.Workbook.Sheets.Cast<Sheet>().ToList();
                result.DocumentProps["worksheet_count"] = sheets.Count;

                var pageNum = 1;
                foreach (var sheet in sheets)
                {
                    result.Pages.Add(new PageInfo
                    {
                        Number = pageNum++,
                        HasContent = true,
                        Props =
                        {
                            ["sheet_name"] = sheet.Name?.Value ?? $"Sheet{pageNum}",
                            ["file_type"] = "excel_worksheet"
                        }
                    });
                }
            }

            result.Duration = DateTime.UtcNow - startTime;
            return result;
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
            return await Task.Run(() => ExtractExcelContent(filePath, cancellationToken), cancellationToken);
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            throw new DocumentProcessingException(filePath, $"Failed to process Excel document: {ex.Message}", ex);
        }
    }

    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!CanRead(fileName))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        try
        {
            return await Task.Run(() => ExtractExcelContentFromStream(stream, fileName, cancellationToken), cancellationToken);
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            throw new DocumentProcessingException(fileName, $"Failed to process Excel document from stream: {ex.Message}", ex);
        }
    }

    private static RawContent ExtractExcelContent(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var images = new List<ImageInfo>();

        try
        {
            using var spreadsheetDocument = SpreadsheetDocument.Open(filePath, false);
            var workbookPart = spreadsheetDocument.WorkbookPart;

            if (workbookPart?.Workbook?.Sheets == null)
            {
                warnings.Add("Workbook contains no sheets or is corrupted");
                return CreateEmptyResult(fileInfo, warnings, structuralHints);
            }

            var textBuilder = new StringBuilder();
            var sharedStringTable = workbookPart.SharedStringTablePart?.SharedStringTable;

            // 문서 속성 추출
            var documentTitle = ExtractDocumentTitle(spreadsheetDocument);
            if (!string.IsNullOrEmpty(documentTitle))
            {
                structuralHints["document_title"] = documentTitle;
                textBuilder.AppendLine($"# {documentTitle}");
                textBuilder.AppendLine();
            }

            var sheets = workbookPart.Workbook.Sheets.Cast<Sheet>().ToList();
            var worksheetCount = 0;
            var totalRows = 0;
            var totalCells = 0;

            foreach (Sheet sheet in sheets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (sheet.Id?.Value == null) continue;

                try
                {
                    var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id.Value);
                    var worksheet = worksheetPart?.Worksheet;

                    if (worksheet == null || worksheetPart == null) continue;

                    var sheetContent = ExtractWorksheetContent(worksheetPart, sharedStringTable, sheet.Name?.Value ?? $"Sheet{worksheetCount + 1}", warnings, cancellationToken);

                    if (!string.IsNullOrWhiteSpace(sheetContent.Content))
                    {
                        if (textBuilder.Length > 0)
                            textBuilder.AppendLine();

                        textBuilder.AppendLine($"## {sheet.Name?.Value ?? $"Sheet{worksheetCount + 1}"}");
                        textBuilder.AppendLine();
                        textBuilder.Append(sheetContent.Content);
                        textBuilder.AppendLine();

                        totalRows += sheetContent.RowCount;
                        totalCells += sheetContent.CellCount;
                    }

                    // Extract images from worksheet
                    ExtractImagesFromWorksheet(worksheetPart, worksheetCount + 1, sheet.Name?.Value, images, warnings);

                    worksheetCount++;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Error processing sheet '{sheet.Name?.Value}': {ex.Message}");
                }
            }

            var extractedText = textBuilder.ToString().Trim();

            // 문서 통계
            structuralHints["file_type"] = "excel_workbook";
            structuralHints["character_count"] = extractedText.Length;
            structuralHints["worksheet_count"] = worksheetCount;
            structuralHints["total_rows"] = totalRows;
            structuralHints["total_cells"] = totalCells;
            structuralHints["ImagesExtracted"] = images.Count;

            return new RawContent
            {
                Text = extractedText,
                File = new SourceFileInfo
                {
                    Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                    Extension = ".xlsx",
                    Size = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    ModifiedAt = fileInfo.LastWriteTimeUtc,

                },
                Hints = structuralHints,
                Warnings = warnings,
                ReaderType = "ExcelReader",
                Images = images
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Error processing Excel document: {ex.Message}");
            return CreateEmptyResult(fileInfo, warnings, structuralHints);
        }
    }

    private static RawContent ExtractExcelContentFromStream(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var images = new List<ImageInfo>();

        try
        {
            using var spreadsheetDocument = SpreadsheetDocument.Open(stream, false);
            var workbookPart = spreadsheetDocument.WorkbookPart;

            if (workbookPart?.Workbook?.Sheets == null)
            {
                warnings.Add("Workbook contains no sheets or is corrupted");
                return CreateEmptyStreamResult(stream, fileName, warnings, structuralHints);
            }

            var textBuilder = new StringBuilder();
            var sharedStringTable = workbookPart.SharedStringTablePart?.SharedStringTable;

            // 문서 속성 추출
            var documentTitle = ExtractDocumentTitle(spreadsheetDocument);
            if (!string.IsNullOrEmpty(documentTitle))
            {
                structuralHints["document_title"] = documentTitle;
                textBuilder.AppendLine($"# {documentTitle}");
                textBuilder.AppendLine();
            }

            var sheets = workbookPart.Workbook.Sheets.Cast<Sheet>().ToList();
            var worksheetCount = 0;
            var totalRows = 0;
            var totalCells = 0;

            foreach (Sheet sheet in sheets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (sheet.Id?.Value == null) continue;

                try
                {
                    var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id.Value);
                    var worksheet = worksheetPart?.Worksheet;

                    if (worksheet == null || worksheetPart == null) continue;

                    var sheetContent = ExtractWorksheetContent(worksheetPart, sharedStringTable, sheet.Name?.Value ?? $"Sheet{worksheetCount + 1}", warnings, cancellationToken);

                    if (!string.IsNullOrWhiteSpace(sheetContent.Content))
                    {
                        if (textBuilder.Length > 0)
                            textBuilder.AppendLine();

                        textBuilder.AppendLine($"## {sheet.Name?.Value ?? $"Sheet{worksheetCount + 1}"}");
                        textBuilder.AppendLine();
                        textBuilder.Append(sheetContent.Content);
                        textBuilder.AppendLine();

                        totalRows += sheetContent.RowCount;
                        totalCells += sheetContent.CellCount;
                    }

                    // Extract images from worksheet
                    ExtractImagesFromWorksheet(worksheetPart, worksheetCount + 1, sheet.Name?.Value, images, warnings);

                    worksheetCount++;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Error processing sheet '{sheet.Name?.Value}': {ex.Message}");
                }
            }

            var extractedText = textBuilder.ToString().Trim();

            // 문서 통계
            structuralHints["file_type"] = "excel_workbook";
            structuralHints["character_count"] = extractedText.Length;
            structuralHints["worksheet_count"] = worksheetCount;
            structuralHints["total_rows"] = totalRows;
            structuralHints["total_cells"] = totalCells;
            structuralHints["ImagesExtracted"] = images.Count;

            return new RawContent
            {
                Text = extractedText,
                File = new SourceFileInfo
                {
                    Name = fileName,
                    Extension = ".xlsx",
                    Size = stream.Length,
                    CreatedAt = DateTime.Now,

                },
                Hints = structuralHints,
                Warnings = warnings,
                ReaderType = "ExcelReader",
                Images = images
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Error processing Excel document from stream: {ex.Message}");
            return CreateEmptyStreamResult(stream, fileName, warnings, structuralHints);
        }
    }

    private static (string Content, int RowCount, int CellCount) ExtractWorksheetContent(WorksheetPart worksheetPart, SharedStringTable? sharedStringTable, string sheetName, List<string> warnings, CancellationToken cancellationToken)
    {
        var contentBuilder = new StringBuilder();
        var rowCount = 0;
        var cellCount = 0;

        try
        {
            if (worksheetPart.Worksheet is null)
            {
                warnings.Add($"Worksheet '{sheetName}' has no content.");
                return (string.Empty, 0, 0);
            }

            var rows = worksheetPart.Worksheet.Descendants<Row>().ToList();
            var processedRows = new List<List<string>>();
            var maxColumns = 0;

            // First pass: collect all rows and determine max columns
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var cellValues = new List<string>();
                var hasContent = false;

                var cells = row.Elements<Cell>().ToList();
                foreach (var cell in cells)
                {
                    var cellValue = GetCellValue(cell, sharedStringTable);
                    // Escape pipe characters for Markdown table compatibility
                    var escapedValue = cellValue.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
                    cellValues.Add(escapedValue);

                    if (!string.IsNullOrWhiteSpace(cellValue))
                    {
                        hasContent = true;
                        cellCount++;
                    }
                }

                if (hasContent)
                {
                    processedRows.Add(cellValues);
                    maxColumns = Math.Max(maxColumns, cellValues.Count);
                    rowCount++;
                }
            }

            // Generate Markdown table
            if (processedRows.Count > 0 && maxColumns > 0)
            {
                // Normalize all rows to have same column count
                foreach (var row in processedRows)
                {
                    while (row.Count < maxColumns)
                        row.Add(string.Empty);
                }

                // Header row (first row)
                var headerRow = processedRows[0];
                contentBuilder.AppendLine("| " + string.Join(" | ", headerRow) + " |");

                // Separator row
                contentBuilder.AppendLine("| " + string.Join(" | ", headerRow.Select(_ => "---")) + " |");

                // Data rows (skip header)
                for (int i = 1; i < processedRows.Count; i++)
                {
                    contentBuilder.AppendLine("| " + string.Join(" | ", processedRows[i]) + " |");
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Error extracting content from sheet '{sheetName}': {ex.Message}");
        }

        return (contentBuilder.ToString(), rowCount, cellCount);
    }

    private static string GetCellValue(Cell cell, SharedStringTable? sharedStringTable)
    {
        if (cell.CellValue == null) return string.Empty;

        var value = cell.CellValue.Text;
        if (string.IsNullOrEmpty(value)) return string.Empty;

        // SharedString 참조인 경우
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (sharedStringTable != null && int.TryParse(value, out var stringIndex))
            {
                var sharedStringItems = sharedStringTable.Elements<SharedStringItem>().ToList();
                if (stringIndex >= 0 && stringIndex < sharedStringItems.Count)
                {
                    return sharedStringItems[stringIndex].Text?.Text ?? string.Empty;
                }
            }
            return string.Empty;
        }

        // 수식인 경우 (결과값만 반환)
        if (cell.DataType?.Value == CellValues.Number || cell.DataType == null)
        {
            return value;
        }

        // Boolean 값
        if (cell.DataType?.Value == CellValues.Boolean)
        {
            return value == "1" ? "TRUE" : "FALSE";
        }

        // 날짜/시간 (Excel에서는 숫자로 저장되므로 별도 처리 필요)
        // 여기서는 단순히 값을 반환
        return value;
    }

    private static string ExtractDocumentTitle(SpreadsheetDocument document)
    {
        try
        {
            var coreProperties = document.PackageProperties;
            return coreProperties?.Title ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void ExtractImagesFromWorksheet(WorksheetPart worksheetPart, int sheetNumber, string? sheetName, List<ImageInfo> images, List<string> warnings)
    {
        try
        {
            var drawingsPart = worksheetPart.DrawingsPart;
            if (drawingsPart == null) return;

            foreach (var imagePart in drawingsPart.ImageParts)
            {
                try
                {
                    using var stream = imagePart.GetStream();
                    using var memoryStream = new MemoryStream();
                    stream.CopyTo(memoryStream);
                    var imageBytes = memoryStream.ToArray();

                    // Filter out small decorative images
                    if (imageBytes.Length < MinImageDataSize)
                        continue;

                    var imageId = $"img_{images.Count:D3}";
                    var mimeType = imagePart.ContentType ?? DetermineImageMimeType(imageBytes);

                    images.Add(new ImageInfo
                    {
                        Id = imageId,
                        Data = imageBytes,
                        MimeType = mimeType,
                        Position = sheetNumber,
                        SourceUrl = $"embedded:{imageId}",
                        OriginalSize = imageBytes.Length,
                        Properties =
                        {
                            ["SheetNumber"] = sheetNumber,
                            ["SheetName"] = sheetName ?? $"Sheet{sheetNumber}",
                            ["ContentType"] = mimeType
                        }
                    });
                }
                catch (Exception ex)
                {
                    warnings.Add($"Sheet '{sheetName ?? sheetNumber.ToString()}': Image extraction failed - {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Sheet '{sheetName ?? sheetNumber.ToString()}': Image enumeration failed - {ex.Message}");
        }
    }

    private static string DetermineImageMimeType(byte[] imageBytes)
    {
        if (imageBytes.Length < 8)
            return "application/octet-stream";

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (imageBytes[0] == 0x89 && imageBytes[1] == 0x50 && imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
            return "image/png";

        // JPEG: FF D8 FF
        if (imageBytes[0] == 0xFF && imageBytes[1] == 0xD8 && imageBytes[2] == 0xFF)
            return "image/jpeg";

        // GIF: 47 49 46 38
        if (imageBytes[0] == 0x47 && imageBytes[1] == 0x49 && imageBytes[2] == 0x46 && imageBytes[3] == 0x38)
            return "image/gif";

        // BMP: 42 4D
        if (imageBytes[0] == 0x42 && imageBytes[1] == 0x4D)
            return "image/bmp";

        // TIFF: 49 49 2A 00 or 4D 4D 00 2A
        if ((imageBytes[0] == 0x49 && imageBytes[1] == 0x49 && imageBytes[2] == 0x2A && imageBytes[3] == 0x00) ||
            (imageBytes[0] == 0x4D && imageBytes[1] == 0x4D && imageBytes[2] == 0x00 && imageBytes[3] == 0x2A))
            return "image/tiff";

        // WebP: 52 49 46 46 ... 57 45 42 50
        if (imageBytes.Length > 12 && imageBytes[0] == 0x52 && imageBytes[1] == 0x49 && imageBytes[2] == 0x46 && imageBytes[3] == 0x46 &&
            imageBytes[8] == 0x57 && imageBytes[9] == 0x45 && imageBytes[10] == 0x42 && imageBytes[11] == 0x50)
            return "image/webp";

        // EMF: 01 00 00 00
        if (imageBytes[0] == 0x01 && imageBytes[1] == 0x00 && imageBytes[2] == 0x00 && imageBytes[3] == 0x00)
            return "image/emf";

        // WMF: D7 CD C6 9A
        if (imageBytes[0] == 0xD7 && imageBytes[1] == 0xCD && imageBytes[2] == 0xC6 && imageBytes[3] == 0x9A)
            return "image/wmf";

        return "application/octet-stream";
    }

    private static RawContent CreateEmptyResult(FileInfo fileInfo, List<string> warnings, Dictionary<string, object> structuralHints)
    {
        return new RawContent
        {
            Text = string.Empty,
            File = new SourceFileInfo
            {
                Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                Extension = ".xlsx",
                Size = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc,
                ModifiedAt = fileInfo.LastWriteTimeUtc,

            },
            Hints = structuralHints,
            Warnings = warnings,
            ReaderType = "ExcelReader"
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
                Extension = ".xlsx",
                Size = stream.Length,
                CreatedAt = DateTime.Now,

            },
            Hints = structuralHints,
            Warnings = warnings,
            ReaderType = "ExcelReader"
        };
    }
}
