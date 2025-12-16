using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using FileFlux.Core;
using System.Text;
using A = DocumentFormat.OpenXml.Drawing;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Microsoft PowerPoint 문서(.pptx) 처리를 위한 문서 Reader
/// DocumentFormat.OpenXml 라이브러리를 사용하여 슬라이드 콘텐츠, 노트, 제목 추출
/// </summary>
public class PowerPointDocumentReader : IDocumentReader
{
    public string ReaderType => "PowerPointReader";

    public IEnumerable<string> SupportedExtensions => new[] { ".pptx" };

    public bool CanRead(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension == ".pptx";
    }

    public async Task<RawContent> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PowerPoint document not found: {filePath}");

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        try
        {
            return await Task.Run(() => ExtractPowerPointContent(filePath, cancellationToken), cancellationToken);
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            throw new DocumentProcessingException(filePath, $"Failed to process PowerPoint document: {ex.Message}", ex);
        }
    }

    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!CanRead(fileName))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        try
        {
            return await Task.Run(() => ExtractPowerPointContentFromStream(stream, fileName, cancellationToken), cancellationToken);
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            throw new DocumentProcessingException(fileName, $"Failed to process PowerPoint document from stream: {ex.Message}", ex);
        }
    }

    private static RawContent ExtractPowerPointContent(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var images = new List<ImageInfo>();

        try
        {
            using var presentationDocument = PresentationDocument.Open(filePath, false);
            var presentationPart = presentationDocument.PresentationPart;

            if (presentationPart?.Presentation == null)
            {
                warnings.Add("Presentation is empty or corrupted");
                return CreateEmptyResult(fileInfo, warnings, structuralHints);
            }

            var textBuilder = new StringBuilder();

            // 문서 메타데이터 추출 (제목, 작성자)
            var (documentTitle, documentAuthor) = ExtractDocumentMetadata(presentationDocument);
            if (!string.IsNullOrEmpty(documentTitle))
            {
                structuralHints["document_title"] = documentTitle;
                textBuilder.AppendLine($"# {documentTitle}");
                textBuilder.AppendLine();
            }
            if (!string.IsNullOrEmpty(documentAuthor))
            {
                structuralHints["author"] = documentAuthor;
            }

            var slideIds = presentationPart.Presentation.SlideIdList?.Elements<SlideId>().ToList() ?? new List<SlideId>();
            var slideCount = 0;
            var totalShapes = 0;

            foreach (var slideId in slideIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (slideId.RelationshipId?.Value == null) continue;

                try
                {
                    var slidePart = (SlidePart)presentationPart.GetPartById(slideId.RelationshipId.Value);
                    var slide = slidePart?.Slide;

                    if (slide == null || slidePart == null) continue;

                    var slideContent = ExtractSlideContent(slide, slidePart, slideCount + 1, warnings, cancellationToken);

                    if (!string.IsNullOrWhiteSpace(slideContent.Content))
                    {
                        if (textBuilder.Length > 0)
                            textBuilder.AppendLine();

                        textBuilder.Append(slideContent.Content);
                        textBuilder.AppendLine();

                        totalShapes += slideContent.ShapeCount;
                    }

                    // Extract images from slide
                    ExtractImagesFromSlide(slidePart, slideCount + 1, images, warnings);

                    slideCount++;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Error processing slide {slideCount + 1}: {ex.Message}");
                }
            }

            var extractedText = textBuilder.ToString().Trim();

            // 문서 통계
            structuralHints["file_type"] = "powerpoint_presentation";
            structuralHints["character_count"] = extractedText.Length;
            structuralHints["slide_count"] = slideCount;
            structuralHints["total_shapes"] = totalShapes;
            structuralHints["ImagesExtracted"] = images.Count;

            return new RawContent
            {
                Text = extractedText,
                File = new SourceFileInfo
                {
                    Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                    Extension = ".pptx",
                    Size = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    ModifiedAt = fileInfo.LastWriteTimeUtc,

                },
                Hints = structuralHints,
                Warnings = warnings,
                ReaderType = "PowerPointReader",
                Images = images
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Error processing PowerPoint document: {ex.Message}");
            return CreateEmptyResult(fileInfo, warnings, structuralHints);
        }
    }

    private static RawContent ExtractPowerPointContentFromStream(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var images = new List<ImageInfo>();

        try
        {
            using var presentationDocument = PresentationDocument.Open(stream, false);
            var presentationPart = presentationDocument.PresentationPart;

            if (presentationPart?.Presentation == null)
            {
                warnings.Add("Presentation is empty or corrupted");
                return CreateEmptyStreamResult(stream, fileName, warnings, structuralHints);
            }

            var textBuilder = new StringBuilder();

            // 문서 메타데이터 추출 (제목, 작성자)
            var (documentTitle, documentAuthor) = ExtractDocumentMetadata(presentationDocument);
            if (!string.IsNullOrEmpty(documentTitle))
            {
                structuralHints["document_title"] = documentTitle;
                textBuilder.AppendLine($"# {documentTitle}");
                textBuilder.AppendLine();
            }
            if (!string.IsNullOrEmpty(documentAuthor))
            {
                structuralHints["author"] = documentAuthor;
            }

            var slideIds = presentationPart.Presentation.SlideIdList?.Elements<SlideId>().ToList() ?? new List<SlideId>();
            var slideCount = 0;
            var totalShapes = 0;

            foreach (var slideId in slideIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (slideId.RelationshipId?.Value == null) continue;

                try
                {
                    var slidePart = (SlidePart)presentationPart.GetPartById(slideId.RelationshipId.Value);
                    var slide = slidePart?.Slide;

                    if (slide == null || slidePart == null) continue;

                    var slideContent = ExtractSlideContent(slide, slidePart, slideCount + 1, warnings, cancellationToken);

                    if (!string.IsNullOrWhiteSpace(slideContent.Content))
                    {
                        if (textBuilder.Length > 0)
                            textBuilder.AppendLine();

                        textBuilder.Append(slideContent.Content);
                        textBuilder.AppendLine();

                        totalShapes += slideContent.ShapeCount;
                    }

                    // Extract images from slide
                    ExtractImagesFromSlide(slidePart, slideCount + 1, images, warnings);

                    slideCount++;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Error processing slide {slideCount + 1}: {ex.Message}");
                }
            }

            var extractedText = textBuilder.ToString().Trim();

            // 문서 통계
            structuralHints["file_type"] = "powerpoint_presentation";
            structuralHints["character_count"] = extractedText.Length;
            structuralHints["slide_count"] = slideCount;
            structuralHints["total_shapes"] = totalShapes;
            structuralHints["ImagesExtracted"] = images.Count;

            return new RawContent
            {
                Text = extractedText,
                File = new SourceFileInfo
                {
                    Name = fileName,
                    Extension = ".pptx",
                    Size = stream.Length,
                    CreatedAt = DateTime.Now,

                },
                Hints = structuralHints,
                Warnings = warnings,
                ReaderType = "PowerPointReader",
                Images = images
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Error processing PowerPoint document from stream: {ex.Message}");
            return CreateEmptyStreamResult(stream, fileName, warnings, structuralHints);
        }
    }

    private static (string Content, int ShapeCount) ExtractSlideContent(Slide slide, SlidePart slidePart, int slideNumber, List<string> warnings, CancellationToken cancellationToken)
    {
        var contentBuilder = new StringBuilder();
        var shapeCount = 0;

        try
        {
            contentBuilder.AppendLine($"## Slide {slideNumber}");
            contentBuilder.AppendLine();

            var shapeTree = slide.CommonSlideData?.ShapeTree;
            if (shapeTree != null)
            {
                // Process all child elements in ShapeTree (Shape, GraphicFrame, GroupShape, etc.)
                shapeCount = ExtractContentFromShapeTree(shapeTree, contentBuilder, warnings, cancellationToken);
            }

            // 슬라이드 노트 추출
            if (slidePart.NotesSlidePart?.NotesSlide != null)
            {
                var notesContent = ExtractNotesContent(slidePart.NotesSlidePart.NotesSlide);
                if (!string.IsNullOrWhiteSpace(notesContent))
                {
                    contentBuilder.AppendLine("### Speaker Notes");
                    contentBuilder.AppendLine(notesContent);
                    contentBuilder.AppendLine();
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Error extracting content from slide {slideNumber}: {ex.Message}");
        }

        return (contentBuilder.ToString(), shapeCount);
    }

    private static int ExtractContentFromShapeTree(ShapeTree shapeTree, StringBuilder contentBuilder, List<string> warnings, CancellationToken cancellationToken)
    {
        var elementCount = 0;

        foreach (var element in shapeTree.ChildElements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extractedText = ExtractTextFromElement(element, warnings);
            if (!string.IsNullOrWhiteSpace(extractedText))
            {
                contentBuilder.AppendLine(extractedText);
                contentBuilder.AppendLine();
                elementCount++;
            }
        }

        return elementCount;
    }

    private static string ExtractTextFromElement(OpenXmlElement element, List<string> warnings)
    {
        try
        {
            return element switch
            {
                Shape shape => ExtractTextFromShape(shape),
                GraphicFrame graphicFrame => ExtractTextFromGraphicFrame(graphicFrame),
                GroupShape groupShape => ExtractTextFromGroupShape(groupShape, warnings),
                _ => string.Empty
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Error extracting text from element {element.GetType().Name}: {ex.Message}");
            return string.Empty;
        }
    }

    private static string ExtractTextFromGraphicFrame(GraphicFrame graphicFrame)
    {
        var textBuilder = new StringBuilder();

        try
        {
            // Extract table content from GraphicFrame
            var graphic = graphicFrame.Graphic;
            var graphicData = graphic?.GraphicData;

            if (graphicData == null) return string.Empty;

            // Find table element (A.Table)
            var table = graphicData.Descendants<A.Table>().FirstOrDefault();
            if (table != null)
            {
                textBuilder.Append(ExtractTextFromTable(table));
            }

            // Also extract any direct text content
            foreach (var paragraph in graphicData.Descendants<A.Paragraph>())
            {
                var paragraphText = ExtractParagraphText(paragraph);
                if (!string.IsNullOrWhiteSpace(paragraphText))
                {
                    textBuilder.AppendLine(paragraphText);
                }
            }
        }
        catch
        {
            // GraphicFrame extraction failure - return empty
        }

        return textBuilder.ToString().Trim();
    }

    private static string ExtractTextFromTable(A.Table table)
    {
        var rows = new List<List<string>>();

        try
        {
            foreach (var tableRow in table.Elements<A.TableRow>())
            {
                var row = new List<string>();

                foreach (var tableCell in tableRow.Elements<A.TableCell>())
                {
                    var cellText = new StringBuilder();

                    foreach (var paragraph in tableCell.Elements<A.Paragraph>())
                    {
                        var paragraphText = ExtractParagraphText(paragraph);
                        if (!string.IsNullOrWhiteSpace(paragraphText))
                        {
                            if (cellText.Length > 0) cellText.Append(" ");
                            cellText.Append(paragraphText);
                        }
                    }

                    row.Add(cellText.ToString().Trim());
                }

                // 빈 행이 아닌 경우에만 추가 (모든 셀이 빈 경우 제외)
                if (row.Count > 0 && row.Any(cell => !string.IsNullOrWhiteSpace(cell)))
                {
                    rows.Add(row);
                }
            }

            if (rows.Count == 0) return string.Empty;

            // Convert to markdown table
            return ConvertToMarkdownTable(rows);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ConvertToMarkdownTable(List<List<string>> rows)
    {
        if (rows.Count == 0) return string.Empty;

        var maxColumns = rows.Max(r => r.Count);
        var tableBuilder = new StringBuilder();

        // Normalize all rows to have the same number of columns
        foreach (var row in rows)
        {
            while (row.Count < maxColumns)
            {
                row.Add(string.Empty);
            }
        }

        // Header row
        var headerRow = rows[0];
        tableBuilder.AppendLine("| " + string.Join(" | ", headerRow.Select(c => c.Replace("|", "\\|"))) + " |");

        // Separator row
        tableBuilder.AppendLine("| " + string.Join(" | ", Enumerable.Repeat("---", maxColumns)) + " |");

        // Data rows
        for (int i = 1; i < rows.Count; i++)
        {
            tableBuilder.AppendLine("| " + string.Join(" | ", rows[i].Select(c => c.Replace("|", "\\|"))) + " |");
        }

        return tableBuilder.ToString().Trim();
    }

    private static string ExtractTextFromGroupShape(GroupShape groupShape, List<string> warnings)
    {
        var textBuilder = new StringBuilder();

        try
        {
            // Recursively extract text from all child elements in the group
            foreach (var childElement in groupShape.ChildElements)
            {
                var extractedText = ExtractTextFromElement(childElement, warnings);
                if (!string.IsNullOrWhiteSpace(extractedText))
                {
                    textBuilder.AppendLine(extractedText);
                }
            }
        }
        catch
        {
            // GroupShape extraction failure - return empty
        }

        return textBuilder.ToString().Trim();
    }

    private static string ExtractParagraphText(A.Paragraph paragraph)
    {
        var paragraphText = new StringBuilder();

        foreach (var run in paragraph.Elements<A.Run>())
        {
            var text = run.Elements<A.Text>().FirstOrDefault();
            if (text?.Text != null)
            {
                paragraphText.Append(text.Text);
            }
        }

        // Also check for A.Field elements (for slide numbers, dates, etc.)
        foreach (var field in paragraph.Elements<A.Field>())
        {
            var text = field.Elements<A.Text>().FirstOrDefault();
            if (text?.Text != null)
            {
                paragraphText.Append(text.Text);
            }
        }

        return paragraphText.ToString().Trim();
    }

    private static string ExtractTextFromShape(Shape shape)
    {
        var textBuilder = new StringBuilder();

        try
        {
            var textBody = shape.TextBody;
            if (textBody == null) return string.Empty;

            foreach (var paragraph in textBody.Elements<A.Paragraph>())
            {
                var paragraphText = new StringBuilder();

                foreach (var run in paragraph.Elements<A.Run>())
                {
                    var text = run.Elements<A.Text>().FirstOrDefault();
                    if (text?.Text != null)
                    {
                        paragraphText.Append(text.Text);
                    }
                }

                var lineText = paragraphText.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(lineText))
                {
                    textBuilder.AppendLine(lineText);
                }
            }
        }
        catch
        {
            // 텍스트 추출 실패는 무시하고 빈 문자열 반환
        }

        return textBuilder.ToString().Trim();
    }

    private static string ExtractNotesContent(NotesSlide notesSlide)
    {
        var textBuilder = new StringBuilder();

        try
        {
            var shapes = notesSlide.CommonSlideData?.ShapeTree?.Elements<Shape>().ToList() ?? new List<Shape>();

            foreach (var shape in shapes)
            {
                var shapeText = ExtractTextFromShape(shape);
                if (!string.IsNullOrWhiteSpace(shapeText))
                {
                    textBuilder.AppendLine(shapeText);
                }
            }
        }
        catch
        {
            // 노트 추출 실패는 무시
        }

        return textBuilder.ToString().Trim();
    }

    private static (string Title, string? Author) ExtractDocumentMetadata(PresentationDocument document)
    {
        try
        {
            var coreProperties = document.PackageProperties;
            var title = coreProperties?.Title ?? string.Empty;
            var author = coreProperties?.Creator;
            return (title, author);
        }
        catch
        {
            return (string.Empty, null);
        }
    }

    #region Image Extraction

    /// <summary>
    /// Minimum image data size in bytes to extract.
    /// Very small images are likely decorative elements (icons, bullets).
    /// </summary>
    private const int MinImageDataSize = 1000;

    /// <summary>
    /// Extract embedded images from a PowerPoint slide.
    /// </summary>
    private static void ExtractImagesFromSlide(SlidePart slidePart, int slideNumber, List<ImageInfo> images, List<string> warnings)
    {
        try
        {
            foreach (var imagePart in slidePart.ImageParts)
            {
                try
                {
                    using var stream = imagePart.GetStream();
                    using var memoryStream = new MemoryStream();
                    stream.CopyTo(memoryStream);
                    var imageBytes = memoryStream.ToArray();

                    // Skip very small images (likely decorative)
                    if (imageBytes.Length < MinImageDataSize)
                    {
                        continue;
                    }

                    var imageId = $"img_{images.Count:D3}";
                    var mimeType = imagePart.ContentType ?? DetermineImageMimeType(imageBytes);

                    images.Add(new ImageInfo
                    {
                        Id = imageId,
                        Data = imageBytes,
                        MimeType = mimeType,
                        Position = slideNumber,
                        SourceUrl = $"embedded:{imageId}",
                        OriginalSize = imageBytes.Length,
                        Properties =
                        {
                            ["SlideNumber"] = slideNumber,
                            ["ContentType"] = mimeType
                        }
                    });
                }
                catch (Exception ex)
                {
                    warnings.Add($"Slide {slideNumber}: Image extraction failed - {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Slide {slideNumber}: Image enumeration failed - {ex.Message}");
        }
    }

    /// <summary>
    /// Determine the MIME type of image data based on magic bytes.
    /// </summary>
    private static string DetermineImageMimeType(byte[] bytes)
    {
        if (bytes.Length >= 2)
        {
            // JPEG magic bytes: FF D8
            if (bytes[0] == 0xFF && bytes[1] == 0xD8)
            {
                return "image/jpeg";
            }
            // PNG magic bytes: 89 50 4E 47
            if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            {
                return "image/png";
            }
            // GIF magic bytes: 47 49 46
            if (bytes.Length >= 3 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            {
                return "image/gif";
            }
            // BMP magic bytes: 42 4D
            if (bytes[0] == 0x42 && bytes[1] == 0x4D)
            {
                return "image/bmp";
            }
            // TIFF magic bytes: 49 49 2A 00 (little-endian) or 4D 4D 00 2A (big-endian)
            if (bytes.Length >= 4 &&
                ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) ||
                 (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A)))
            {
                return "image/tiff";
            }
            // EMF magic bytes: 01 00 00 00 (first 4 bytes of EMF header)
            if (bytes.Length >= 4 && bytes[0] == 0x01 && bytes[1] == 0x00 && bytes[2] == 0x00 && bytes[3] == 0x00)
            {
                return "image/emf";
            }
            // WMF magic bytes: D7 CD C6 9A
            if (bytes.Length >= 4 && bytes[0] == 0xD7 && bytes[1] == 0xCD && bytes[2] == 0xC6 && bytes[3] == 0x9A)
            {
                return "image/wmf";
            }
        }

        return "application/octet-stream";
    }

    #endregion

    private static RawContent CreateEmptyResult(FileInfo fileInfo, List<string> warnings, Dictionary<string, object> structuralHints)
    {
        return new RawContent
        {
            Text = string.Empty,
            File = new SourceFileInfo
            {
                Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                Extension = ".pptx",
                Size = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc,
                ModifiedAt = fileInfo.LastWriteTimeUtc,

            },
            Hints = structuralHints,
            Warnings = warnings,
            ReaderType = "PowerPointReader"
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
                Extension = ".pptx",
                Size = stream.Length,
                CreatedAt = DateTime.Now,

            },
            Hints = structuralHints,
            Warnings = warnings,
            ReaderType = "PowerPointReader"
        };
    }
}
