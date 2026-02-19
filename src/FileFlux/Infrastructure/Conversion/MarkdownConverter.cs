using FileFlux.Core;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

namespace FileFlux.Infrastructure.Conversion;

/// <summary>
/// RawContent를 구조화된 Markdown으로 변환하는 서비스
/// 휴리스틱 기반 변환이 기본이며, IDocumentAnalysisService가 DI로 제공된 경우 LLM 추론 가능
/// </summary>
public class MarkdownConverter : IMarkdownConverter
{
    private static readonly string[] s_lineSeparators = ["\r\n", "\n"];
    private readonly IDocumentAnalysisService? _textCompletionService;

    /// <summary>
    /// 기본 생성자 - 휴리스틱만 사용
    /// </summary>
    public MarkdownConverter()
    {
        _textCompletionService = null;
    }

    /// <summary>
    /// DI 생성자 - 선택적 LLM 지원
    /// </summary>
    public MarkdownConverter(IServiceProvider serviceProvider)
    {
        _textCompletionService = serviceProvider.GetService<IDocumentAnalysisService>();
    }

    /// <summary>
    /// IDocumentAnalysisService를 직접 주입받는 생성자
    /// </summary>
    public MarkdownConverter(IDocumentAnalysisService? textCompletionService)
    {
        _textCompletionService = textCompletionService;
    }

    public async Task<MarkdownConversionResult> ConvertAsync(
        RawContent rawContent,
        MarkdownConversionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MarkdownConversionOptions();
        var result = new MarkdownConversionResult
        {
            OriginalLength = rawContent.Text?.Length ?? 0
        };

        if (string.IsNullOrWhiteSpace(rawContent.Text))
        {
            result.IsSuccess = true;
            result.Method = ConversionMethod.Heuristic;
            result.Warnings.Add("Empty content provided");
            return result;
        }

        try
        {
            // 1. 휴리스틱 기반 변환 (기본)
            var markdown = ConvertWithHeuristics(rawContent, options, result.Statistics);
            result.Method = ConversionMethod.Heuristic;

            // 2. LLM 추론 옵션이 활성화되고 서비스가 있는 경우
            if (options.UseLLMInference && _textCompletionService != null)
            {
                var llmResult = await EnhanceWithLLM(markdown, rawContent, options, cancellationToken);
                if (llmResult != null)
                {
                    markdown = llmResult;
                    result.Method = ConversionMethod.Mixed;
                }
                else
                {
                    result.Warnings.Add("LLM enhancement failed, using heuristic result");
                }
            }
            else if (options.UseLLMInference && _textCompletionService == null)
            {
                result.Warnings.Add("LLM inference requested but IDocumentAnalysisService not available");
            }

            // 3. 후처리
            if (options.NormalizeWhitespace)
            {
                markdown = NormalizeWhitespace(markdown);
            }

            result.Markdown = markdown;
            result.IsSuccess = true;
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.Warnings.Add($"Conversion failed: {ex.Message}");
            result.Markdown = rawContent.Text ?? string.Empty;
        }

        return result;
    }

