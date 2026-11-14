using FileFlux.Core;
using Markdig;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Renderers;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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

    // ========================================
    // Stage 0: Read (Document Structure)
    // ========================================

    public async Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Markdown file not found: {filePath}");

        var startTime = DateTime.UtcNow;
        var fileInfo = new FileInfo(filePath);

        var result = new ReadResult
        {
            File = new SourceFileInfo
            {
                Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                Extension = fileInfo.Extension,
                Size = fileInfo.Length,
                CreatedAt = fileInfo.CreationTime,
                ModifiedAt = fileInfo.LastWriteTime
            },
            ReaderType = ReaderType,
            Duration = DateTime.UtcNow - startTime
        };

        // Markdown files are single-page documents
        result.Pages.Add(new PageInfo
        {
            Number = 1,
            HasContent = fileInfo.Length > 0,
            Props = { ["file_type"] = "markdown" }
        });

        return result;
    }

    public Task<ReadResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(stream);

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name cannot be null or empty", nameof(fileName));

        var startTime = DateTime.UtcNow;

        var result = new ReadResult
        {
            File = new SourceFileInfo
            {
                Name = fileName,
                Extension = Path.GetExtension(fileName),
                Size = stream.CanSeek ? stream.Length : 0,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            },
            ReaderType = ReaderType,
            Duration = DateTime.UtcNow - startTime
        };

        result.Pages.Add(new PageInfo
        {
            Number = 1,
            HasContent = stream.Length > 0,
            Props = { ["file_type"] = "markdown" }
        });

        return Task.FromResult(result);
    }

    // ========================================
    // Stage 1: Extract (Raw Content)
    // ========================================

    public async Task<RawContent> ExtractAsync(string filePath, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Markdown file not found: {filePath}");

        var markdownText = TextDecoder.Decode(await File.ReadAllBytesAsync(filePath, cancellationToken)).Text;
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

    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(stream);

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name cannot be null or empty", nameof(fileName));

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var markdownText = TextDecoder.Decode(buffer.ToArray()).Text;
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

        // StringBuilder.AppendLine writes Environment.NewLine while Markdig's line groups and breaks use '\n': one line
        // ending, the same on every platform.
        return content.ToString().ReplaceLineEndings("\n").Trim();
    }

    private static void ExtractBlock(Block block, StringBuilder content, string originalText)
    {
        switch (block)
        {
            // Link reference definitions ([label]: url) are Markdig metadata, not renderable
            // content. They have no matching case and would otherwise fall through to the
            // NormalizeRenderer default arm, leaking the raw definition (and a trailing []:)
            // into chunk text. Skip them entirely; inline links keep their own display text.
            case LinkReferenceDefinitionGroup:
            case LinkReferenceDefinition:
                return;

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

            case DefinitionList definitions:
                ExtractDefinitionList(definitions, content);
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
                    content.Append(CultureInfo.InvariantCulture, $"{EscapeCellPipes(cellContent.Trim())} | ");
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
                        content.Append(CultureInfo.InvariantCulture, $"{EscapeCellPipes(cellContent.Trim())} | ");
                    }
                }
                content.AppendLine();
            }
        }

        // 테이블 종료 마커
        content.AppendLine("<!-- TABLE_END -->");
    }

    /// <summary>
    /// A literal <c>|</c> in a cell (written <c>\|</c> in the source, which the parser unescapes) goes back out escaped: written
    /// bare it cuts the cell in two and the row grows a column the header does not have.
    /// </summary>
    private static string EscapeCellPipes(string cellText) => cellText.Replace("|", "\\|", StringComparison.Ordinal);

    private static void ExtractHeading(HeadingBlock heading, StringBuilder content)
    {
        var headingText = ExtractInlines(heading.Inline);
        var prefix = new string('#', heading.Level);

        // 헤딩 시작 마커
        content.AppendLine(CultureInfo.InvariantCulture, $"<!-- HEADING_START:H{heading.Level} -->");
        content.AppendLine(CultureInfo.InvariantCulture, $"{prefix} {headingText}");
        // 헤딩 종료 마커
        content.AppendLine(CultureInfo.InvariantCulture, $"<!-- HEADING_END:H{heading.Level} -->");
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
        content.AppendLine(CultureInfo.InvariantCulture, $"<!-- LIST_START:{listType} -->");

        AppendListItems(list, content, depth: 0);

        // 리스트 종료 마커
        content.AppendLine(CultureInfo.InvariantCulture, $"<!-- LIST_END:{listType} -->");
    }

    /// <summary>
    /// Writes the items of a list. An item holds blocks, not just one paragraph: a nested list, a second paragraph, a code
    /// block or a quote under a bullet are part of that item, and each is written (indented under the bullet) rather than
    /// dropped. Only the outermost list carries the LIST_START / LIST_END markers.
    /// </summary>
    private static void AppendListItems(ListBlock list, StringBuilder content, int depth)
    {
        var indent = new string(' ', depth * 2);
        // An ordered list numbers from the number it starts at (`3.` `4.`), as the document wrote it.
        var itemIndex = list.IsOrdered && int.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out var start) ? start : 1;
        foreach (var item in list)
        {
            if (item is not ListItemBlock listItem) continue;

            var bullet = list.IsOrdered ? $"{itemIndex++}. " : "• ";
            content.Append(indent).Append(bullet);

            var first = true;
            foreach (var block in listItem)
            {
                if (block is ParagraphBlock para)
                {
                    var itemText = ExtractInlines(para.Inline);
                    content.AppendLine(first ? itemText : $"{indent}  {itemText}");
                }
                else
                {
                    // The bullet stands alone on its line when the item opens with a nested list, a code block or a quote.
                    if (first) content.AppendLine();

                    if (block is ListBlock nested)
                    {
                        AppendListItems(nested, content, depth + 1);
                    }
                    else
                    {
                        var rendered = new StringBuilder();
                        ExtractBlock(block, rendered, "");
                        foreach (var line in rendered.ToString().ReplaceLineEndings("\n").Split('\n'))
                        {
                            if (line.Length > 0) content.Append(indent).Append("  ").AppendLine(line);
                        }
                    }
                }

                first = false;
            }

            if (first) content.AppendLine(); // an empty item still ends its line
        }
    }

    private static int LongestBacktickRun(string text)
    {
        int longest = 0, run = 0;
        foreach (var c in text)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        return longest;
    }

    private static void ExtractCodeBlock(CodeBlock codeBlock, StringBuilder content)
    {
        // 코드 블록 시작 마커
        content.AppendLine("<!-- CODE_START -->");

        // The fence is longer than any run of backticks in the code, so a sample that shows a fence (a Markdown example
        // inside a Markdown document) stays inside its block.
        var code = codeBlock.Lines.ToString();
        var fence = new string('`', Math.Max(3, LongestBacktickRun(code) + 1));
        // (A backtick fence's info string holds no backtick: one from a tilde fence would turn the opening line into text.)
        var info = codeBlock is FencedCodeBlock fenced ? (fenced.Info ?? "").Replace("`", string.Empty, StringComparison.Ordinal) : "";

        content.AppendLine(CultureInfo.InvariantCulture, $"{fence}{info}");
        content.AppendLine(code);
        content.AppendLine(fence);

        // 코드 블록 종료 마커
        content.AppendLine("<!-- CODE_END -->");
    }

    /// <summary>
    /// A definition list (<c>Term</c> then <c>: definition</c>): the term and every definition are written, the term on its
    /// own line and each definition after a colon. The terms were dropped when the list fell through to the default renderer.
    /// </summary>
    private static void ExtractDefinitionList(DefinitionList definitions, StringBuilder content)
    {
        foreach (var item in definitions)
        {
            if (item is not DefinitionItem definitionItem) continue;

            foreach (var child in definitionItem)
            {
                switch (child)
                {
                    case DefinitionTerm term:
                        content.AppendLine(ExtractInlines(term.Inline));
                        break;

                    case ParagraphBlock paragraph:
                        content.AppendLine(CultureInfo.InvariantCulture, $": {ExtractInlines(paragraph.Inline)}");
                        break;

                    default:
                        var rendered = new StringBuilder();
                        ExtractBlock(child, rendered, "");
                        foreach (var line in rendered.ToString().ReplaceLineEndings("\n").Split('\n'))
                        {
                            if (line.Length > 0) content.Append("  ").AppendLine(line);
                        }

                        break;
                }
            }
        }
    }

    private static void ExtractQuoteBlock(QuoteBlock quote, StringBuilder content)
    {
        foreach (var block in quote)
        {
            var quoteContent = new StringBuilder();
            ExtractBlock(block, quoteContent, "");
            var lines = quoteContent.ToString().ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                content.AppendLine(CultureInfo.InvariantCulture, $"> {line}");
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
                var content = ExtractInlines(paragraph.Inline, htmlBreak: "<br>");
                text.Append(content);
            }
        }
        return text.ToString();
    }

    /// <summary>
    /// 인라인 요소들(링크, 강조, 이미지 등)에서 텍스트 추출
    /// </summary>
    private static string ExtractInlines(ContainerInline? inline, string htmlBreak = "  \n")
    {
        if (inline == null) return string.Empty;

        var text = new StringBuilder();
        ExtractInlinesRecursive(inline, text, htmlBreak);
        return text.ToString();
    }

    private static readonly Regex HtmlBreakTag = new(@"^<br\s*/?>$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static void ExtractInlinesRecursive(ContainerInline container, StringBuilder text, string htmlBreak)
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

                case HtmlInline html:
                    // Inline HTML tags are markup, not words - but <br> is a line break: without it the words around it fuse.
                    if (HtmlBreakTag.IsMatch(html.Tag)) text.Append(htmlBreak);
                    break;

                case EmphasisInline emphasis:
                    var emphasisText = new StringBuilder();
                    ExtractInlinesRecursive(emphasis, emphasisText, htmlBreak);
                    var marker = emphasis.DelimiterChar == '*' ? "**" : "__";
                    text.Append(CultureInfo.InvariantCulture, $"{marker}{emphasisText}{marker}");
                    break;

                case LinkInline link:
                    var linkText = new StringBuilder();
                    ExtractInlinesRecursive(link, linkText, htmlBreak);
                    var label = linkText.ToString();
                    if (link.IsImage)
                    {
                        // Images keep the `!` marker and path so downstream can tell them apart
                        // from text/links (consistent with DocumentRefiner/MarkdownConverter which
                        // emit `![alt](path)`). Empty alt still renders `![](url)` — the marker matters.
                        text.Append(CultureInfo.InvariantCulture, $"![{label}]({link.Url})");
                    }
                    else if (label.Length > 0)
                    {
                        text.Append(CultureInfo.InvariantCulture, $"[{label}]({link.Url})");
                    }
                    else if (!string.IsNullOrEmpty(link.Url))
                    {
                        // Empty link text: emit the url only, never a bare `[]` bracket token (noise).
                        text.Append(link.Url);
                    }
                    break;

                case CodeInline code:
                    text.Append(CultureInfo.InvariantCulture, $"`{code.Content}`");
                    break;

                case ContainerInline containerInline:
                    ExtractInlinesRecursive(containerInline, text, htmlBreak);
                    break;

                default:
                    // 기타 인라인 요소들
                    if (inline is ContainerInline otherContainer)
                    {
                        ExtractInlinesRecursive(otherContainer, text, htmlBreak);
                    }
                    break;
            }
        }
    }
}

