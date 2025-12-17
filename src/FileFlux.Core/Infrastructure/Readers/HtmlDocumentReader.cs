using HtmlAgilityPack;
using FileFlux.Core;
using System.Text;
using System.Text.RegularExpressions;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// HTML 문서(.html, .htm) 처리를 위한 문서 Reader
/// HtmlAgilityPack을 사용하여 시맨틱 구조, 메타데이터, 콘텐츠 추출
/// </summary>
public partial class HtmlDocumentReader : IDocumentReader
{
    public string ReaderType => "HtmlReader";

    public IEnumerable<string> SupportedExtensions => new[] { ".html", ".htm" };

    public bool CanRead(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension == ".html" || extension == ".htm";
    }

    // ========================================
    // Stage 0: Read (Document Structure)
    // ========================================

    public async Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"HTML document not found: {filePath}");

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        var startTime = DateTime.UtcNow;
        var fileInfo = new FileInfo(filePath);

        var result = new ReadResult
        {
            File = new SourceFileInfo
            {
                Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                Extension = fileInfo.Extension,
                Size = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc,
                ModifiedAt = fileInfo.LastWriteTimeUtc
            },
            ReaderType = ReaderType,
            Duration = DateTime.UtcNow - startTime
        };

        // HTML files are single-page documents
        result.Pages.Add(new PageInfo
        {
            Number = 1,
            HasContent = fileInfo.Length > 0,
            Props = { ["file_type"] = "html_document" }
        });

        return result;
    }

    public Task<ReadResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name cannot be null or empty", nameof(fileName));

        if (!CanRead(fileName))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        var startTime = DateTime.UtcNow;

        var result = new ReadResult
        {
            File = new SourceFileInfo
            {
                Name = fileName,
                Extension = Path.GetExtension(fileName),
                Size = stream.CanSeek ? stream.Length : 0,
                CreatedAt = DateTime.UtcNow
            },
            ReaderType = ReaderType,
            Duration = DateTime.UtcNow - startTime
        };

        result.Pages.Add(new PageInfo
        {
            Number = 1,
            HasContent = stream.Length > 0,
            Props = { ["file_type"] = "html_document" }
        });

        return Task.FromResult(result);
    }

    // ========================================
    // Stage 1: Extract (Raw Content)
    // ========================================

    public async Task<RawContent> ExtractAsync(string filePath, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"HTML document not found: {filePath}");

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        try
        {
            var htmlContent = await File.ReadAllTextAsync(filePath, cancellationToken);
            return await Task.Run(() => ExtractHtmlContent(htmlContent, Path.GetFileName(filePath), new FileInfo(filePath), cancellationToken), cancellationToken);
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            throw new DocumentProcessingException(filePath, $"Failed to process HTML document: {ex.Message}", ex);
        }
    }

    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!CanRead(fileName))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        try
        {
            using var reader = new StreamReader(stream);
            var htmlContent = await reader.ReadToEndAsync(cancellationToken);
            return await Task.Run(() => ExtractHtmlContentFromStream(htmlContent, fileName, stream, cancellationToken), cancellationToken);
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            throw new DocumentProcessingException(fileName, $"Failed to process HTML document from stream: {ex.Message}", ex);
        }
    }

    private static RawContent ExtractHtmlContent(string htmlContent, string fileName, FileInfo fileInfo, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var images = new List<ImageInfo>();

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);

            if (doc.DocumentNode == null)
            {
                warnings.Add("HTML document could not be parsed");
                return CreateEmptyResult(fileInfo, warnings, structuralHints);
            }

            var textBuilder = new StringBuilder();

            // HTML 메타데이터 추출
            ExtractMetadata(doc, structuralHints, textBuilder);

            // 시맨틱 구조 분석
            AnalyzeSemanticStructure(doc, structuralHints);

            // 본문 콘텐츠 추출
            var bodyNode = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
            ExtractBodyContent(bodyNode, textBuilder, structuralHints, warnings, images, cancellationToken);

            var extractedText = textBuilder.ToString().Trim();

            // 문서 통계
            structuralHints["file_type"] = "html_document";
            structuralHints["character_count"] = extractedText.Length;

            // Count embedded images separately
            var embeddedImageCount = images.Count(img => img.Data != null);
            if (embeddedImageCount > 0)
            {
                structuralHints["embedded_image_count"] = embeddedImageCount;
                structuralHints["embedded_images_extracted"] = true;
            }

            return new RawContent
            {
                Text = extractedText,
                File = new SourceFileInfo
                {
                    Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                    Extension = fileInfo.Extension,
                    Size = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    ModifiedAt = fileInfo.LastWriteTimeUtc,

                },
                Hints = structuralHints,
                Warnings = warnings,
                ReaderType = "HtmlReader",
                Images = images
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Error processing HTML document: {ex.Message}");
            return CreateEmptyResult(fileInfo, warnings, structuralHints);
        }
    }

    private static RawContent ExtractHtmlContentFromStream(string htmlContent, string fileName, Stream stream, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var images = new List<ImageInfo>();

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);

            if (doc.DocumentNode == null)
            {
                warnings.Add("HTML document could not be parsed");
                return CreateEmptyStreamResult(stream, fileName, warnings, structuralHints);
            }

            var textBuilder = new StringBuilder();

            // HTML 메타데이터 추출
            ExtractMetadata(doc, structuralHints, textBuilder);

            // 시맨틱 구조 분석
            AnalyzeSemanticStructure(doc, structuralHints);

            // 본문 콘텐츠 추출
            var bodyNode = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
            ExtractBodyContent(bodyNode, textBuilder, structuralHints, warnings, images, cancellationToken);

            var extractedText = textBuilder.ToString().Trim();

            // 문서 통계
            structuralHints["file_type"] = "html_document";
            structuralHints["character_count"] = extractedText.Length;

            // Count embedded images separately
            var embeddedImageCount = images.Count(img => img.Data != null);
            if (embeddedImageCount > 0)
            {
                structuralHints["embedded_image_count"] = embeddedImageCount;
                structuralHints["embedded_images_extracted"] = true;
            }

            return new RawContent
            {
                Text = extractedText,
                File = new SourceFileInfo
                {
                    Name = fileName,
                    Extension = Path.GetExtension(fileName),
                    Size = stream.Length,
                    CreatedAt = DateTime.Now,

                },
                Hints = structuralHints,
                Warnings = warnings,
                ReaderType = "HtmlReader",
                Images = images
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Error processing HTML document from stream: {ex.Message}");
            return CreateEmptyStreamResult(stream, fileName, warnings, structuralHints);
        }
    }

    private static void ExtractMetadata(HtmlDocument doc, Dictionary<string, object> structuralHints, StringBuilder textBuilder)
    {
        // Title 추출
        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        if (titleNode != null && !string.IsNullOrWhiteSpace(titleNode.InnerText))
        {
            var title = HtmlEntity.DeEntitize(titleNode.InnerText).Trim();
            structuralHints["title"] = title;
            textBuilder.AppendLine($"# {title}");
            textBuilder.AppendLine();
        }

        // Meta 태그들 추출
        var metaTags = doc.DocumentNode.SelectNodes("//meta");
        if (metaTags != null)
        {
            var keywords = new List<string>();
            var externalLinks = new List<string>();

            foreach (var meta in metaTags)
            {
                var name = meta.GetAttributeValue("name", "");
                var property = meta.GetAttributeValue("property", "");
                var content = meta.GetAttributeValue("content", "");

                // name 또는 property가 있는 경우만 처리
                var attributeName = !string.IsNullOrWhiteSpace(name) ? name : property;
                if (string.IsNullOrWhiteSpace(attributeName)) continue;

                if (string.IsNullOrWhiteSpace(content)) continue;

                content = HtmlEntity.DeEntitize(content).Trim();

                switch (attributeName.ToLowerInvariant())
                {
                    case "description":
                        structuralHints["description"] = content;
                        break;
                    case "keywords":
                        var keywordList = content.Split(',', ';')
                            .Select(k => k.Trim())
                            .Where(k => !string.IsNullOrWhiteSpace(k))
                            .ToArray();
                        keywords.AddRange(keywordList);
                        break;
                    case "author":
                        structuralHints["author"] = content;
                        break;
                    case "og:title":
                        structuralHints["og_title"] = content;
                        break;
                    case "og:description":
                        structuralHints["og_description"] = content;
                        break;
                }
            }

            if (keywords.Count != 0)
            {
                structuralHints["keywords"] = keywords.ToArray();
            }
        }
    }

    private static void AnalyzeSemanticStructure(HtmlDocument doc, Dictionary<string, object> structuralHints)
    {
        var semanticElements = new List<string>();
        var semanticTags = new[] { "header", "nav", "main", "article", "section", "aside", "footer", "figure" };

        foreach (var tag in semanticTags)
        {
            var nodes = doc.DocumentNode.SelectNodes($"//{tag}");
            if (nodes != null && nodes.Count > 0)
            {
                semanticElements.Add(tag);
            }
        }

        if (semanticElements.Count != 0)
        {
            structuralHints["has_semantic_structure"] = true;
            structuralHints["semantic_elements"] = semanticElements.ToArray();
        }
    }

    private static void ExtractBodyContent(HtmlNode bodyNode, StringBuilder textBuilder, Dictionary<string, object> structuralHints, List<string> warnings, List<ImageInfo> images, CancellationToken cancellationToken)
    {
        var listCount = 0;
        var tableCount = 0;
        var imageCount = 0;
        var linkCount = 0;
        var codeLanguages = new List<string>();
        var externalLinks = new List<string>();
        var hasCode = false;

        TraverseNode(bodyNode, textBuilder, structuralHints, ref listCount, ref tableCount, ref imageCount, ref linkCount, codeLanguages, externalLinks, ref hasCode, images, cancellationToken);

        // 구조적 힌트 업데이트
        if (listCount > 0)
        {
            structuralHints["has_lists"] = true;
            structuralHints["list_count"] = listCount;
        }

        if (tableCount > 0)
        {
            structuralHints["has_tables"] = true;
            structuralHints["table_count"] = tableCount;
        }

        if (imageCount > 0)
        {
            structuralHints["has_images"] = true;
            structuralHints["image_count"] = imageCount;
        }

        if (linkCount > 0 || externalLinks.Count != 0)
        {
            structuralHints["has_links"] = true;
            structuralHints["link_count"] = linkCount;

            if (externalLinks.Count != 0)
            {
                structuralHints["external_links"] = externalLinks.ToArray();
            }
        }

        if (hasCode)
        {
            structuralHints["has_code"] = true;
            if (codeLanguages.Count != 0)
            {
                structuralHints["code_languages"] = codeLanguages.Distinct().ToArray();
            }
        }
    }

    private static void TraverseNode(HtmlNode node, StringBuilder textBuilder, Dictionary<string, object> structuralHints,
        ref int listCount, ref int tableCount, ref int imageCount, ref int linkCount,
        List<string> codeLanguages, List<string> externalLinks, ref bool hasCode, List<ImageInfo> images, CancellationToken cancellationToken, int depth = 0)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (node.NodeType == HtmlNodeType.Text)
        {
            var text = HtmlEntity.DeEntitize(node.InnerText).Trim();
            if (!string.IsNullOrWhiteSpace(text) && !IsInSkippedElement(node))
            {
                textBuilder.Append(text);
                textBuilder.Append(' ');
            }
            return;
        }

        if (node.NodeType != HtmlNodeType.Element) return;

        var tagName = node.Name.ToLowerInvariant();

        // 스킵할 요소들
        if (IsSkippedElement(tagName)) return;

        // 블록 레벨 요소 전 줄바꿈
        if (IsBlockElement(tagName) && textBuilder.Length > 0 && !textBuilder.ToString().EndsWith("\n", StringComparison.Ordinal))
        {
            textBuilder.AppendLine();
        }

        switch (tagName)
        {
            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
                ProcessHeading(node, textBuilder, tagName);
                break;

            case "p":
                ProcessParagraph(node, textBuilder, structuralHints, ref listCount, ref tableCount, ref imageCount, ref linkCount, codeLanguages, externalLinks, ref hasCode, images, depth, cancellationToken);
                break;

            case "ul":
            case "ol":
                ProcessList(node, textBuilder, structuralHints, ref listCount, ref tableCount, ref imageCount, ref linkCount, codeLanguages, externalLinks, ref hasCode, images, depth, tagName, cancellationToken);
                listCount++;
                break;

            case "table":
                ProcessTable(node, textBuilder);
                tableCount++;
                break;

            case "a":
                ProcessLink(node, textBuilder, ref linkCount, externalLinks);
                break;

            case "img":
                ProcessImage(node, textBuilder, ref imageCount, images);
                break;

            case "code":
                ProcessInlineCode(node, textBuilder, ref hasCode);
                break;

            case "pre":
                ProcessCodeBlock(node, textBuilder, codeLanguages, ref hasCode);
                break;

            case "figure":
                ProcessFigure(node, textBuilder, structuralHints, ref listCount, ref tableCount, ref imageCount, ref linkCount, codeLanguages, externalLinks, ref hasCode, images, depth, cancellationToken);
                break;

            case "br":
                textBuilder.AppendLine();
                break;

            default:
                // 기타 요소들은 재귀적으로 처리
                foreach (var childNode in node.ChildNodes)
                {
                    TraverseNode(childNode, textBuilder, structuralHints, ref listCount, ref tableCount, ref imageCount, ref linkCount, codeLanguages, externalLinks, ref hasCode, images, cancellationToken, depth + 1);
                }
                break;
        }

        // 블록 레벨 요소 후 줄바꿈
        if (IsBlockElement(tagName))
        {
            textBuilder.AppendLine();
        }
    }

    private static void ProcessHeading(HtmlNode node, StringBuilder textBuilder, string tagName)
    {
        var level = int.Parse(tagName.Substring(1)); // h1 -> 1, h2 -> 2, etc.
        var headingText = HtmlEntity.DeEntitize(node.InnerText).Trim();

        if (!string.IsNullOrWhiteSpace(headingText))
        {
            var prefix = new string('#', level);
            textBuilder.AppendLine($"{prefix} {headingText}");
        }
    }

    private static void ProcessParagraph(HtmlNode node, StringBuilder textBuilder, Dictionary<string, object> structuralHints,
        ref int listCount, ref int tableCount, ref int imageCount, ref int linkCount,
        List<string> codeLanguages, List<string> externalLinks, ref bool hasCode, List<ImageInfo> images, int depth, CancellationToken cancellationToken)
    {
        foreach (var childNode in node.ChildNodes)
        {
            TraverseNode(childNode, textBuilder, structuralHints, ref listCount, ref tableCount, ref imageCount, ref linkCount, codeLanguages, externalLinks, ref hasCode, images, cancellationToken, depth + 1);
        }
    }

    private static void ProcessList(HtmlNode node, StringBuilder textBuilder, Dictionary<string, object> structuralHints,
        ref int listCount, ref int tableCount, ref int imageCount, ref int linkCount,
        List<string> codeLanguages, List<string> externalLinks, ref bool hasCode, List<ImageInfo> images, int depth, string tagName, CancellationToken cancellationToken)
    {
        var isOrdered = tagName == "ol";
        var items = node.SelectNodes(".//li");

        if (items != null)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var indent = new string(' ', depth * 3);

                if (isOrdered)
                {
                    textBuilder.Append($"{indent}{i + 1}. ");
                }
                else
                {
                    textBuilder.Append($"{indent}- ");
                }

                foreach (var childNode in item.ChildNodes)
                {
                    if (!childNode.Name.Equals("ul", StringComparison.InvariantCultureIgnoreCase) && !childNode.Name.Equals("ol", StringComparison.InvariantCultureIgnoreCase))
                    {
                        TraverseNode(childNode, textBuilder, structuralHints, ref listCount, ref tableCount, ref imageCount, ref linkCount, codeLanguages, externalLinks, ref hasCode, images, cancellationToken, depth + 1);
                    }
                }

                textBuilder.AppendLine();

                // 중첩된 리스트 처리
                var nestedLists = item.SelectNodes("./ul | ./ol");
                if (nestedLists != null)
                {
                    foreach (var nestedList in nestedLists)
                    {
                        TraverseNode(nestedList, textBuilder, structuralHints, ref listCount, ref tableCount, ref imageCount, ref linkCount, codeLanguages, externalLinks, ref hasCode, images, cancellationToken, depth + 1);
                    }
                }
            }
        }
    }

    private static void ProcessTable(HtmlNode node, StringBuilder textBuilder)
    {
        var caption = node.SelectSingleNode(".//caption");
        var captionText = caption != null ? HtmlEntity.DeEntitize(caption.InnerText).Trim() : "";

        if (!string.IsNullOrWhiteSpace(captionText))
        {
            textBuilder.AppendLine($"--- TABLE: {captionText} ---");
        }
        else
        {
            textBuilder.AppendLine("--- TABLE ---");
        }

        var rows = node.SelectNodes(".//tr");
        if (rows != null)
        {
            foreach (var row in rows)
            {
                var cells = row.SelectNodes("./th | ./td");
                if (cells != null)
                {
                    var cellTexts = cells.Select(cell => HtmlEntity.DeEntitize(cell.InnerText).Trim()).ToList();
                    if (cellTexts.Any(c => !string.IsNullOrWhiteSpace(c)))
                    {
                        textBuilder.AppendLine(string.Join(" | ", cellTexts));
                    }
                }
            }
        }

        textBuilder.AppendLine("--- END TABLE ---");
    }

    private static void ProcessLink(HtmlNode node, StringBuilder textBuilder, ref int linkCount, List<string> externalLinks)
    {
        var href = node.GetAttributeValue("href", "");
        var linkText = HtmlEntity.DeEntitize(node.InnerText).Trim();

        if (!string.IsNullOrWhiteSpace(linkText))
        {
            if (!string.IsNullOrWhiteSpace(href))
            {
                textBuilder.Append($"[{linkText}]({href})");
                linkCount++;

                // 외부 링크 판별 (http/https로 시작하는 경우)
                if (href.StartsWith("http://", StringComparison.Ordinal) || href.StartsWith("https://", StringComparison.Ordinal))
                {
                    externalLinks.Add(href);
                }
            }
            else
            {
                textBuilder.Append(linkText);
            }
        }
    }

    private static void ProcessImage(HtmlNode node, StringBuilder textBuilder, ref int imageCount, List<ImageInfo> images)
    {
        var src = node.GetAttributeValue("src", "");
        var alt = node.GetAttributeValue("alt", "");
        var title = node.GetAttributeValue("title", "");

        if (string.IsNullOrWhiteSpace(src)) return;

        // Check for base64 embedded image
        if (IsBase64Image(src))
        {
            var (mimeType, data, originalSize) = ParseBase64Image(src);
            var imageId = $"img_{imageCount:D3}";

            images.Add(new ImageInfo
            {
                Id = imageId,
                Caption = !string.IsNullOrWhiteSpace(alt) ? alt : title,
                Position = textBuilder.Length,
                MimeType = mimeType,
                Data = data,
                SourceUrl = $"embedded:{imageId}",
                OriginalSize = originalSize
            });

            // Output placeholder instead of huge base64 data
            var captionText = !string.IsNullOrWhiteSpace(alt) ? alt : "";
            textBuilder.AppendLine($"![{captionText}](embedded:{imageId})");
        }
        else
        {
            // External URL - keep as-is
            images.Add(new ImageInfo
            {
                Id = $"img_{imageCount:D3}",
                Caption = !string.IsNullOrWhiteSpace(alt) ? alt : title,
                Position = textBuilder.Length,
                SourceUrl = src
            });

            if (!string.IsNullOrWhiteSpace(alt))
            {
                textBuilder.AppendLine($"![{alt}]({src})");
            }
            else
            {
                textBuilder.AppendLine($"![]({src})");
            }
        }

        imageCount++;
    }

    private static bool IsBase64Image(string src)
    {
        return src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
    }

    private static (string? mimeType, byte[]? data, long originalSize) ParseBase64Image(string src)
    {
        try
        {
            // Format: data:image/png;base64,iVBORw0KGgo...
            var match = Base64ImageRegex().Match(src);
            if (!match.Success)
                return (null, null, src.Length);

            var mimeType = $"image/{match.Groups[1].Value}";
            var base64Data = match.Groups[2].Value;
            var originalSize = base64Data.Length;

            var data = Convert.FromBase64String(base64Data);
            return (mimeType, data, originalSize);
        }
        catch
        {
            // If parsing fails, return null data but preserve size info
            return (null, null, src.Length);
        }
    }

    [GeneratedRegex(@"^data:image/(\w+);base64,(.+)$", RegexOptions.Singleline)]
    private static partial Regex Base64ImageRegex();

    private static void ProcessInlineCode(HtmlNode node, StringBuilder textBuilder, ref bool hasCode)
    {
        var codeText = HtmlEntity.DeEntitize(node.InnerText);
        textBuilder.Append($"`{codeText}`");
        hasCode = true;
    }

    private static void ProcessCodeBlock(HtmlNode node, StringBuilder textBuilder, List<string> codeLanguages, ref bool hasCode)
    {
        var codeNode = node.SelectSingleNode(".//code");
        if (codeNode != null)
        {
            var classAttr = codeNode.GetAttributeValue("class", "");
            var language = "";

            if (!string.IsNullOrWhiteSpace(classAttr))
            {
                var match = MyRegex().Match(classAttr);
                if (match.Success)
                {
                    language = match.Groups[1].Value;
                    codeLanguages.Add(language);
                }
            }

            var codeText = HtmlEntity.DeEntitize(codeNode.InnerText);

            if (!string.IsNullOrWhiteSpace(language))
            {
                textBuilder.AppendLine($"```{language}");
            }
            else
            {
                textBuilder.AppendLine("```");
            }

            textBuilder.AppendLine(codeText);
            textBuilder.AppendLine("```");

            hasCode = true;
        }
        else
        {
            // pre 태그만 있는 경우
            var preText = HtmlEntity.DeEntitize(node.InnerText);
            textBuilder.AppendLine("```");
            textBuilder.AppendLine(preText);
            textBuilder.AppendLine("```");
        }
    }

    private static void ProcessFigure(HtmlNode node, StringBuilder textBuilder, Dictionary<string, object> structuralHints,
        ref int listCount, ref int tableCount, ref int imageCount, ref int linkCount,
        List<string> codeLanguages, List<string> externalLinks, ref bool hasCode, List<ImageInfo> images, int depth, CancellationToken cancellationToken)
    {
        // figure 내의 이미지 처리
        var imgNode = node.SelectSingleNode(".//img");
        if (imgNode != null)
        {
            ProcessImage(imgNode, textBuilder, ref imageCount, images);
        }

        // figcaption 처리
        var captionNode = node.SelectSingleNode(".//figcaption");
        if (captionNode != null)
        {
            var captionText = HtmlEntity.DeEntitize(captionNode.InnerText).Trim();
            if (!string.IsNullOrWhiteSpace(captionText))
            {
                textBuilder.AppendLine($"*Figure: {captionText}*");
            }
        }

        // figure 내의 다른 요소들 처리
        foreach (var childNode in node.ChildNodes)
        {
            if (!childNode.Name.Equals("img", StringComparison.InvariantCultureIgnoreCase) && !childNode.Name.Equals("figcaption", StringComparison.InvariantCultureIgnoreCase))
            {
                TraverseNode(childNode, textBuilder, structuralHints, ref listCount, ref tableCount, ref imageCount, ref linkCount, codeLanguages, externalLinks, ref hasCode, images, cancellationToken, depth + 1);
            }
        }
    }

    private static bool IsSkippedElement(string tagName)
    {
        var skippedElements = new[] { "script", "style", "head", "meta", "link", "title" };
        return skippedElements.Contains(tagName);
    }

    private static bool IsInSkippedElement(HtmlNode node)
    {
        var current = node.ParentNode;
        while (current != null)
        {
            if (IsSkippedElement(current.Name.ToLowerInvariant()))
                return true;
            current = current.ParentNode;
        }
        return false;
    }

    private static bool IsBlockElement(string tagName)
    {
        var blockElements = new[] {
            "div", "p", "h1", "h2", "h3", "h4", "h5", "h6",
            "ul", "ol", "li", "table", "tr", "td", "th",
            "header", "nav", "main", "article", "section", "aside", "footer",
            "figure", "figcaption", "pre", "blockquote"
        };
        return blockElements.Contains(tagName);
    }

    private static RawContent CreateEmptyResult(FileInfo fileInfo, List<string> warnings, Dictionary<string, object> structuralHints)
    {
        return new RawContent
        {
            Text = string.Empty,
            File = new SourceFileInfo
            {
                Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                Extension = fileInfo.Extension,
                Size = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc,
                ModifiedAt = fileInfo.LastWriteTimeUtc,

            },
            Hints = structuralHints,
            Warnings = warnings,
            ReaderType = "HtmlReader"
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
                Extension = Path.GetExtension(fileName),
                Size = stream.Length,
                CreatedAt = DateTime.Now,

            },
            Hints = structuralHints,
            Warnings = warnings,
            ReaderType = "HtmlReader"
        };
    }

    [GeneratedRegex(@"language-(\w+)")]
    private static partial Regex MyRegex();
}