    /// <summary>
    /// 휴리스틱 기반 Markdown 변환
    /// </summary>
    private static string ConvertWithHeuristics(
        RawContent rawContent,
        MarkdownConversionOptions options,
        StructureStatistics stats)
    {
        var text = rawContent.Text ?? string.Empty;
        var sb = new StringBuilder();
        var lines = text.Split(s_lineSeparators, StringSplitOptions.None);

        // RawContent.Hints에서 구조 정보 추출
        var hasHeadings = rawContent.Hints?.ContainsKey("HasHeadings") == true;
        var hasTables = rawContent.HasTables ||
                        rawContent.Hints?.ContainsKey("HasTables") == true ||
                        rawContent.Hints?.ContainsKey("TableCount") == true;
        var hasLists = rawContent.Hints?.ContainsKey("HasLists") == true;
        var hasImages = rawContent.Hints?.ContainsKey("HasImages") == true;

        // RawContent.Tables에서 추출된 테이블이 있으면 먼저 마크다운으로 변환하여 추가
        if (options.ConvertTables && rawContent.HasTables)
        {
            foreach (var table in rawContent.Tables)
            {
                var tableMarkdown = ConvertTableDataToMarkdown(table);
                if (!string.IsNullOrEmpty(tableMarkdown))
                {
                    sb.AppendLine(tableMarkdown);
                    sb.AppendLine();
                    stats.TableCount++;
                }
            }
            sb.AppendLine();
        }

        bool inCodeBlock = false;
        bool inTable = false;
        var tableBuffer = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmedLine = line.Trim();

            // 코드 블록 감지
            if (options.DetectCodeBlocks && IsCodeBlockMarker(trimmedLine))
            {
                inCodeBlock = !inCodeBlock;
                sb.AppendLine(line);
                if (!inCodeBlock) stats.CodeBlockCount++;
                continue;
            }

            if (inCodeBlock)
            {
                sb.AppendLine(line);
                continue;
            }

            // 테이블 처리
            if (options.ConvertTables && IsTableLine(trimmedLine))
            {
                inTable = true;
                tableBuffer.Add(trimmedLine);
                continue;
            }
            else if (inTable && !string.IsNullOrWhiteSpace(trimmedLine))
            {
                // 테이블이 계속되는지 확인
                if (IsTableLine(trimmedLine) || IsTableSeparator(trimmedLine))
                {
                    tableBuffer.Add(trimmedLine);
                    continue;
                }
                else
                {
                    // 테이블 종료, 버퍼 출력
                    OutputTable(sb, tableBuffer, stats);
                    tableBuffer.Clear();
                    inTable = false;
                }
            }
            else if (inTable && string.IsNullOrWhiteSpace(trimmedLine))
            {
                // 빈 줄로 테이블 종료
                OutputTable(sb, tableBuffer, stats);
                tableBuffer.Clear();
                inTable = false;
                sb.AppendLine();
                continue;
            }

            // 헤딩 감지 및 변환
            if (options.PreserveHeadings)
            {
                var headingResult = DetectAndConvertHeading(line, trimmedLine, options, stats);
                if (headingResult != null)
                {
                    sb.AppendLine(headingResult);
                    continue;
                }
            }

            // 리스트 감지
            if (options.PreserveLists)
            {
                var listResult = DetectAndConvertList(line, trimmedLine);
                if (listResult != null)
                {
                    sb.AppendLine(listResult);
                    stats.ListCount++;
                    continue;
                }
            }

            // 이미지 플레이스홀더 처리
            if (options.IncludeImagePlaceholders && IsImagePlaceholder(trimmedLine))
            {
                sb.AppendLine(ConvertImagePlaceholder(trimmedLine));
                stats.ImagePlaceholderCount++;
                continue;
            }

            // 일반 텍스트
            sb.AppendLine(line);
        }

        // 남은 테이블 버퍼 처리
        if (tableBuffer.Count > 0)
        {
            OutputTable(sb, tableBuffer, stats);
        }

