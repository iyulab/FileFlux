using System.Text.RegularExpressions;
using FileFlux.Core;

namespace FileFlux.Infrastructure;

/// <summary>
/// Rule-based markdown normalizer that fixes structural issues without AI.
/// Applies corrections for heading hierarchy, list structure, and whitespace.
/// </summary>
public partial class MarkdownNormalizer : IMarkdownNormalizer
{
    // Regex patterns for markdown elements
    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^(#{1,6})\s*$")]
    private static partial Regex EmptyHeadingPattern();

    [GeneratedRegex(@"^(\s*)([-*+]|\d+\.)\s+(.*)$")]
    private static partial Regex ListItemPattern();

    [GeneratedRegex(@"^\s*\|.*\|\s*$")]
    private static partial Regex TableRowPattern();

    [GeneratedRegex(@"^\s*\|[\s\-:|\+]+\|\s*$")]
    private static partial Regex TableSeparatorPattern();

    // Patterns that indicate annotation/comment rather than heading
    private static readonly string[] AnnotationPatterns =
    [
        @"^\s*[\(（].*[\)）]\s*$",           // (증가율), （감소율）
        @"^\s*※",                            // ※ 주석
        @"^\s*\*\s*$",                       // * (single asterisk)
        @"^\s*•",                            // • 불릿
        @"^\s*\d+\.\s*$",                    // 숫자만 "1." "2."
        @"^\s*[.,:;]+\s*$",                  // 구두점만
    ];

    /// <inheritdoc />
    public NormalizationResult Normalize(string markdown, NormalizationOptions? options = null)
    {
        options ??= NormalizationOptions.Default;
        var actions = new List<NormalizationAction>();
        var lines = markdown.Split('\n').ToList();
        var stats = new NormalizationStats { TotalLines = lines.Count };

        // Phase 1: Demote annotation-like headings to plain text
        if (options.DemoteAnnotationHeadings)
        {
            DemoteAnnotationHeadings(lines, actions, stats);
        }

        // Phase 2: Remove empty/meaningless headings
        if (options.RemoveEmptyHeadings)
        {
            RemoveEmptyHeadings(lines, actions, stats);
        }

        // Phase 3: Normalize heading hierarchy
        if (options.NormalizeHeadings)
        {
            NormalizeHeadingHierarchy(lines, actions, stats, options);
        }

        // Phase 4: Normalize list structure
        if (options.NormalizeLists)
        {
            NormalizeListStructure(lines, actions, stats);
        }

        // Phase 5: Normalize tables
        if (options.NormalizeTables)
        {
            NormalizeTables(lines, actions, stats, options);
        }

        // Phase 6: Normalize whitespace
        if (options.NormalizeWhitespace)
        {
            NormalizeWhitespace(lines, actions, stats);
        }

        // Count headings after normalization
        stats.HeadingsFound = lines.Count(l => HeadingPattern().IsMatch(l));

        return new NormalizationResult
        {
            Markdown = string.Join('\n', lines),
            OriginalMarkdown = markdown,
            Actions = actions,
            Stats = stats
        };
    }

    /// <summary>
    /// Demote annotation-like headings to plain text.
    /// Examples: "## (증가율)" → "(증가율)", "### ※ 주석" → "※ 주석"
    /// </summary>
    private static void DemoteAnnotationHeadings(
        List<string> lines,
        List<NormalizationAction> actions,
        NormalizationStats stats)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var match = HeadingPattern().Match(lines[i]);
            if (!match.Success) continue;

            var content = match.Groups[2].Value;

            foreach (var pattern in AnnotationPatterns)
            {
                if (Regex.IsMatch(content, pattern))
                {
                    var original = lines[i];
                    lines[i] = content; // Remove heading markers

                    actions.Add(new NormalizationAction
                    {
                        Type = NormalizationActionType.AnnotationHeadingDemoted,
                        LineNumber = i + 1,
                        Before = original,
                        After = content,
                        Reason = "Annotation pattern detected, demoted to plain text"
                    });
                    stats.HeadingsDemoted++;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Remove empty or meaningless headings.
    /// Examples: "##" (no content), "### " (whitespace only)
    /// </summary>
    private static void RemoveEmptyHeadings(
        List<string> lines,
        List<NormalizationAction> actions,
        NormalizationStats stats)
    {
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            var line = lines[i];

            // Check for empty heading pattern
            if (EmptyHeadingPattern().IsMatch(line))
            {
                actions.Add(new NormalizationAction
                {
                    Type = NormalizationActionType.EmptyHeadingRemoved,
                    LineNumber = i + 1,
                    Before = line,
                    After = "(removed)",
                    Reason = "Empty heading removed"
                });
                lines.RemoveAt(i);
                stats.HeadingsRemoved++;
                continue;
            }

            // Check for heading with only whitespace content
            var match = HeadingPattern().Match(line);
            if (match.Success && string.IsNullOrWhiteSpace(match.Groups[2].Value))
            {
                actions.Add(new NormalizationAction
                {
                    Type = NormalizationActionType.EmptyHeadingRemoved,
                    LineNumber = i + 1,
                    Before = line,
                    After = "(removed)",
                    Reason = "Whitespace-only heading removed"
                });
                lines.RemoveAt(i);
                stats.HeadingsRemoved++;
            }
        }
    }

    /// <summary>
    /// Normalize heading hierarchy to prevent level jumps.
    /// H1 → H5 becomes H1 → H2. First heading is promoted if too deep.
    /// </summary>
    private static void NormalizeHeadingHierarchy(
        List<string> lines,
        List<NormalizationAction> actions,
        NormalizationStats stats,
        NormalizationOptions options)
    {
        int lastLevel = 0;
        bool isFirstHeading = true;

        for (int i = 0; i < lines.Count; i++)
        {
            var match = HeadingPattern().Match(lines[i]);
            if (!match.Success) continue;

            int currentLevel = match.Groups[1].Value.Length;
            string content = match.Groups[2].Value;

            // Handle first heading - promote if too deep
            if (isFirstHeading)
            {
                isFirstHeading = false;

                if (currentLevel > options.MaxFirstHeadingLevel)
                {
                    var newLevel = 1;
                    var original = lines[i];
                    lines[i] = $"# {content}";

                    actions.Add(new NormalizationAction
                    {
                        Type = NormalizationActionType.FirstHeadingPromoted,
                        LineNumber = i + 1,
                        Before = $"H{currentLevel}: {TruncateForDisplay(content)}",
                        After = $"H{newLevel}: {TruncateForDisplay(content)}",
                        Reason = $"First heading promoted from H{currentLevel} to H{newLevel}"
                    });
                    stats.HeadingsAdjusted++;
                    lastLevel = newLevel;
                    continue;
                }

                lastLevel = currentLevel;
                continue;
            }

            // Check for level jump violation
            int maxAllowedLevel = lastLevel + options.MaxHeadingLevelJump;

            if (currentLevel > maxAllowedLevel && lastLevel > 0)
            {
                var newLevel = maxAllowedLevel;
                var original = lines[i];
                lines[i] = new string('#', newLevel) + " " + content;

                actions.Add(new NormalizationAction
                {
                    Type = NormalizationActionType.HeadingLevelAdjusted,
                    LineNumber = i + 1,
                    Before = $"H{currentLevel}: {TruncateForDisplay(content)}",
                    After = $"H{newLevel}: {TruncateForDisplay(content)}",
                    Reason = $"Level jump H{lastLevel} → H{currentLevel} exceeded max jump of {options.MaxHeadingLevelJump}"
                });
                stats.HeadingsAdjusted++;
                lastLevel = newLevel;
            }
            else
            {
                lastLevel = currentLevel;
            }
        }
    }

    /// <summary>
    /// Normalize list structure and indentation consistency.
    /// </summary>
    private static void NormalizeListStructure(
        List<string> lines,
        List<NormalizationAction> actions,
        NormalizationStats stats)
    {
        int? baseIndent = null;
        int lastIndent = 0;

        for (int i = 0; i < lines.Count; i++)
        {
            var match = ListItemPattern().Match(lines[i]);
            if (!match.Success)
            {
                // Reset on non-list line (but not blank lines within lists)
                if (!string.IsNullOrWhiteSpace(lines[i]))
                {
                    baseIndent = null;
                    lastIndent = 0;
                }
                continue;
            }

            var indent = match.Groups[1].Value.Length;
            var marker = match.Groups[2].Value;
            var content = match.Groups[3].Value;

            // First list item sets the base
            if (baseIndent == null)
            {
                baseIndent = indent;
                lastIndent = indent;
                continue;
            }

            // Check for excessive indent jump (more than 4 spaces)
            if (indent > lastIndent + 4)
            {
                var newIndent = lastIndent + 2;
                var original = lines[i];
                lines[i] = new string(' ', newIndent) + $"{marker} {content}";

                actions.Add(new NormalizationAction
                {
                    Type = NormalizationActionType.ListIndentNormalized,
                    LineNumber = i + 1,
                    Before = $"indent={indent}",
                    After = $"indent={newIndent}",
                    Reason = $"List indent jump from {lastIndent} to {indent} normalized"
                });
                stats.ListItemsNormalized++;
                lastIndent = newIndent;
            }
            else
            {
                lastIndent = indent;
            }
        }
    }

    /// <summary>
    /// Normalize tables by validating column consistency.
    /// Malformed or complex tables are converted to text blocks with hints.
    /// </summary>
    private static void NormalizeTables(
        List<string> lines,
        List<NormalizationAction> actions,
        NormalizationStats stats,
        NormalizationOptions options)
    {
        var tableBlocks = FindTableBlocks(lines);
        stats.TablesFound = tableBlocks.Count;

        // Process in reverse to avoid index shifting issues
        for (int t = tableBlocks.Count - 1; t >= 0; t--)
        {
            var (startLine, endLine) = tableBlocks[t];
            var tableLines = lines.Skip(startLine).Take(endLine - startLine + 1).ToList();

            var validationResult = ValidateTable(tableLines, options.MaxColumnVariance);

            if (!validationResult.IsValid)
            {
                // Convert malformed table to text block with hint
                var convertedLines = ConvertTableToTextBlock(tableLines, validationResult.Reason);

                // Replace table lines with converted text
                for (int i = endLine; i >= startLine; i--)
                {
                    lines.RemoveAt(i);
                }

                for (int i = 0; i < convertedLines.Count; i++)
                {
                    lines.Insert(startLine + i, convertedLines[i]);
                }

                actions.Add(new NormalizationAction
                {
                    Type = validationResult.Reason.Contains("complex", StringComparison.OrdinalIgnoreCase)
                        ? NormalizationActionType.ComplexTableConverted
                        : NormalizationActionType.MalformedTableConverted,
                    LineNumber = startLine + 1,
                    Before = $"Table with {tableLines.Count} rows",
                    After = $"Text block with <table> hint",
                    Reason = validationResult.Reason
                });
                stats.TablesConvertedToText++;
            }
            else
            {
                stats.TablesPreserved++;
            }
        }
    }

    /// <summary>
    /// Find all table blocks in the document.
    /// Returns list of (startLine, endLine) tuples.
    /// </summary>
    private static List<(int Start, int End)> FindTableBlocks(List<string> lines)
    {
        var blocks = new List<(int Start, int End)>();
        int? tableStart = null;

        for (int i = 0; i < lines.Count; i++)
        {
            var isTableRow = TableRowPattern().IsMatch(lines[i]);

            if (isTableRow && tableStart == null)
            {
                tableStart = i;
            }
            else if (!isTableRow && tableStart != null)
            {
                // Table ended at previous line
                blocks.Add((tableStart.Value, i - 1));
                tableStart = null;
            }
        }

        // Handle table at end of document
        if (tableStart != null)
        {
            blocks.Add((tableStart.Value, lines.Count - 1));
        }

        return blocks;
    }

    /// <summary>
    /// Validate a table's structure.
    /// </summary>
    private static (bool IsValid, string Reason) ValidateTable(
        List<string> tableLines,
        int maxColumnVariance)
    {
        if (tableLines.Count < 2)
        {
            return (false, "Table has fewer than 2 rows");
        }

        var columnCounts = new List<int>();
        bool hasSeparator = false;

        foreach (var line in tableLines)
        {
            if (TableSeparatorPattern().IsMatch(line))
            {
                hasSeparator = true;
                continue;
            }

            // Count columns by counting pipe characters (minus outer pipes)
            var pipeCount = line.Count(c => c == '|');
            var colCount = pipeCount > 1 ? pipeCount - 1 : pipeCount;
            columnCounts.Add(colCount);
        }

        if (columnCounts.Count == 0)
        {
            return (false, "No data rows in table");
        }

        // Check column count consistency
        var minCols = columnCounts.Min();
        var maxCols = columnCounts.Max();
        var variance = maxCols - minCols;

        if (variance > maxColumnVariance)
        {
            return (false, $"Column count varies from {minCols} to {maxCols} (variance: {variance}, max allowed: {maxColumnVariance})");
        }

        // Check for signs of merged cells or complex structure
        if (!hasSeparator && tableLines.Count > 2)
        {
            // Table without separator line might be malformed
            return (false, "Complex table structure (missing separator)");
        }

        // Check for very wide variance in cell content length (might indicate merged cells)
        var cellLengths = tableLines
            .Where(l => !TableSeparatorPattern().IsMatch(l))
            .SelectMany(l => l.Split('|', StringSplitOptions.RemoveEmptyEntries))
            .Select(c => c.Trim().Length)
            .ToList();

        if (cellLengths.Count > 0)
        {
            var avgLength = cellLengths.Average();
            var maxLength = cellLengths.Max();

            // If max cell is more than 5x average, might be merged cell content
            if (maxLength > avgLength * 5 && maxLength > 100)
            {
                return (false, "Complex table structure (possible merged cells)");
            }
        }

        return (true, string.Empty);
    }

    /// <summary>
    /// Convert a table to a text block with hint for LLM processing.
    /// </summary>
    private static List<string> ConvertTableToTextBlock(List<string> tableLines, string reason)
    {
        var result = new List<string>
        {
            $"<!-- table: {reason} -->",
            "<table>"
        };

        foreach (var line in tableLines)
        {
            // Remove pipe characters and clean up
            var cleanedLine = line.Trim();
            if (cleanedLine.StartsWith('|'))
                cleanedLine = cleanedLine[1..];
            if (cleanedLine.EndsWith('|'))
                cleanedLine = cleanedLine[..^1];

            // Replace remaining pipes with tabs or spaces
            cleanedLine = cleanedLine.Replace('|', '\t').Trim();

            if (!string.IsNullOrWhiteSpace(cleanedLine) &&
                !TableSeparatorPattern().IsMatch(line))
            {
                result.Add(cleanedLine);
            }
        }

        result.Add("</table>");
        return result;
    }

    /// <summary>
    /// Normalize whitespace - remove excessive blank lines and trailing spaces.
    /// </summary>
    private static void NormalizeWhitespace(
        List<string> lines,
        List<NormalizationAction> actions,
        NormalizationStats stats)
    {
        // Remove trailing whitespace from each line
        for (int i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimEnd();
            if (trimmed != lines[i])
            {
                lines[i] = trimmed;
                // Don't log individual trailing whitespace removals to reduce noise
            }
        }

        // Remove excessive consecutive blank lines (more than 2)
        int consecutiveBlankLines = 0;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                consecutiveBlankLines++;
                if (consecutiveBlankLines > 2)
                {
                    lines.RemoveAt(i);
                    stats.BlankLinesRemoved++;
                }
            }
            else
            {
                consecutiveBlankLines = 0;
            }
        }

        // Log if significant blank lines were removed
        if (stats.BlankLinesRemoved > 0)
        {
            actions.Add(new NormalizationAction
            {
                Type = NormalizationActionType.ExcessiveBlankLinesRemoved,
                LineNumber = 0,
                Before = $"{stats.BlankLinesRemoved} excessive blank lines",
                After = "removed",
                Reason = "Consecutive blank lines normalized to max 2"
            });
        }
    }

    /// <summary>
    /// Truncate content for display in action messages.
    /// </summary>
    private static string TruncateForDisplay(string content, int maxLength = 40)
    {
        if (string.IsNullOrEmpty(content)) return "(empty)";
        if (content.Length <= maxLength) return content;
        return content[..(maxLength - 3)] + "...";
    }
}
