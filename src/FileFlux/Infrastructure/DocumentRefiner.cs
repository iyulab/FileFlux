using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileFlux.Core;
using FileFlux.Infrastructure.Filters;
using FluxCurator.Core;
using FluxCurator.Core.Infrastructure.Refining;
using Microsoft.Extensions.Logging;
using FluxCuratorTextRefineOptions = FluxCurator.Core.Domain.TextRefineOptions;
using System.Globalization;

namespace FileFlux.Infrastructure;

/// <summary>
/// Default document refiner implementation.
/// Transforms RawContent into RefinedContent by cleaning, normalizing, and extracting structure.
/// </summary>
public sealed partial class DocumentRefiner : IDocumentRefiner
{
    private readonly TextRefiner _textRefiner;
    private readonly IMarkdownConverter? _markdownConverter;
    private readonly IMarkdownNormalizer _markdownNormalizer;
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
        IMarkdownNormalizer? markdownNormalizer = null,
        ILogger<DocumentRefiner>? logger = null)
    {
        _textRefiner = TextRefiner.Instance;
        _markdownConverter = markdownConverter;
        _markdownNormalizer = markdownNormalizer ?? new MarkdownNormalizer();
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
        LogStartingRefinement(_logger, raw.File.Name);

        try
        {
            // Reader spans (pages, time ranges) ride through the steps below as marker lines (SourceSpanMarkers).
            var refinedText = SourceSpanMarkers.Insert(raw.Text, raw.Spans);
            var structures = new List<StructuredElement>();

            // Step 1: Clean noise (headers, footers, page numbers)
            if (options.CleanNoise)
            {
                refinedText = CleanDocumentNoise(refinedText);

                // Apply PDF-specific header/footer filtering if enabled
                if (options.FilterPdfHeaderFooter && IsPdfDocument(raw))
                {
                    var pageCount = CalculatePageCount(raw);
                    var pdfFilter = new PdfHeaderFooterFilter(new PdfHeaderFooterFilter.Options
                    {
                        Enabled = true,
                        RepetitionThreshold = options.PdfHeaderFooterThreshold,
                        MinPageCount = 3
                    });
                    refinedText = pdfFilter.Filter(refinedText, pageCount);
                    LogAppliedPdfFilter(_logger, pageCount);
                }
            }

            // Step 1.5: Convert numbered section markers to Markdown headings
            // This improves structure for technical documents with numbered sections
            if (options.BuildSections)
            {
                refinedText = ConvertNumberedSectionsToHeadings(refinedText);
            }

            // Step 2: Markdown conversion of the text. RawContent.Text already carries every table inline — a reader
            // that fills RawContent.Tables writes the same tables into the text — so the text is converted, never
            // rebuilt from the structured view.
            if ((options.ConvertTablesToMarkdown || options.ConvertBlocksToMarkdown) && _markdownConverter != null)
            {
                // Fallback to IMarkdownConverter for legacy readers. It converts the text as cleaned so far: handing it
                // the raw content rebuilt the document from raw text and threw away every step above.
                var markdownResult = await _markdownConverter.ConvertAsync(raw.WithText(refinedText), new MarkdownConversionOptions
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

            // Step 2.5: Normalize markdown structure (heading hierarchy, lists, whitespace)
            // This is a format-agnostic normalization applied to all markdown outputs
            if (options.NormalizeMarkdownStructure)
            {
                var normalizationResult = _markdownNormalizer.Normalize(refinedText, new NormalizationOptions
                {
                    NormalizeHeadings = true,
                    RemoveEmptyHeadings = true,
                    NormalizeLists = true,
                    NormalizeWhitespace = true,
                    DemoteAnnotationHeadings = true,
                    NormalizeTables = true,
                    MaxHeadingLevelJump = 1,
                    MaxColumnVariance = 0
                });

                if (normalizationResult.HasChanges)
                {
                    refinedText = normalizationResult.Markdown;
                    LogMarkdownNormalized(_logger, normalizationResult.Actions.Count);
                }
            }

            // Step 3: Extract structured elements (tables, code blocks, lists) from text
            if (options.ExtractStructures)
            {
                structures.AddRange(ExtractStructuredElements(refinedText));
            }

            // Step 4: Text-level refinement via FluxCurator TextRefiner (Standard includes token optimization)
            var textRefineOptions = FluxCuratorTextRefineOptions.Standard;
            refinedText = _textRefiner.Refine(refinedText, textRefineOptions);

            // Step 5: Normalize whitespace
            if (options.NormalizeWhitespace)
            {
                refinedText = NormalizeWhitespace(refinedText);
            }

            // Read the span markers back as offsets over the final text, and remove them.
            (refinedText, var spans) = SourceSpanMarkers.Extract(refinedText, raw.Spans);

            // Build sections from text headings
            var sections = options.BuildSections ? BuildSections(refinedText) : [];

            // Build metadata
            var metadata = BuildMetadata(raw);

            var refined = new RefinedContent
            {
                RawId = raw.Id,
                Text = refinedText,
                Sections = sections,
                Spans = spans,
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

            LogRefinementComplete(_logger, raw.Text.Length, refinedText.Length, structures.Count, sections.Count, sw.Elapsed.TotalSeconds);

            return refined;
        }
        catch (Exception ex)
        {
            LogRefinementFailed(_logger, ex, raw.File.Name);
            throw new DocumentProcessingException($"stream://{raw.File.Name}", $"Refinement failed: {ex.Message}", ex);
        }
    }

    #region PDF Header/Footer Filtering

    /// <summary>
    /// Determines if the document is a PDF based on file extension.
    /// </summary>
    private static bool IsPdfDocument(RawContent raw)
    {
        var extension = raw.File.Extension.TrimStart('.').ToUpperInvariant();
        return extension == "PDF";
    }

    /// <summary>
    /// Calculates the page count from TextBlocks or estimates from text content.
    /// </summary>
    private static int CalculatePageCount(RawContent raw)
    {
        // First, try to get page count from blocks
        if (raw.Blocks.Count > 0)
        {
            var maxPageNumber = raw.Blocks.Max(b => b.PageNumber);
            if (maxPageNumber > 0)
                return maxPageNumber;
        }

        // Fallback: estimate from text length (average 3000 chars per page)
        var estimatedPages = Math.Max(1, raw.Text.Length / 3000);
        return estimatedPages;
    }

    #endregion

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

    /// <summary>
    /// Longest numbered line still read as a section title. A title is a label; a numbered reference, footnote or
    /// inline point is a sentence or a paragraph (a 33-page encyclopedia export: median 357 characters).
    /// </summary>
    internal const int MaxNumberedTitleLength = 80;

    private static readonly (Regex Pattern, int Level, Func<Match, string> Marker)[] NumberedMarkers =
    [
        // Third level "3-1-1." -> H4, sub level "3-1." -> H3, top level "1." -> H2
        (new Regex(@"^(\d+-\d+-\d+)\.\s*(.+)$"), 4, m => m.Groups[1].Value + "."),
        (new Regex(@"^(\d+-\d+)\.\s+(.+)$"), 3, m => m.Groups[1].Value + "."),
        (new Regex(@"^(\d+)\.\s+(.+)$"), 2, m => m.Groups[1].Value + "."),
        // Korean-style circled numbers "①" and parenthesized "(1)" -> H3
        (new Regex(@"^([①②③④⑤⑥⑦⑧⑨⑩])\s+(.+)$"), 3, m => m.Groups[1].Value),
        (new Regex(@"^\((\d+)\)\s+(.+)$"), 3, m => "(" + m.Groups[1].Value + ")"),
    ];

    /// <summary>
    /// Converts numbered section markers ("1.", "3-1.", "3-1-1.", "①", "(1)") to Markdown headings — for text whose
    /// sections are numbered lines rather than marked headings. A numbered line is a section title only when it looks
    /// like one: short, not ending like a sentence, and followed by body text. A run of numbered lines (another numbered
    /// line of the same kind as the nearest non-blank neighbour) is a list — references, footnotes, a table of contents,
    /// steps — and stays a list; so does a one-item list with no body after it (the next thing is a heading, or nothing),
    /// a long numbered sentence inside a paragraph, and anything inside a code fence.
    /// </summary>
    internal static string ConvertNumberedSectionsToHeadings(string text)
    {
        var lines = text.Split('\n');
        var matches = new (int Level, string Marker, string Title)?[lines.Length];
        var inFence = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (!inFence)
            {
                matches[i] = MatchNumberedMarker(line);
            }
        }

        for (var i = 0; i < lines.Length; i++)
        {
            if (matches[i] is not { } numbered || !IsTitleShaped(numbered.Title) || IsInNumberedRun(lines, matches, i)
                || !IsFollowedByBody(lines, i))
            {
                continue;
            }

            var cr = lines[i].EndsWith('\r') ? "\r" : string.Empty;
            lines[i] = $"{new string('#', numbered.Level)} {numbered.Marker} {numbered.Title}{cr}";
        }

        return string.Join('\n', lines);
    }

    private static (int Level, string Marker, string Title)? MatchNumberedMarker(string line)
    {
        foreach (var (pattern, level, marker) in NumberedMarkers)
        {
            var match = pattern.Match(line);
            if (match.Success)
            {
                var title = match.Groups[2].Value.Trim();
                return title.Length == 0 ? null : (level, marker(match), title);
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the text after a section number reads as a title: short, not ending like a sentence, and — in a script
    /// with letter case — not starting with a lowercase letter. A figure legend's numbered labels ("1. outer membrane")
    /// and a paragraph that happens to begin with a number ("2 H2O + 2 NADP+ → …") are not titles. The one rule both
    /// numbered-heading paths use (this refiner and <see cref="Conversion.MarkdownConverter"/>).
    /// </summary>
    internal static bool IsTitleShaped(string title)
        => title.Length is > 0 and <= MaxNumberedTitleLength
           && title[^1] is not ('.' or '。' or ',' or ';')
           && !char.IsLower(title.FirstOrDefault(char.IsLetter));

    private static bool IsFollowedByBody(string[] lines, int index)
    {
        for (var j = index + 1; j < lines.Length; j++)
        {
            if (!string.IsNullOrWhiteSpace(lines[j]))
            {
                return !lines[j].TrimStart().StartsWith('#');
            }
        }

        return false;
    }

    private static bool IsInNumberedRun(string[] lines, (int Level, string Marker, string Title)?[] matches, int index)
    {
        var level = matches[index]!.Value.Level;
        return NeighbourLevel(-1) == level || NeighbourLevel(+1) == level;

        int? NeighbourLevel(int step)
        {
            for (var j = index + step; j >= 0 && j < lines.Length; j += step)
            {
                if (!string.IsNullOrWhiteSpace(lines[j]))
                {
                    return matches[j]?.Level;
                }
            }

            return null;
        }
    }

    #endregion

    #region Structure Extraction

    /// <summary>
    /// Extracts structured elements from refined text.
    /// </summary>
    private static List<StructuredElement> ExtractStructuredElements(string text)
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
                Data = StructureJsonContext.ToElement(codeData),
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
                    Data = StructureJsonContext.ToElement(tableData),
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
                Data = StructureJsonContext.ToElement(new ListBlockData(isOrdered, items)),
                Location = new StructureLocation
                {
                    StartChar = match.Index,
                    EndChar = match.Index + match.Length
                }
            };
        }
    }

    #endregion

    #region Section Building

    /// <summary>
    /// Builds hierarchical sections from heading markers in text.
    /// Delegates to <see cref="SectionPathCalculator.BuildSections"/> (single source of truth).
    /// </summary>
    private static List<Section> BuildSections(string text) =>
        SectionPathCalculator.BuildSections(text);

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

    #region LoggerMessage

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting refinement for {FileName}")]
    private static partial void LogStartingRefinement(ILogger logger, string fileName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Applied PDF header/footer filter: {PageCount} pages")]
    private static partial void LogAppliedPdfFilter(ILogger logger, int pageCount);


    [LoggerMessage(Level = LogLevel.Debug, Message = "Markdown normalized: {ActionCount} corrections applied")]
    private static partial void LogMarkdownNormalized(ILogger logger, int actionCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refined {OriginalChars} -> {RefinedChars} chars, {StructureCount} structures, {SectionCount} sections in {Duration:F2}s")]
    private static partial void LogRefinementComplete(ILogger logger, int originalChars, int refinedChars, int structureCount, int sectionCount, double duration);

    [LoggerMessage(Level = LogLevel.Error, Message = "Refinement failed for {FileName}")]
    private static partial void LogRefinementFailed(ILogger logger, Exception ex, string fileName);

    #endregion
}
