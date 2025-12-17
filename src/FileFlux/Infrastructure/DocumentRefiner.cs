using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileFlux.Core;
using FluxCurator.Core;
using FluxCurator.Core.Infrastructure.Refining;
using Microsoft.Extensions.Logging;
using FluxCuratorTextRefineOptions = FluxCurator.Core.Domain.TextRefineOptions;

namespace FileFlux.Infrastructure;

/// <summary>
/// Default document refiner implementation.
/// Transforms RawContent into RefinedContent by cleaning, normalizing, and extracting structure.
/// </summary>
public sealed class DocumentRefiner : IDocumentRefiner
{
    private readonly ITextRefiner _textRefiner;
    private readonly IMarkdownConverter? _markdownConverter;
    private readonly ILogger<DocumentRefiner> _logger;

    /// <inheritdoc/>
    public string RefinerType => "DocumentRefiner";

    /// <inheritdoc/>
    public bool SupportsLlm => false;

    /// <summary>
    /// Creates a new document refiner.
    /// </summary>
    public DocumentRefiner(
        IMarkdownConverter? markdownConverter = null,
        ILogger<DocumentRefiner>? logger = null)
    {
        _textRefiner = TextRefiner.Instance;
        _markdownConverter = markdownConverter;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DocumentRefiner>.Instance;
    }

    /// <inheritdoc/>
    public async Task<RefinedContent> RefineAsync(
        RawContent raw,
        RefineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(raw);
        options ??= RefineOptions.Default;

        var sw = Stopwatch.StartNew();
        _logger.LogDebug("Starting refinement for {FileName}", raw.File.Name);

        try
        {
            var refinedText = raw.Text;
            var structures = new List<StructuredElement>();

            // Step 1: Clean noise (headers, footers, page numbers)
            if (options.CleanNoise)
            {
                refinedText = CleanDocumentNoise(refinedText);
            }

            // Step 2: Convert structured data to markdown
            // Priority: Use RawContent.Tables/Blocks if available, otherwise fallback to IMarkdownConverter
            var hasStructuredData = raw.HasTables || raw.HasBlocks;

            if (hasStructuredData)
            {
                // Use structured data from RawContent (new architecture)
                refinedText = ConvertStructuredToMarkdown(raw, options);
                _logger.LogDebug("Converted structured data to markdown: {TableCount} tables, {BlockCount} blocks",
                    raw.TableCount, raw.Blocks.Count);
            }
            else if ((options.ConvertTablesToMarkdown || options.ConvertBlocksToMarkdown) && _markdownConverter != null)
            {
                // Fallback to IMarkdownConverter for legacy readers
                var markdownResult = await _markdownConverter.ConvertAsync(raw, new MarkdownConversionOptions
                {
                    PreserveHeadings = true,
                    ConvertTables = true,
                    PreserveLists = true,
                    IncludeImagePlaceholders = true,
                    DetectCodeBlocks = true,
                    NormalizeWhitespace = true
                }, cancellationToken).ConfigureAwait(false);

                if (markdownResult.IsSuccess)
                {
                    refinedText = markdownResult.Markdown;
                }
            }

            // Step 3: Extract structured elements (tables, code blocks, lists) from text
            if (options.ExtractStructures)
            {
                // Add structures from RawContent.Tables first
                if (raw.HasTables)
                {
                    structures.AddRange(ConvertTablesToStructuredElements(raw.Tables));
                }

                // Then extract from text for additional structures
                structures.AddRange(ExtractStructuredElements(refinedText));
            }

            // Step 4: Text-level refinement via FluxCurator TextRefiner
            var textRefineOptions = FluxCuratorTextRefineOptions.Light;
            refinedText = _textRefiner.Refine(refinedText, textRefineOptions);

            // Step 5: Normalize whitespace
            if (options.NormalizeWhitespace)
            {
                refinedText = NormalizeWhitespace(refinedText);
            }

            // Build sections from text headings
            var sections = options.BuildSections ? BuildSections(refinedText) : [];

            // Build metadata
            var metadata = BuildMetadata(raw);

            var refined = new RefinedContent
            {
                RawId = raw.Id,
                Text = refinedText,
                Sections = sections,
                Structures = structures,
                Metadata = metadata,
                Quality = new RefinementQuality
                {
                    OriginalCharCount = raw.Text.Length,
                    RefinedCharCount = refinedText.Length,
                    StructureScore = CalculateStructureScore(structures.Count, sections.Count),
                    CleanupScore = CalculateCleanupScore(raw.Text.Length, refinedText.Length),
                    RetentionScore = CalculateRetentionScore(raw.Text.Length, refinedText.Length),
                    ConfidenceScore = 0.75
                },
                Info = new RefinementInfo
                {
                    RefinerType = RefinerType,
                    UsedLlm = options.UseLlm,
                    Duration = sw.Elapsed
                }
            };

            _logger.LogInformation("Refined {OriginalChars} → {RefinedChars} chars, {StructureCount} structures, {SectionCount} sections in {Duration:F2}s",
                raw.Text.Length, refinedText.Length, structures.Count, sections.Count, sw.Elapsed.TotalSeconds);

            return refined;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refinement failed for {FileName}", raw.File.Name);
            throw new DocumentProcessingException($"stream://{raw.File.Name}", $"Refinement failed: {ex.Message}", ex);
        }
    }

