using FileFlux.Core;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Renderers;
using System.Text;
using System.IO;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Markdig를 사용한 전문 Markdown 문서 리더
/// 테이블 구조를 완벽히 보존하고 의미적 요소를 정확히 파싱
/// </summary>
public class MarkdownDocumentReader : IDocumentReader
{
    public IEnumerable<string> SupportedExtensions => new[] { ".md", ".markdown" };

    public string ReaderType => "MarkdownReader";

    public bool CanRead(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    public async Task<RawContent> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Markdown file not found: {filePath}");

        var markdownText = await File.ReadAllTextAsync(filePath, cancellationToken);
        var metadata = CreateMetadata(filePath, markdownText);

        // Markdig 파이프라인 설정 - 고급 기능 활성화 (테이블 포함)
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()  // 테이블을 포함한 모든 고급 확장 기능
            .Build();

        // Markdown 문서 파싱
        var document = Markdown.Parse(markdownText, pipeline);

        // 구조적 콘텐츠 추출
        var structuredContent = ExtractStructuredContent(document, markdownText);

        return new RawContent
        {
            Text = structuredContent,
            File = metadata,
            ReaderType = ReaderType
        };
    }

    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(stream);

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name cannot be null or empty", nameof(fileName));

        using var reader = new StreamReader(stream, leaveOpen: true);
        var markdownText = await reader.ReadToEndAsync(cancellationToken);
        var metadata = CreateStreamMetadata(fileName, markdownText);

        // Markdig 파이프라인 설정 - 고급 기능 활성화 (테이블 포함)
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()  // 테이블을 포함한 모든 고급 확장 기능
            .Build();

        // Markdown 문서 파싱
        var document = Markdown.Parse(markdownText, pipeline);

        // 구조적 콘텐츠 추출
        var structuredContent = ExtractStructuredContent(document, markdownText);

