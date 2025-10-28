using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using FileFlux;
using FileFlux.Exceptions;
using FileFlux.Domain;
using FileFlux.Infrastructure.Utils;
using System.Text;
using A = DocumentFormat.OpenXml.Drawing;

namespace FileFlux.Infrastructure.Readers;

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

            // 문서 제목 추출
            var documentTitle = ExtractDocumentTitle(presentationDocument);
            if (!string.IsNullOrEmpty(documentTitle))
            {
                structuralHints["document_title"] = documentTitle;
                textBuilder.AppendLine($"# {documentTitle}");
                textBuilder.AppendLine();
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

                    if (slide == null) continue;

                    var slideContent = ExtractSlideContent(slide, slidePart, slideCount + 1, warnings, cancellationToken);

                    if (!string.IsNullOrWhiteSpace(slideContent.Content))
                    {
                        if (textBuilder.Length > 0)
                            textBuilder.AppendLine();

                        textBuilder.Append(slideContent.Content);
                        textBuilder.AppendLine();

                        totalShapes += slideContent.ShapeCount;
                    }

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
                ReaderType = "PowerPointReader"
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

            // 문서 제목 추출
            var documentTitle = ExtractDocumentTitle(presentationDocument);
            if (!string.IsNullOrEmpty(documentTitle))
            {
                structuralHints["document_title"] = documentTitle;
                textBuilder.AppendLine($"# {documentTitle}");
                textBuilder.AppendLine();
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

                    if (slide == null) continue;

                    var slideContent = ExtractSlideContent(slide, slidePart, slideCount + 1, warnings, cancellationToken);

                    if (!string.IsNullOrWhiteSpace(slideContent.Content))
                    {
                        if (textBuilder.Length > 0)
                            textBuilder.AppendLine();

                        textBuilder.Append(slideContent.Content);
                        textBuilder.AppendLine();

                        totalShapes += slideContent.ShapeCount;
                    }

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
                ReaderType = "PowerPointReader"
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

            // 슬라이드의 모든 텍스트 요소 추출
            var shapes = slide.CommonSlideData?.ShapeTree?.Elements<Shape>().ToList() ?? new List<Shape>();

            foreach (var shape in shapes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var shapeText = ExtractTextFromShape(shape);
                if (!string.IsNullOrWhiteSpace(shapeText))
                {
                    contentBuilder.AppendLine(shapeText);
                    contentBuilder.AppendLine();
                    shapeCount++;
                }
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

    private static string ExtractDocumentTitle(PresentationDocument document)
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