    #region Content Cleaning

    /// <summary>
    /// Cleans document-level noise like artificial paragraph headings.
    /// </summary>
    private static string CleanDocumentNoise(string text)
    {
        // Remove artificial paragraph headings
        text = Regex.Replace(
            text,
            @"^#{1,6}\s*Paragraph\s+\d+\s*$",
            "",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // Clean excessive newlines
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        // Clean excessive horizontal whitespace
        text = Regex.Replace(text, @"[ \t]{2,}", " ");

        return text.Trim();
    }

    /// <summary>
    /// Normalizes whitespace while preserving structure.
    /// </summary>
    private static string NormalizeWhitespace(string text)
    {
        // Replace multiple blank lines with double newline
        text = Regex.Replace(text, @"\n\s*\n\s*\n", "\n\n");

        // Trim trailing whitespace from each line
        text = Regex.Replace(text, @"[ \t]+$", "", RegexOptions.Multiline);

        return text.Trim();
    }

    #endregion

    #region Structure Extraction

    /// <summary>
    /// Extracts structured elements from refined text.
    /// </summary>
    private List<StructuredElement> ExtractStructuredElements(string text)
    {
        var structures = new List<StructuredElement>();

        // Extract code blocks
        structures.AddRange(ExtractCodeBlocks(text));

        // Extract markdown tables
        structures.AddRange(ExtractMarkdownTables(text));

        // Extract lists
        structures.AddRange(ExtractLists(text));

        return structures;
    }

    /// <summary>
    /// Extracts fenced code blocks from text.
    /// </summary>
    private static IEnumerable<StructuredElement> ExtractCodeBlocks(string text)
    {
        var codeBlockPattern = @"```(\w+)?\s*\n([\s\S]*?)```";
        var matches = Regex.Matches(text, codeBlockPattern);

        foreach (Match match in matches)
        {
            var language = match.Groups[1].Value;
            var code = match.Groups[2].Value.Trim();

            var codeData = new CodeBlockData
            {
                Language = string.IsNullOrEmpty(language) ? "text" : language,
                Content = code
            };

            yield return new StructuredElement
            {
                Type = StructureType.Code,
                Caption = $"Code block ({codeData.Language})",
                Data = JsonSerializer.SerializeToElement(codeData),
                Location = new StructureLocation
                {
                    StartChar = match.Index,
                    EndChar = match.Index + match.Length
                }
            };
        }
    }

    /// <summary>
    /// Extracts markdown tables from text.
    /// </summary>
    private static IEnumerable<StructuredElement> ExtractMarkdownTables(string text)
    {
        var tablePattern = @"^\|.+\|\s*\n\|[-:\s|]+\|\s*\n(\|.+\|\s*\n)+";
        var matches = Regex.Matches(text, tablePattern, RegexOptions.Multiline);

        foreach (Match match in matches)
        {
            var tableData = ParseMarkdownTable(match.Value);
            if (tableData.Count > 0)
            {
                yield return new StructuredElement
                {
                    Type = StructureType.Table,
                    Caption = $"Table ({tableData.Count} rows)",
                    Data = JsonSerializer.SerializeToElement(tableData),
                    Location = new StructureLocation
                    {
                        StartChar = match.Index,
                        EndChar = match.Index + match.Length
                    }
                };
            }
        }
    }

    /// <summary>
    /// Parses a markdown table into structured data.
    /// </summary>
    private static List<Dictionary<string, string>> ParseMarkdownTable(string tableText)
    {
        var lines = tableText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 3) return [];

        var headers = lines[0].Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(h => h.Trim())
            .ToArray();

        var rows = new List<Dictionary<string, string>>();
        for (int i = 2; i < lines.Length; i++)
        {
            var cells = lines[i].Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .ToArray();

            var row = new Dictionary<string, string>();
            for (int j = 0; j < Math.Min(headers.Length, cells.Length); j++)
            {
                row[headers[j]] = cells[j];
            }
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Extracts list structures from text.
    /// </summary>
    private static IEnumerable<StructuredElement> ExtractLists(string text)
    {
        // Match ordered and unordered lists (3+ consecutive items)
        var listPattern = @"(?:^[ \t]*(?:[-*+]|\d+\.)[ \t]+.+\n){3,}";
        var matches = Regex.Matches(text, listPattern, RegexOptions.Multiline);

        foreach (Match match in matches)
        {
            var listText = match.Value.Trim();
            var items = listText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => Regex.Replace(line.Trim(), @"^(?:[-*+]|\d+\.)\s*", ""))
                .ToList();

            var isOrdered = Regex.IsMatch(listText, @"^\s*\d+\.");

            yield return new StructuredElement
            {
                Type = StructureType.List,
                Caption = $"{(isOrdered ? "Ordered" : "Unordered")} list ({items.Count} items)",
                Data = JsonSerializer.SerializeToElement(new { Ordered = isOrdered, Items = items }),
                Location = new StructureLocation
                {
                    StartChar = match.Index,
                    EndChar = match.Index + match.Length
                }
            };
        }
    }

    #endregion

    #region Structured Data Conversion

    /// <summary>
    /// Converts structured data (Tables and Blocks) from RawContent to markdown text.
    /// This is the core conversion logic moved from Extract stage to Refine stage.
    /// </summary>
    private string ConvertStructuredToMarkdown(RawContent raw, RefineOptions options)
    {
        var sb = new StringBuilder();

        // Process blocks in order, inserting tables at appropriate positions
        var tableIndex = 0;
        var tables = raw.Tables;

        foreach (var block in raw.Blocks)
        {
            // Insert any tables that should appear before this block
            // (For now, we append tables at the end if no position info)
            var markdown = ConvertBlockToMarkdown(block);
            if (!string.IsNullOrEmpty(markdown))
            {
                sb.AppendLine(markdown);
                sb.AppendLine();
            }
        }

        // Append remaining tables
        if (options.ConvertTablesToMarkdown)
        {
            for (int i = tableIndex; i < tables.Count; i++)
            {
                var tableMarkdown = ConvertTableToMarkdown(tables[i]);
                if (!string.IsNullOrEmpty(tableMarkdown))
                {
                    sb.AppendLine(tableMarkdown);
                    sb.AppendLine();
                }
            }
        }

        // If no blocks but has tables, just convert tables
        if (raw.Blocks.Count == 0 && raw.HasTables && options.ConvertTablesToMarkdown)
        {
            foreach (var table in tables)
            {
                var tableMarkdown = ConvertTableToMarkdown(table);
                if (!string.IsNullOrEmpty(tableMarkdown))
                {
                    sb.AppendLine(tableMarkdown);
                    sb.AppendLine();
                }
            }
        }

        // If still empty, fall back to raw text
        var result = sb.ToString().Trim();
        return string.IsNullOrEmpty(result) ? raw.Text : result;
    }

    /// <summary>
    /// Converts a single TextBlock to markdown format.
    /// </summary>
    private static string ConvertBlockToMarkdown(TextBlock block)
    {
        if (string.IsNullOrWhiteSpace(block.Content))
            return string.Empty;

        return block.Type switch
        {
            BlockType.Heading => ConvertHeadingToMarkdown(block),
            BlockType.ListItem => ConvertListItemToMarkdown(block),
            BlockType.CodeBlock => $"```\n{block.Content}\n```",
            BlockType.Quote => $"> {block.Content}",
            BlockType.Header => $"<!-- Header: {block.Content} -->",
            BlockType.Footer => $"<!-- Footer: {block.Content} -->",
            BlockType.Caption => $"*{block.Content}*",
            BlockType.TocEntry => $"- {block.Content}",
            BlockType.Note => $"> **Note:** {block.Content}",
            BlockType.Paragraph or _ => block.Content
        };
    }

    /// <summary>
    /// Converts a heading block to markdown with appropriate level.
    /// </summary>
    private static string ConvertHeadingToMarkdown(TextBlock block)
    {
        var level = block.HeadingLevel ?? 1;
        level = Math.Clamp(level, 1, 6);
        var prefix = new string('#', level);
        return $"{prefix} {block.Content}";
    }

    /// <summary>
    /// Converts a list item block to markdown format.
    /// </summary>
    private static string ConvertListItemToMarkdown(TextBlock block)
    {
        var indent = block.ListLevel.HasValue ? new string(' ', (block.ListLevel.Value - 1) * 2) : "";
        var marker = block.IsOrderedList == true ? "1." : "-";
        return $"{indent}{marker} {block.Content}";
    }

    /// <summary>
    /// Converts a TableData to markdown table format.
    /// </summary>
    private static string ConvertTableToMarkdown(TableData table)
    {
        if (table.Cells == null || table.Cells.Length == 0)
            return string.Empty;

        var sb = new StringBuilder();
        var columnCount = table.Cells.Max(row => row?.Length ?? 0);

        if (columnCount == 0)
            return string.Empty;

        // Determine headers
        string[] headers;
        int dataStartRow;

        if (table.HasHeader && table.Headers != null && table.Headers.Length > 0)
        {
            headers = table.Headers;
            dataStartRow = 0;
        }
        else if (table.HasHeader && table.Cells.Length > 0)
        {
            headers = table.Cells[0] ?? [];
            dataStartRow = 1;
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
            sb.Append($" {EscapeMarkdownCell(header ?? "")} |");
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
            sb.Append($" {separator} |");
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
                sb.Append($" {EscapeMarkdownCell(cell ?? "")} |");
            }
            sb.AppendLine();
        }

        // Add confidence warning comment if low confidence
        if (table.NeedsLlmAssist)
        {
            sb.AppendLine($"<!-- Table confidence: {table.Confidence:F2} - may need LLM assistance -->");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Escapes special characters in markdown table cells.
    /// </summary>
    private static string EscapeMarkdownCell(string content)
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

    /// <summary>
    /// Converts TableData list to StructuredElement list for extraction.
    /// </summary>
    private static IEnumerable<StructuredElement> ConvertTablesToStructuredElements(List<TableData> tables)
    {
        for (int i = 0; i < tables.Count; i++)
        {
            var table = tables[i];
            if (table.Cells == null || table.Cells.Length == 0)
                continue;

            var rowCount = table.Cells.Length;
            var colCount = table.Cells.Max(row => row?.Length ?? 0);

            // Create structured data from TableData
            var tableStructure = new
            {
                RowCount = rowCount,
                ColumnCount = colCount,
                HasHeader = table.HasHeader,
                Headers = table.Headers,
                Confidence = table.Confidence,
                NeedsLlmAssist = table.NeedsLlmAssist,
                Cells = table.Cells
            };

            yield return new StructuredElement
            {
                Type = StructureType.Table,
                Caption = $"Table {i + 1} ({rowCount}x{colCount}){(table.NeedsLlmAssist ? " [low confidence]" : "")}",
                Data = JsonSerializer.SerializeToElement(tableStructure),
                Location = new StructureLocation
                {
                    StartChar = 0, // Position info not available from TableData
                    EndChar = 0
                }
            };
        }
    }

    #endregion

    #region Section Building

    /// <summary>
    /// Builds hierarchical sections from heading markers in text.
    /// </summary>
    private static List<Section> BuildSections(string text)
    {
        var sections = new List<Section>();
        var headingPattern = @"^(#{1,6})\s+(.+)$";
        var matches = Regex.Matches(text, headingPattern, RegexOptions.Multiline);

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var level = match.Groups[1].Value.Length;
            var title = match.Groups[2].Value.Trim();

            var endPos = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;

            sections.Add(new Section
            {
                Id = $"section_{i}",
                Title = title,
                Level = level,
                Start = match.Index,
                End = endPos,
                Content = text.Substring(match.Index, endPos - match.Index).Trim()
            });
        }

        return sections;
    }

    #endregion

    #region Metadata Building

    /// <summary>
    /// Builds document metadata from raw content.
    /// </summary>
    private static DocumentMetadata BuildMetadata(RawContent raw)
    {
        return new DocumentMetadata
        {
            FileName = raw.File.Name,
            FileType = raw.File.Extension.TrimStart('.').ToUpperInvariant(),
            FileSize = raw.File.Size,
            Title = raw.File.Name,
            CreatedAt = raw.File.CreatedAt,
            ModifiedAt = raw.File.ModifiedAt
        };
    }

    #endregion

    #region Quality Scoring

    /// <summary>
    /// Calculates structure extraction quality score.
    /// </summary>
    private static double CalculateStructureScore(int structureCount, int sectionCount)
    {
        // Base score based on presence of structures
        var hasStructures = structureCount > 0;
        var hasSections = sectionCount > 0;

        if (hasStructures && hasSections) return 0.9;
        if (hasStructures || hasSections) return 0.7;
        return 0.5;
    }

    /// <summary>
    /// Calculates cleanup effectiveness score.
    /// </summary>
    private static double CalculateCleanupScore(int originalLength, int refinedLength)
    {
        if (originalLength == 0) return 1.0;

        var reduction = 1.0 - ((double)refinedLength / originalLength);
        // Ideal reduction is 5-20% (cleaning noise without losing content)
        if (reduction is >= 0.05 and <= 0.20) return 0.9;
        if (reduction is >= 0.0 and < 0.05) return 0.8;
        if (reduction is > 0.20 and <= 0.35) return 0.7;
        return 0.5;
    }

    /// <summary>
    /// Calculates content retention score.
    /// </summary>
    private static double CalculateRetentionScore(int originalLength, int refinedLength)
    {
        if (originalLength == 0) return 1.0;
        return Math.Min(1.0, (double)refinedLength / originalLength);
    }

    #endregion
}