        return sb.ToString();
    }

    /// <summary>
    /// LLM을 사용한 구조 추론 및 개선
    /// </summary>
    private async Task<string?> EnhanceWithLLM(
        string heuristicMarkdown,
        RawContent rawContent,
        MarkdownConversionOptions options,
        CancellationToken cancellationToken)
    {
        if (_textCompletionService == null)
            return null;

        try
        {
            var prompt = BuildLLMPrompt(heuristicMarkdown, rawContent);
            var response = await _textCompletionService.GenerateAsync(prompt, cancellationToken);

            if (string.IsNullOrWhiteSpace(response))
                return null;

            // LLM 응답에서 Markdown만 추출 (코드 블록 제거)
            return ExtractMarkdownFromLLMResponse(response);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildLLMPrompt(string heuristicMarkdown, RawContent rawContent)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Improve the following Markdown structure while preserving all content.");
        sb.AppendLine("Focus on: proper heading hierarchy, table formatting, list organization.");
        sb.AppendLine("Do NOT add new content, only restructure existing content.");
        sb.AppendLine();

        // 힌트 정보 추가
        if (rawContent.Hints?.Count > 0)
        {
            sb.AppendLine("Document hints:");
            foreach (var hint in rawContent.Hints.Take(5))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {hint.Key}: {hint.Value}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Input Markdown:");
        sb.AppendLine("```markdown");
        sb.AppendLine(heuristicMarkdown.Length > 8000
            ? heuristicMarkdown[..8000] + "\n... (truncated)"
            : heuristicMarkdown);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Output only the improved Markdown without explanation:");

        return sb.ToString();
    }

    private static string ExtractMarkdownFromLLMResponse(string response)
    {
        // ```markdown ... ``` 블록 추출
        var match = Regex.Match(response, @"```(?:markdown)?\s*\n(.*?)\n```", RegexOptions.Singleline);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        // 코드 블록이 없으면 전체 응답 반환
        return response.Trim();
    }

    #region Heuristic Detection Methods

    private static bool IsCodeBlockMarker(string line)
    {
        return line.StartsWith("```") || line.StartsWith("~~~");
    }

    private static bool IsTableLine(string line)
    {
        // 파이프로 구분된 테이블 라인 감지
        if (string.IsNullOrWhiteSpace(line)) return false;

        var pipeCount = line.Count(c => c == '|');
        return pipeCount >= 2 && !line.TrimStart().StartsWith('>');
    }

    private static bool IsTableSeparator(string line)
    {
        // | --- | --- | 형식의 구분선
        return Regex.IsMatch(line, @"^\|?\s*[-:]+\s*\|");
    }

    private static void OutputTable(StringBuilder sb, List<string> tableLines, StructureStatistics stats)
    {
        if (tableLines.Count == 0) return;

        // 이미 Markdown 테이블 형식인지 확인
        bool hasHeaderSeparator = tableLines.Any(l => IsTableSeparator(l));

        if (hasHeaderSeparator)
        {
            // 이미 Markdown 테이블 형식
            foreach (var line in tableLines)
            {
                sb.AppendLine(line);
            }
        }
        else
        {
            // 단순 파이프 구분 테이블 → Markdown 테이블로 변환
            for (int i = 0; i < tableLines.Count; i++)
            {
                sb.AppendLine(tableLines[i]);
                if (i == 0 && tableLines.Count > 1)
                {
                    // 헤더 후 구분선 추가
                    var columnCount = tableLines[0].Count(c => c == '|') + 1;
                    var separator = "|" + string.Join("|", Enumerable.Repeat(" --- ", Math.Max(1, columnCount - 1))) + "|";
                    sb.AppendLine(separator);
                }
            }
        }

        stats.TableCount++;
        sb.AppendLine();
    }

    private static string? DetectAndConvertHeading(
        string originalLine,
        string trimmedLine,
        MarkdownConversionOptions options,
        StructureStatistics stats)
    {
        // 이미 Markdown 헤딩인 경우
        var mdHeadingMatch = Regex.Match(trimmedLine, @"^(#{1,6})\s+(.+)$");
        if (mdHeadingMatch.Success)
        {
            var level = mdHeadingMatch.Groups[1].Value.Length;
            var adjustedLevel = AdjustHeadingLevel(level, options);
            stats.HeadingCount++;
            UpdateHeadingDistribution(stats, adjustedLevel);
            return new string('#', adjustedLevel) + " " + mdHeadingMatch.Groups[2].Value;
        }

        // 대문자로만 이루어진 짧은 라인 → 헤딩으로 추론
        if (trimmedLine.Length > 0 && trimmedLine.Length <= 100 &&
            trimmedLine.Equals(trimmedLine.ToUpperInvariant(), StringComparison.Ordinal) &&
            !trimmedLine.All(c => char.IsPunctuation(c) || char.IsWhiteSpace(c)))
        {
            if (Regex.IsMatch(trimmedLine, @"^[A-Z][A-Z0-9\s\-_]+$"))
            {
                var level = AdjustHeadingLevel(2, options); // 대문자만 있으면 H2로 추론
                stats.HeadingCount++;
                UpdateHeadingDistribution(stats, level);
                return new string('#', level) + " " + CultureAwareTitleCase(trimmedLine);
            }
        }

        // 숫자로 시작하는 섹션 (1. Introduction, 2.1 Background 등)
        var numberedSectionMatch = Regex.Match(trimmedLine, @"^(\d+(?:\.\d+)*)\s+([A-Z].+)$");
        if (numberedSectionMatch.Success)
        {
            var sectionNum = numberedSectionMatch.Groups[1].Value;
            var depth = sectionNum.Count(c => c == '.') + 1;
            var level = AdjustHeadingLevel(Math.Min(depth + 1, 6), options);
            stats.HeadingCount++;
            UpdateHeadingDistribution(stats, level);
            return new string('#', level) + " " + trimmedLine;
        }

        return null;
    }

    private static int AdjustHeadingLevel(int level, MarkdownConversionOptions options)
    {
        return Math.Max(options.MinHeadingLevel, Math.Min(options.MaxHeadingLevel, level));
    }

    private static void UpdateHeadingDistribution(StructureStatistics stats, int level)
    {
        if (!stats.HeadingLevelDistribution.ContainsKey(level))
            stats.HeadingLevelDistribution[level] = 0;
        stats.HeadingLevelDistribution[level]++;
    }

    private static string CultureAwareTitleCase(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var words = text.ToLowerInvariant().Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
            {
                words[i] = char.ToUpper(words[i][0], CultureInfo.InvariantCulture) + words[i][1..];
            }
        }
        return string.Join(" ", words);
    }

    private static string? DetectAndConvertList(string originalLine, string trimmedLine)
    {
        // 이미 Markdown 리스트인 경우
        if (Regex.IsMatch(trimmedLine, @"^[-*+]\s+.+") ||
            Regex.IsMatch(trimmedLine, @"^\d+\.\s+.+"))
        {
            return originalLine;
        }

        // 글머리 기호 패턴 (•, ●, ○, ■, □, ▪, ▸ 등)
        var bulletMatch = Regex.Match(trimmedLine, @"^[•●○■□▪▸►→]\s*(.+)$");
        if (bulletMatch.Success)
        {
            var indent = originalLine.Length - originalLine.TrimStart().Length;
            var prefix = new string(' ', indent);
            return prefix + "- " + bulletMatch.Groups[1].Value;
        }

        // 숫자/문자 + 괄호 패턴 (1), a), (a) 등)
        var numberedMatch = Regex.Match(trimmedLine, @"^(?:[\(\[])?([0-9a-zA-Z]+)[)\]\.]\s*(.+)$");
        if (numberedMatch.Success)
        {
            var indent = originalLine.Length - originalLine.TrimStart().Length;
            var prefix = new string(' ', indent);
            var num = numberedMatch.Groups[1].Value;

            // 숫자인 경우 번호 매기기 리스트
            if (int.TryParse(num, out _))
            {
                return prefix + num + ". " + numberedMatch.Groups[2].Value;
            }
            // 문자인 경우 글머리 기호 리스트
            return prefix + "- " + numberedMatch.Groups[2].Value;
        }

        return null;
    }

    private static bool IsImagePlaceholder(string line)
    {
        return line.Contains("<!-- IMAGE") ||
               line.Contains("[image:") ||
               line.Contains("embedded:img_") ||
               Regex.IsMatch(line, @"\[img_\d+\]", RegexOptions.IgnoreCase);
    }

    private static string ConvertImagePlaceholder(string line)
    {
        // <!-- IMAGE_START:IMG_1 --> 형식
        var commentMatch = Regex.Match(line, @"<!--\s*IMAGE[^>]*IMG[_]?(\d+)[^>]*-->", RegexOptions.IgnoreCase);
        if (commentMatch.Success)
        {
            return $"![image](embedded:img_{commentMatch.Groups[1].Value})";
        }

        // [image:description] 형식
        var bracketMatch = Regex.Match(line, @"\[image:([^\]]+)\]", RegexOptions.IgnoreCase);
        if (bracketMatch.Success)
        {
            return $"![{bracketMatch.Groups[1].Value}](embedded:img_000)";
        }

        // embedded:img_XXX가 이미 포함된 경우 그대로
        if (line.Contains("embedded:img_"))
        {
            return line;
        }

        // [img_N] 형식
        var imgMatch = Regex.Match(line, @"\[img[_]?(\d+)\]", RegexOptions.IgnoreCase);
        if (imgMatch.Success)
        {
            return $"![image](embedded:img_{imgMatch.Groups[1].Value})";
        }

        return line;
    }

    #endregion

    #region Post-processing

    private static string NormalizeWhitespace(string markdown)
    {
        // 먼저 \r\n을 \n으로 통일
        var normalized = markdown.Replace("\r\n", "\n");

        // 연속된 빈 줄을 2개로 제한
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");

        // 헤딩 앞뒤에 빈 줄 보장
        normalized = Regex.Replace(normalized, @"([^\n])\n(#{1,6}\s)", "$1\n\n$2");
        normalized = Regex.Replace(normalized, @"(#{1,6}\s[^\n]+)\n([^\n#])", "$1\n\n$2");

        // 코드 블록 앞뒤에 빈 줄 보장
        normalized = Regex.Replace(normalized, @"([^\n])\n(```)", "$1\n\n$2");
        normalized = Regex.Replace(normalized, @"(```)\n([^\n])", "$1\n\n$2");

        return normalized.Trim();
    }

    #endregion

    #region TableData to Markdown Conversion

    /// <summary>
    /// Converts TableData (from PDF extraction) to Markdown table format.
    /// </summary>
    private static string ConvertTableDataToMarkdown(TableData table)
    {
        if (table.Cells == null || table.Cells.Length == 0)
            return string.Empty;

        var sb = new StringBuilder();
        var columnCount = table.Cells.Max(row => row?.Length ?? 0);

        if (columnCount == 0)
            return string.Empty;

        // Determine headers
        // Note: TableData.Headers is a computed property that returns Cells[0] when HasHeader is true
        // So we always use Cells[0] as header and start data from row 1 when HasHeader is true
        string[] headers;
        int dataStartRow;

        if (table.HasHeader && table.Cells.Length > 0)
        {
            headers = table.Cells[0] ?? [];
            dataStartRow = 1;  // Skip first row since it's the header
        }
        else
        {
            // Generate column headers (Col1, Col2, etc.)
            headers = Enumerable.Range(1, columnCount).Select(i => $"Col{i}").ToArray();
            dataStartRow = 0;
        }

        // Ensure headers match column count
        if (headers.Length < columnCount)
        {
            var newHeaders = new string[columnCount];
            Array.Copy(headers, newHeaders, headers.Length);
            for (int i = headers.Length; i < columnCount; i++)
            {
                newHeaders[i] = $"Col{i + 1}";
            }
            headers = newHeaders;
        }

        // Header row
        sb.Append('|');
        foreach (var header in headers.Take(columnCount))
        {
            sb.Append(CultureInfo.InvariantCulture, $" {EscapeMarkdownTableCell(header ?? "")} |");
        }
        sb.AppendLine();

        // Separator row with alignment
        sb.Append('|');
        for (int i = 0; i < columnCount; i++)
        {
            TextAlignment? alignment = table.ColumnAlignments != null && i < table.ColumnAlignments.Length
                ? table.ColumnAlignments[i]
                : null;

            var separator = alignment switch
            {
                TextAlignment.Left => ":---",
                TextAlignment.Right => "---:",
                TextAlignment.Center => ":---:",
                TextAlignment.Justify => ":---:",
                _ => "---"
            };
            sb.Append(CultureInfo.InvariantCulture, $" {separator} |");
        }
        sb.AppendLine();

        // Data rows
        for (int rowIdx = dataStartRow; rowIdx < table.Cells.Length; rowIdx++)
        {
            var row = table.Cells[rowIdx];
            if (row == null) continue;

            sb.Append('|');
            for (int colIdx = 0; colIdx < columnCount; colIdx++)
            {
                var cell = colIdx < row.Length ? row[colIdx] : "";
                sb.Append(CultureInfo.InvariantCulture, $" {EscapeMarkdownTableCell(cell ?? "")} |");
            }
            sb.AppendLine();
        }

        // Add confidence warning comment if low confidence
        if (table.NeedsLlmAssist)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"<!-- Table confidence: {table.Confidence:F2} - may need verification -->");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Escapes special characters in markdown table cells.
    /// </summary>
    private static string EscapeMarkdownTableCell(string content)
    {
        if (string.IsNullOrEmpty(content))
            return "";

        // Replace pipe characters and newlines
        return content
            .Replace("|", "\\|")
            .Replace("\n", " ")
            .Replace("\r", "")
            .Trim();
    }

    #endregion
}
