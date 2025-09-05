using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FileFlux;
using FileFlux.Exceptions;
using FileFlux.Domain;
using System.Text;

namespace FileFlux.Infrastructure.Readers;

/// <summary>
/// Microsoft Excel 문서(.xlsx) 처리를 위한 문서 Reader
/// DocumentFormat.OpenXml 라이브러리를 사용하여 워크시트, 셀 데이터, 수식 추출
/// </summary>
public class ExcelDocumentReader : IDocumentReader
{
    public string ReaderType => "ExcelReader";

    public IEnumerable<string> SupportedExtensions => new[] { ".xlsx" };

    public bool CanRead(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension == ".xlsx";
    }

    public async Task<RawDocumentContent> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
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

    public async Task<RawDocumentContent> ExtractAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
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

    private static RawDocumentContent ExtractExcelContent(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();

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

                    if (worksheet == null) continue;

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

            return new RawDocumentContent
            {
                Text = extractedText,
                FileInfo = new FileMetadata
                {
                    FileName = fileInfo.Name,
                    FileExtension = ".xlsx",
                    FileSize = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    ModifiedAt = fileInfo.LastWriteTimeUtc,
                    ExtractedAt = DateTime.UtcNow,
                    ReaderType = "ExcelReader"
                },
                StructuralHints = structuralHints,
                ExtractionWarnings = warnings
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Error processing Excel document: {ex.Message}");
            return CreateEmptyResult(fileInfo, warnings, structuralHints);
        }
    }

    private static RawDocumentContent ExtractExcelContentFromStream(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();

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

                    if (worksheet == null) continue;

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

            return new RawDocumentContent
            {
                Text = extractedText,
                FileInfo = new FileMetadata
                {
                    FileName = fileName,
                    FileExtension = ".xlsx",
                    FileSize = stream.Length,
                    CreatedAt = DateTime.Now,
                    ModifiedAt = DateTime.Now,
                    ExtractedAt = DateTime.UtcNow,
                    ReaderType = "ExcelReader"
                },
                StructuralHints = structuralHints,
                ExtractionWarnings = warnings
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
            var rows = worksheetPart.Worksheet.Descendants<Row>().ToList();
            
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var cellValues = new List<string>();
                var hasContent = false;

                var cells = row.Elements<Cell>().ToList();
                foreach (var cell in cells)
                {
                    var cellValue = GetCellValue(cell, sharedStringTable);
                    cellValues.Add(cellValue);
                    
                    if (!string.IsNullOrWhiteSpace(cellValue))
                    {
                        hasContent = true;
                        cellCount++;
                    }
                }

                if (hasContent)
                {
                    // 테이블 형식으로 셀 값들을 연결 (파이프 구분자 사용)
                    var rowText = string.Join(" | ", cellValues.Where(v => !string.IsNullOrWhiteSpace(v)));
                    if (!string.IsNullOrWhiteSpace(rowText))
                    {
                        contentBuilder.AppendLine(rowText);
                        rowCount++;
                    }
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

    private static RawDocumentContent CreateEmptyResult(FileInfo fileInfo, List<string> warnings, Dictionary<string, object> structuralHints)
    {
        return new RawDocumentContent
        {
            Text = string.Empty,
            FileInfo = new FileMetadata
            {
                FileName = fileInfo.Name,
                FileExtension = ".xlsx",
                FileSize = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc,
                ModifiedAt = fileInfo.LastWriteTimeUtc,
                ExtractedAt = DateTime.UtcNow,
                ReaderType = "ExcelReader"
            },
            StructuralHints = structuralHints,
            ExtractionWarnings = warnings
        };
    }

    private static RawDocumentContent CreateEmptyStreamResult(Stream stream, string fileName, List<string> warnings, Dictionary<string, object> structuralHints)
    {
        return new RawDocumentContent
        {
            Text = string.Empty,
            FileInfo = new FileMetadata
            {
                FileName = fileName,
                FileExtension = ".xlsx",
                FileSize = stream.Length,
                CreatedAt = DateTime.Now,
                ModifiedAt = DateTime.Now,
                ExtractedAt = DateTime.UtcNow,
                ReaderType = "ExcelReader"
            },
            StructuralHints = structuralHints,
            ExtractionWarnings = warnings
        };
    }
}