        return new RawContent
        {
            Text = structuredContent,
            File = metadata,
            ReaderType = ReaderType
        };
    }

    private static SourceFileInfo CreateMetadata(string filePath, string content)
    {
        var fileInfo = new FileInfo(filePath);
        return new SourceFileInfo
        {
            Name = FileNameHelper.ExtractSafeFileName(fileInfo),
            Extension = fileInfo.Extension,
            Size = fileInfo.Length,
            CreatedAt = fileInfo.CreationTime,
            ModifiedAt = fileInfo.LastWriteTime
        };
    }

    private static SourceFileInfo CreateStreamMetadata(string fileName, string content)
    {
        return new SourceFileInfo
        {
            Name = fileName,
            Extension = Path.GetExtension(fileName),
            Size = content.Length,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Markdig AST에서 구조적 콘텐츠 추출
    /// 테이블, 헤더, 리스트 등의 구조를 보존
    /// </summary>
    private static string ExtractStructuredContent(MarkdownDocument document, string originalText)
    {
        var content = new StringBuilder();

        foreach (var block in document)
        {
            ExtractBlock(block, content, originalText);
        }

        return content.ToString().Trim();
    }

    private static void ExtractBlock(Block block, StringBuilder content, string originalText)
    {
        switch (block)
        {
            case HeadingBlock heading:
                ExtractHeading(heading, content);
                break;

            case Table table:
                ExtractTable(table, content, originalText);
                break;

            case ParagraphBlock paragraph:
                ExtractParagraph(paragraph, content);
                break;

            case ListBlock list:
                ExtractList(list, content);
                break;

            case CodeBlock codeBlock:
                ExtractCodeBlock(codeBlock, content);
                break;

            case QuoteBlock quote:
                ExtractQuoteBlock(quote, content);
                break;

            default:
                // 기타 블록은 기본 렌더링 - Markdig의 기본 텍스트 변환 사용
                using (var writer = new StringWriter())
                {
                    var renderer = new Markdig.Renderers.Normalize.NormalizeRenderer(writer);
                    renderer.Render(block);
                    content.Append(writer.ToString());
                }
                break;
        }

        content.AppendLine();
    }

    /// <summary>
    /// 테이블 구조를 완벽히 보존하여 추출
    /// </summary>
    private static void ExtractTable(Table table, StringBuilder content, string originalText)
    {
        if (table.Count == 0) return;

        // 테이블 시작 마커
        content.AppendLine("<!-- TABLE_START -->");

        // 테이블 헤더
        if (table[0] is TableRow headerRow)
        {
            content.Append("| ");
            foreach (var cell in headerRow)
            {
                if (cell is TableCell tableCell)
                {
                    var cellContent = ExtractTableCellContent(tableCell);
                    content.Append($"{cellContent.Trim()} | ");
                }
            }
            content.AppendLine();

            // 구분자 행 추가
            content.Append('|');
            for (int i = 0; i < headerRow.Count; i++)
            {
                content.Append("---|");
            }
            content.AppendLine();
        }

        // 테이블 데이터 행들
        for (int i = 1; i < table.Count; i++)
        {
            if (table[i] is TableRow dataRow)
            {
                content.Append("| ");
                foreach (var cell in dataRow)
                {
                    if (cell is TableCell tableCell)
                    {
                        var cellContent = ExtractTableCellContent(tableCell);
                        content.Append($"{cellContent.Trim()} | ");
                    }
                }
                content.AppendLine();
            }
        }

        // 테이블 종료 마커
        content.AppendLine("<!-- TABLE_END -->");
    }

    private static void ExtractHeading(HeadingBlock heading, StringBuilder content)
    {
        var headingText = ExtractInlines(heading.Inline);
        var prefix = new string('#', heading.Level);

        // 헤딩 시작 마커
        content.AppendLine($"<!-- HEADING_START:H{heading.Level} -->");
        content.AppendLine($"{prefix} {headingText}");
        // 헤딩 종료 마커
        content.AppendLine($"<!-- HEADING_END:H{heading.Level} -->");
    }

    private static void ExtractParagraph(ParagraphBlock paragraph, StringBuilder content)
    {
        var paragraphText = ExtractInlines(paragraph.Inline);
        content.AppendLine(paragraphText);
    }

    private static void ExtractList(ListBlock list, StringBuilder content)
    {
        // 리스트 시작 마커
        var listType = list.IsOrdered ? "ORDERED" : "UNORDERED";
        content.AppendLine($"<!-- LIST_START:{listType} -->");

        foreach (var item in list)
        {
            if (item is ListItemBlock listItem)
            {
                var bullet = list.IsOrdered ? "1. " : "• ";
                content.Append(bullet);

                foreach (var block in listItem)
                {
                    if (block is ParagraphBlock para)
                    {
                        var itemText = ExtractInlines(para.Inline);
                        content.AppendLine(itemText);
                    }
                }
            }
        }

        // 리스트 종료 마커
        content.AppendLine($"<!-- LIST_END:{listType} -->");
    }

    private static void ExtractCodeBlock(CodeBlock codeBlock, StringBuilder content)
    {
        // 코드 블록 시작 마커
        content.AppendLine("<!-- CODE_START -->");

        if (codeBlock is FencedCodeBlock fenced)
        {
            content.AppendLine($"```{fenced.Info ?? ""}");
            content.AppendLine(fenced.Lines.ToString());
            content.AppendLine("```");
        }
        else
        {
            content.AppendLine("```");
            content.AppendLine(codeBlock.Lines.ToString());
            content.AppendLine("```");
        }

        // 코드 블록 종료 마커
        content.AppendLine("<!-- CODE_END -->");
    }

    private static void ExtractQuoteBlock(QuoteBlock quote, StringBuilder content)
    {
        foreach (var block in quote)
        {
            var quoteContent = new StringBuilder();
            ExtractBlock(block, quoteContent, "");
            var lines = quoteContent.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                content.AppendLine($"> {line}");
            }
        }
    }

    /// <summary>
    /// 테이블 셀에서 텍스트 추출
    /// </summary>
    private static string ExtractTableCellContent(TableCell tableCell)
    {
        var text = new StringBuilder();
        foreach (var block in tableCell)
        {
            if (block is ParagraphBlock paragraph)
            {
                var content = ExtractInlines(paragraph.Inline);
                text.Append(content);
            }
        }
        return text.ToString();
    }

    /// <summary>
    /// 인라인 요소들(링크, 강조, 이미지 등)에서 텍스트 추출
    /// </summary>
    private static string ExtractInlines(ContainerInline? inline)
    {
        if (inline == null) return string.Empty;

        var text = new StringBuilder();
        ExtractInlinesRecursive(inline, text);
        return text.ToString();
    }

    private static void ExtractInlinesRecursive(ContainerInline container, StringBuilder text)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    text.Append(literal.Content);
                    break;

                case LineBreakInline lineBreak:
                    // 마크다운 표준: hard break는 두 개의 공백 후 줄바꿈, soft break는 그냥 줄바꿈
                    text.Append(lineBreak.IsHard ? "  \n" : "\n");
                    break;

                case EmphasisInline emphasis:
                    var emphasisText = new StringBuilder();
                    ExtractInlinesRecursive(emphasis, emphasisText);
                    var marker = emphasis.DelimiterChar == '*' ? "**" : "__";
                    text.Append($"{marker}{emphasisText}{marker}");
                    break;

                case LinkInline link:
                    var linkText = new StringBuilder();
                    ExtractInlinesRecursive(link, linkText);
                    text.Append($"[{linkText}]({link.Url})");
                    break;

                case CodeInline code:
                    text.Append($"`{code.Content}`");
                    break;

                case ContainerInline containerInline:
                    ExtractInlinesRecursive(containerInline, text);
                    break;

                default:
                    // 기타 인라인 요소들
                    if (inline is ContainerInline otherContainer)
                    {
                        ExtractInlinesRecursive(otherContainer, text);
                    }
                    break;
            }
        }
    }
}

