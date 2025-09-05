using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml;
using FileFlux;
using FileFlux.Exceptions;
using FileFlux.Domain;
using System.Text;

namespace FileFlux.Infrastructure.Readers;

/// <summary>
/// Microsoft Word 문서(.docx) 처리를 위한 문서 Reader
/// DocumentFormat.OpenXml 라이브러리를 사용하여 텍스트, 테이블, 헤더 구조 추출
/// </summary>
public class WordDocumentReader : IDocumentReader
{
    public string ReaderType => "WordReader";

    public IEnumerable<string> SupportedExtensions => new[] { ".docx" };

    public bool CanRead(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension == ".docx";
    }

    public async Task<RawDocumentContent> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Word document not found: {filePath}");

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        try
        {
            return await Task.Run(() => ExtractWordContent(filePath, cancellationToken), cancellationToken);
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            throw new DocumentProcessingException(filePath, $"Failed to process Word document: {ex.Message}", ex);
        }
    }

    public async Task<RawDocumentContent> ExtractAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!CanRead(fileName))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        try
        {
            return await Task.Run(() => ExtractWordContentFromStream(stream, fileName, cancellationToken), cancellationToken);
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            throw new DocumentProcessingException(fileName, $"Failed to process Word document from stream: {ex.Message}", ex);
        }
    }

    private static RawDocumentContent ExtractWordContent(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();

        try
        {
            using var wordDocument = WordprocessingDocument.Open(filePath, false);
            var mainPart = wordDocument.MainDocumentPart;

            if (mainPart?.Document?.Body == null)
            {
                warnings.Add("Document body is empty or corrupted");
                return CreateEmptyResult(fileInfo, warnings, structuralHints);
            }

            var textBuilder = new StringBuilder();

            // 문서 제목 및 메타데이터 추출
            var documentTitle = ExtractDocumentTitle(wordDocument);
            ExtractDocumentMetadata(wordDocument, structuralHints);
            
            if (!string.IsNullOrEmpty(documentTitle))
            {
                structuralHints["document_title"] = documentTitle;
                textBuilder.AppendLine($"# {documentTitle}");
                textBuilder.AppendLine();
            }

            // 헤더와 푸터 추출
            var headers = ExtractHeadersFooters(wordDocument, true);
            var footers = ExtractHeadersFooters(wordDocument, false);

            if (!string.IsNullOrEmpty(headers))
            {
                structuralHints["has_headers"] = true;
                textBuilder.AppendLine("--- DOCUMENT HEADERS ---");
                textBuilder.AppendLine(headers);
                textBuilder.AppendLine();
            }

            // 본문 내용 추출
            var bodyContent = ExtractBodyContent(mainPart.Document.Body, structuralHints, warnings, cancellationToken);
            textBuilder.Append(bodyContent);

            // 푸터 추가
            if (!string.IsNullOrEmpty(footers))
            {
                structuralHints["has_footers"] = true;
                textBuilder.AppendLine();
                textBuilder.AppendLine("--- DOCUMENT FOOTERS ---");
                textBuilder.AppendLine(footers);
            }

            var extractedText = textBuilder.ToString().Trim();

            // 문서 통계
            structuralHints["file_type"] = "word_document";
            structuralHints["character_count"] = extractedText.Length;
            structuralHints["paragraph_count"] = mainPart.Document.Body.Elements<Paragraph>().Count();

            return new RawDocumentContent
            {
                Text = extractedText,
                FileInfo = new FileMetadata
                {
                    FileName = fileInfo.Name,
                    FileExtension = ".docx",
                    FileSize = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    ModifiedAt = fileInfo.LastWriteTimeUtc,
                    ExtractedAt = DateTime.UtcNow,
                    ReaderType = "WordReader"
                },
                StructuralHints = structuralHints,
                ExtractionWarnings = warnings
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Error processing Word document: {ex.Message}");
            return CreateEmptyResult(fileInfo, warnings, structuralHints);
        }
    }

    private static RawDocumentContent ExtractWordContentFromStream(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();

        try
        {
            using var wordDocument = WordprocessingDocument.Open(stream, false);
            var mainPart = wordDocument.MainDocumentPart;

            if (mainPart?.Document?.Body == null)
            {
                warnings.Add("Document body is empty or corrupted");
                return CreateEmptyStreamResult(stream, fileName, warnings, structuralHints);
            }

            var textBuilder = new StringBuilder();

            // 문서 제목 및 메타데이터 추출
            var documentTitle = ExtractDocumentTitle(wordDocument);
            ExtractDocumentMetadata(wordDocument, structuralHints);
            
            if (!string.IsNullOrEmpty(documentTitle))
            {
                structuralHints["document_title"] = documentTitle;
                textBuilder.AppendLine($"# {documentTitle}");
                textBuilder.AppendLine();
            }

            // 헤더와 푸터 추출
            var headers = ExtractHeadersFooters(wordDocument, true);
            var footers = ExtractHeadersFooters(wordDocument, false);

            if (!string.IsNullOrEmpty(headers))
            {
                structuralHints["has_headers"] = true;
                textBuilder.AppendLine("--- DOCUMENT HEADERS ---");
                textBuilder.AppendLine(headers);
                textBuilder.AppendLine();
            }

            // 본문 내용 추출
            var bodyContent = ExtractBodyContent(mainPart.Document.Body, structuralHints, warnings, cancellationToken);
            textBuilder.Append(bodyContent);

            // 푸터 추가
            if (!string.IsNullOrEmpty(footers))
            {
                structuralHints["has_footers"] = true;
                textBuilder.AppendLine();
                textBuilder.AppendLine("--- DOCUMENT FOOTERS ---");
                textBuilder.AppendLine(footers);
            }

            var extractedText = textBuilder.ToString().Trim();

            // 문서 통계
            structuralHints["file_type"] = "word_document";
            structuralHints["character_count"] = extractedText.Length;
            structuralHints["paragraph_count"] = mainPart.Document.Body.Elements<Paragraph>().Count();

            return new RawDocumentContent
            {
                Text = extractedText,
                FileInfo = new FileMetadata
                {
                    FileName = fileName,
                    FileExtension = ".docx",
                    FileSize = stream.Length,
                    CreatedAt = DateTime.Now,
                    ModifiedAt = DateTime.Now,
                    ExtractedAt = DateTime.UtcNow,
                    ReaderType = "WordReader"
                },
                StructuralHints = structuralHints,
                ExtractionWarnings = warnings
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Error processing Word document from stream: {ex.Message}");
            return CreateEmptyStreamResult(stream, fileName, warnings, structuralHints);
        }
    }

    private static string ExtractBodyContent(Body body, Dictionary<string, object> structuralHints, List<string> warnings, CancellationToken cancellationToken)
    {
        var textBuilder = new StringBuilder();
        var headerCount = 0;
        var tableCount = 0;
        var paragraphCount = 0;

        foreach (var element in body.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (element)
            {
                case Paragraph paragraph:
                    var paragraphText = ExtractParagraphContent(paragraph);
                    if (!string.IsNullOrWhiteSpace(paragraphText))
                    {
                        // 스타일 기반 헤더 감지
                        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                        if (!string.IsNullOrEmpty(styleId) && styleId.StartsWith("Heading"))
                        {
                            headerCount++;
                            var level = ExtractHeadingLevel(styleId);
                            var headerPrefix = new string('#', level);
                            textBuilder.AppendLine($"{headerPrefix} {paragraphText}");
                        }
                        else
                        {
                            textBuilder.AppendLine(paragraphText);
                        }
                        paragraphCount++;
                        textBuilder.AppendLine();
                    }
                    break;

                case Table table:
                    var tableText = ExtractTableContent(table);
                    if (!string.IsNullOrWhiteSpace(tableText))
                    {
                        textBuilder.AppendLine("--- TABLE ---");
                        textBuilder.AppendLine(tableText);
                        textBuilder.AppendLine("--- END TABLE ---");
                        textBuilder.AppendLine();
                        tableCount++;
                    }
                    break;
            }
        }

        // 구조적 힌트 업데이트
        if (headerCount > 0)
        {
            structuralHints["has_headers"] = true;
            structuralHints["header_count"] = headerCount;
        }

        if (tableCount > 0)
        {
            structuralHints["has_tables"] = true;
            structuralHints["table_count"] = tableCount;
        }

        structuralHints["body_paragraph_count"] = paragraphCount;

        return textBuilder.ToString();
    }

    private static string ExtractParagraphContent(Paragraph paragraph)
    {
        var textBuilder = new StringBuilder();
        var hasImportantFormatting = false;

        foreach (var run in paragraph.Elements<Run>())
        {
            var runProperties = run.RunProperties;
            var isBold = runProperties?.Bold?.Val?.Value == true || runProperties?.Bold?.Val == null && runProperties?.Bold != null;
            var isItalic = runProperties?.Italic?.Val?.Value == true || runProperties?.Italic?.Val == null && runProperties?.Italic != null;
            
            foreach (var text in run.Elements<Text>())
            {
                var textContent = text.Text;
                
                if (isBold || isItalic)
                {
                    hasImportantFormatting = true;
                    if (isBold && isItalic)
                    {
                        textContent = $"***{textContent}***"; // Bold + Italic
                    }
                    else if (isBold)
                    {
                        textContent = $"**{textContent}**"; // Bold
                    }
                    else if (isItalic)
                    {
                        textContent = $"*{textContent}*"; // Italic
                    }
                }
                
                textBuilder.Append(textContent);
            }
            
            // Handle footnote references
            foreach (var footnoteRef in run.Elements<FootnoteReference>())
            {
                var id = footnoteRef.Id?.Value;
                if (id.HasValue)
                {
                    textBuilder.Append($"[^{id.Value}]");
                }
            }
            
            // Handle endnote references  
            foreach (var endnoteRef in run.Elements<EndnoteReference>())
            {
                var id = endnoteRef.Id?.Value;
                if (id.HasValue)
                {
                    textBuilder.Append($"[^end{id.Value}]");
                }
            }
        }

        var result = textBuilder.ToString().Trim();
        
        // Add formatting importance hint as a suffix if detected
        if (hasImportantFormatting && !string.IsNullOrWhiteSpace(result))
        {
            result += " [!IMPORTANT]";
        }

        return result;
    }

    private static string ExtractTableContent(Table table)
    {
        var tableBuilder = new StringBuilder();

        foreach (var row in table.Elements<TableRow>())
        {
            var cellTexts = new List<string>();

            foreach (var cell in row.Elements<TableCell>())
            {
                var cellText = new StringBuilder();
                foreach (var paragraph in cell.Elements<Paragraph>())
                {
                    var paragraphText = ExtractParagraphContent(paragraph);
                    if (!string.IsNullOrWhiteSpace(paragraphText))
                    {
                        cellText.Append(paragraphText);
                        cellText.Append(' ');
                    }
                }
                cellTexts.Add(cellText.ToString().Trim());
            }

            if (cellTexts.Any(c => !string.IsNullOrWhiteSpace(c)))
            {
                tableBuilder.AppendLine(string.Join(" | ", cellTexts));
            }
        }

        return tableBuilder.ToString();
    }

    private static string ExtractDocumentTitle(WordprocessingDocument document)
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

    private static void ExtractDocumentMetadata(WordprocessingDocument document, Dictionary<string, object> structuralHints)
    {
        try
        {
            var coreProperties = document.PackageProperties;
            if (coreProperties != null)
            {
                if (!string.IsNullOrWhiteSpace(coreProperties.Creator))
                    structuralHints["author"] = coreProperties.Creator;
                    
                if (!string.IsNullOrWhiteSpace(coreProperties.Subject))
                    structuralHints["subject"] = coreProperties.Subject;
                    
                if (!string.IsNullOrWhiteSpace(coreProperties.Keywords))
                    structuralHints["keywords"] = coreProperties.Keywords.Split(',', ';').Select(k => k.Trim()).Where(k => !string.IsNullOrWhiteSpace(k)).ToArray();
                    
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

    private static string ExtractHeadersFooters(WordprocessingDocument document, bool isHeader)
    {
        var textBuilder = new StringBuilder();

        try
        {
            if (isHeader)
            {
                var headerParts = document.MainDocumentPart?.HeaderParts;
                if (headerParts != null)
                {
                    foreach (var part in headerParts)
                    {
                        if (part.Header != null)
                        {
                            foreach (var paragraph in part.Header.Descendants<Paragraph>())
                            {
                                var paragraphText = ExtractParagraphContent(paragraph);
                                if (!string.IsNullOrWhiteSpace(paragraphText))
                                {
                                    textBuilder.AppendLine(paragraphText);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                var footerParts = document.MainDocumentPart?.FooterParts;
                if (footerParts != null)
                {
                    foreach (var part in footerParts)
                    {
                        if (part.Footer != null)
                        {
                            foreach (var paragraph in part.Footer.Descendants<Paragraph>())
                            {
                                var paragraphText = ExtractParagraphContent(paragraph);
                                if (!string.IsNullOrWhiteSpace(paragraphText))
                                {
                                    textBuilder.AppendLine(paragraphText);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // 헤더/푸터 추출 실패는 무시
        }

        return textBuilder.ToString().Trim();
    }

    private static int ExtractHeadingLevel(string styleId)
    {
        // Heading1, Heading2, ... 에서 레벨 추출
        if (styleId.StartsWith("Heading") && styleId.Length > 7)
        {
            if (int.TryParse(styleId.AsSpan(7), out var level) && level >= 1 && level <= 6)
            {
                return level;
            }
        }
        return 1; // 기본 레벨
    }

    private static string ExtractFootnotes(WordprocessingDocument document)
    {
        var textBuilder = new StringBuilder();
        
        try
        {
            var footnotesPart = document.MainDocumentPart?.FootnotesPart;
            if (footnotesPart?.Footnotes != null)
            {
                foreach (var footnote in footnotesPart.Footnotes.Elements<Footnote>())
                {
                    var id = footnote.Id?.Value;
                    if (id.HasValue)
                    {
                        var footnoteText = new StringBuilder();
                        foreach (var paragraph in footnote.Elements<Paragraph>())
                        {
                            var paragraphText = ExtractParagraphContent(paragraph);
                            if (!string.IsNullOrWhiteSpace(paragraphText))
                            {
                                footnoteText.Append(paragraphText);
                                footnoteText.Append(' ');
                            }
                        }
                        
                        var content = footnoteText.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            textBuilder.AppendLine($"[^{id.Value}]: {content}");
                        }
                    }
                }
            }
        }
        catch
        {
            // 각주 추출 실패는 무시
        }
        
        return textBuilder.ToString().Trim();
    }

    private static string ExtractEndnotes(WordprocessingDocument document)
    {
        var textBuilder = new StringBuilder();
        
        try
        {
            var endnotesPart = document.MainDocumentPart?.EndnotesPart;
            if (endnotesPart?.Endnotes != null)
            {
                foreach (var endnote in endnotesPart.Endnotes.Elements<Endnote>())
                {
                    var id = endnote.Id?.Value;
                    if (id.HasValue)
                    {
                        var endnoteText = new StringBuilder();
                        foreach (var paragraph in endnote.Elements<Paragraph>())
                        {
                            var paragraphText = ExtractParagraphContent(paragraph);
                            if (!string.IsNullOrWhiteSpace(paragraphText))
                            {
                                endnoteText.Append(paragraphText);
                                endnoteText.Append(' ');
                            }
                        }
                        
                        var content = endnoteText.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            textBuilder.AppendLine($"[^end{id.Value}]: {content}");
                        }
                    }
                }
            }
        }
        catch
        {
            // 미주 추출 실패는 무시
        }
        
        return textBuilder.ToString().Trim();
    }

    private static RawDocumentContent CreateEmptyResult(FileInfo fileInfo, List<string> warnings, Dictionary<string, object> structuralHints)
    {
        return new RawDocumentContent
        {
            Text = string.Empty,
            FileInfo = new FileMetadata
            {
                FileName = fileInfo.Name,
                FileExtension = ".docx",
                FileSize = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc,
                ModifiedAt = fileInfo.LastWriteTimeUtc,
                ExtractedAt = DateTime.UtcNow,
                ReaderType = "WordReader"
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
                FileExtension = ".docx",
                FileSize = stream.Length,
                CreatedAt = DateTime.Now,
                ModifiedAt = DateTime.Now,
                ExtractedAt = DateTime.UtcNow,
                ReaderType = "WordReader"
            },
            StructuralHints = structuralHints,
            ExtractionWarnings = warnings
        };
    }
}