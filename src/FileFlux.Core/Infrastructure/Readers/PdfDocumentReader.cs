using FileFlux.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using System.Text;
using System.Text.RegularExpressions;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// PDF document reader optimized for RAG preprocessing.
/// Uses PdfPig library for text extraction with:
/// - Table detection and markdown conversion
/// - Page boundary sentence merging
/// - Layout-aware text ordering
/// </summary>
public partial class PdfDocumentReader : IDocumentReader
{
    public string ReaderType => "PdfReader";

    public IEnumerable<string> SupportedExtensions => [".pdf"];
    private static readonly char[] separator = [' ', '\t', '\n', '\r'];

    // Patterns for detecting incomplete sentences at page boundaries
    private static readonly Regex IncompleteEndPattern = IncompleteSentenceEndRegex();
    private static readonly Regex IncompleteStartPattern = IncompleteSentenceStartRegex();

    // Patterns for detecting page numbers (to be filtered out)
    private static readonly Regex PageNumberPatterns = PageNumberRegex();

    public bool CanRead(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension == ".pdf";
    }

    public async Task<RawContent> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PDF file not found: {filePath}");

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        return await Task.Run(() => ExtractPdfContent(filePath, cancellationToken), cancellationToken);
    }

    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!CanRead(fileName))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        return await Task.Run(() => ExtractPdfContentFromStream(stream, fileName, cancellationToken), cancellationToken);
    }

    private RawContent ExtractPdfContent(string filePath, CancellationToken cancellationToken)
    {
        var extractionWarnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var pageTexts = new List<PageContent>();

        var fileInfo = new FileInfo(filePath);

        try
        {
            using var document = PdfDocument.Open(filePath);

            // Collect PDF metadata
            var info = document.Information;
            if (info != null)
            {
                structuralHints["Title"] = info.Title ?? "";
                structuralHints["Author"] = info.Author ?? "";
                structuralHints["Subject"] = info.Subject ?? "";
                structuralHints["Creator"] = info.Creator ?? "";
                structuralHints["Producer"] = info.Producer ?? "";
                if (info.CreationDate != null)
                    structuralHints["CreationDate"] = info.CreationDate;
                if (info.ModifiedDate != null)
                    structuralHints["ModifiedDate"] = info.ModifiedDate;
            }

            structuralHints["PageCount"] = document.NumberOfPages;
            structuralHints["Version"] = document.Version.ToString();

            var totalPages = document.NumberOfPages;
            var processedPages = 0;
            var tablesDetected = 0;
            var lowConfidenceTables = 0;
            var minTableConfidence = 1.0;

            // Extract text from each page
            for (int pageNum = 1; pageNum <= totalPages; pageNum++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var page = document.GetPage(pageNum);
                    var pageContent = ExtractPageContent(page, pageNum, extractionWarnings);

                    if (pageContent.HasContent)
                    {
                        pageTexts.Add(pageContent);
                        tablesDetected += pageContent.TableCount;
                        lowConfidenceTables += pageContent.LowConfidenceTableCount;
                        if (pageContent.TableCount > 0 && pageContent.MinTableConfidence < minTableConfidence)
                        {
                            minTableConfidence = pageContent.MinTableConfidence;
                        }
                    }

                    processedPages++;
                }
                catch (Exception ex)
                {
                    extractionWarnings.Add($"Page {pageNum} processing error: {ex.Message}");
                }
            }

            // Merge page texts with sentence boundary handling
            var extractedText = MergePageTexts(pageTexts);

            // Build page ranges after merging
            var pageRanges = BuildPageRanges(pageTexts, extractedText);

            // Extraction statistics
            structuralHints["ProcessedPages"] = processedPages;
            structuralHints["TotalCharacters"] = extractedText.Length;
            structuralHints["WordCount"] = CountWords(extractedText);
            structuralHints["LineCount"] = extractedText.Split('\n').Length;
            structuralHints["PageRanges"] = pageRanges;
            structuralHints["TablesDetected"] = tablesDetected;
            structuralHints["LowConfidenceTables"] = lowConfidenceTables;
            structuralHints["MinTableConfidence"] = tablesDetected > 0 ? minTableConfidence : 1.0;

            if (processedPages < totalPages)
            {
                extractionWarnings.Add($"Partial page processing: {processedPages}/{totalPages} pages processed");
            }

            return new RawContent
            {
                Text = extractedText,
                File = new SourceFileInfo
                {
                    Name = Path.GetFileName(filePath),
                    Extension = ".pdf",
                    Size = fileInfo.Length,
                },
                Hints = structuralHints,
                Warnings = extractionWarnings,
                ReaderType = ReaderType
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"PDF file processing error: {ex.Message}", ex);
        }
    }

    private RawContent ExtractPdfContentFromStream(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        var extractionWarnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var pageTexts = new List<PageContent>();

        var streamLength = stream.CanSeek ? stream.Length : -1;

        try
        {
            using var document = PdfDocument.Open(stream);

            // Collect PDF metadata
            var info = document.Information;
            if (info != null)
            {
                structuralHints["Title"] = info.Title ?? "";
                structuralHints["Author"] = info.Author ?? "";
                structuralHints["Subject"] = info.Subject ?? "";
                structuralHints["Creator"] = info.Creator ?? "";
                structuralHints["Producer"] = info.Producer ?? "";
                if (info.CreationDate != null)
                    structuralHints["CreationDate"] = info.CreationDate;
                if (info.ModifiedDate != null)
                    structuralHints["ModifiedDate"] = info.ModifiedDate;
            }

            structuralHints["PageCount"] = document.NumberOfPages;
            structuralHints["Version"] = document.Version.ToString();

            var totalPages = document.NumberOfPages;
            var processedPages = 0;
            var tablesDetected = 0;
            var lowConfidenceTables = 0;
            var minTableConfidence = 1.0;

            // Extract text from each page
            for (int pageNum = 1; pageNum <= totalPages; pageNum++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var page = document.GetPage(pageNum);
                    var pageContent = ExtractPageContent(page, pageNum, extractionWarnings);

                    if (pageContent.HasContent)
                    {
                        pageTexts.Add(pageContent);
                        tablesDetected += pageContent.TableCount;
                        lowConfidenceTables += pageContent.LowConfidenceTableCount;
                        if (pageContent.TableCount > 0 && pageContent.MinTableConfidence < minTableConfidence)
                        {
                            minTableConfidence = pageContent.MinTableConfidence;
                        }
                    }

                    processedPages++;
                }
                catch (Exception ex)
                {
                    extractionWarnings.Add($"Page {pageNum} processing error: {ex.Message}");
                }
            }

            // Merge page texts with sentence boundary handling
            var extractedText = MergePageTexts(pageTexts);

            // Extraction statistics
            structuralHints["ProcessedPages"] = processedPages;
            structuralHints["TotalCharacters"] = extractedText.Length;
            structuralHints["WordCount"] = CountWords(extractedText);
            structuralHints["LineCount"] = extractedText.Split('\n').Length;
            structuralHints["TablesDetected"] = tablesDetected;
            structuralHints["LowConfidenceTables"] = lowConfidenceTables;
            structuralHints["MinTableConfidence"] = tablesDetected > 0 ? minTableConfidence : 1.0;

            if (processedPages < totalPages)
            {
                extractionWarnings.Add($"Partial page processing: {processedPages}/{totalPages} pages processed");
            }

            return new RawContent
            {
                Text = extractedText,
                File = new SourceFileInfo
                {
                    Name = fileName,
                    Extension = ".pdf",
                    Size = streamLength,
                },
                Hints = structuralHints,
                Warnings = extractionWarnings,
                ReaderType = ReaderType
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"PDF stream processing error: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Extract content from a single page with table detection and layout analysis.
    /// </summary>
    private PageContent ExtractPageContent(Page page, int pageNum, List<string> warnings)
    {
        var content = new PageContent { PageNumber = pageNum };

        try
        {
            var words = page.GetWords().ToList();
            if (words.Count == 0)
            {
                warnings.Add($"Page {pageNum}: No text content");
                return content;
            }

            // Detect tables using layout analysis
            var tables = DetectTables(page, words, warnings);
            content.TableCount = tables.Count;

            // Track table confidence metrics
            if (tables.Count > 0)
            {
                content.LowConfidenceTableCount = tables.Count(t => t.ConfidenceScore < TableConfidenceThreshold);
                content.MinTableConfidence = tables.Min(t => t.ConfidenceScore);

                // Add warning for low confidence tables
                if (content.LowConfidenceTableCount > 0)
                {
                    warnings.Add($"Page {pageNum}: {content.LowConfidenceTableCount} table(s) with low confidence, using plain text fallback");
                }
            }

            // Get text blocks excluding table areas
            var textBlocks = ExtractTextBlocks(page, words, tables);

            // Build page text with tables converted to markdown
            var textBuilder = new StringBuilder();

            foreach (var block in textBlocks.OrderBy(b => b.TopY).ThenBy(b => b.LeftX))
            {
                if (block.IsTable)
                {
                    // Convert table to markdown format
                    textBuilder.AppendLine();
                    textBuilder.AppendLine(block.Content);
                    textBuilder.AppendLine();
                }
                else
                {
                    textBuilder.AppendLine(block.Content);
                }
            }

            var rawText = textBuilder.ToString().Trim();

            // Filter out page numbers from the extracted text
            content.Text = FilterPageNumbers(rawText);
            content.EndsWithIncompleteSentence = IsIncompleteSentenceEnd(content.Text);
            content.StartsWithIncompleteSentence = IsIncompleteSentenceStart(content.Text);
        }
        catch (Exception ex)
        {
            warnings.Add($"Page {pageNum} text extraction error: {ex.Message}");
        }

        return content;
    }

    /// <summary>
    /// Detect table regions in the page based on word alignment patterns.
    /// </summary>
    private static List<TableRegion> DetectTables(Page page, List<Word> words, List<string> warnings)
    {
        var tables = new List<TableRegion>();

        try
        {
            // Group words by approximate Y position (rows)
            var lineHeight = EstimateLineHeight(words);
            var rows = GroupWordsIntoRows(words, lineHeight);

            if (rows.Count < 2) return tables;

            // Detect potential table regions by analyzing column alignment
            var columnPositions = DetectColumnPositions(rows);

            if (columnPositions.Count >= 2)
            {
                // Find consecutive rows that align to columns (table candidates)
                var tableRows = new List<List<Word>>();
                var inTable = false;

                foreach (var row in rows)
                {
                    var alignsToColumns = RowAlignsToColumns(row, columnPositions, lineHeight);

                    if (alignsToColumns)
                    {
                        if (!inTable)
                        {
                            tableRows = [];
                            inTable = true;
                        }
                        tableRows.Add(row);
                    }
                    else if (inTable)
                    {
                        // End of table region
                        if (tableRows.Count >= 2)
                        {
                            var table = CreateTableFromRows(tableRows, columnPositions, page.Height);
                            if (table != null)
                            {
                                tables.Add(table);
                            }
                        }
                        inTable = false;
                    }
                }

                // Handle table at end of page
                if (inTable && tableRows.Count >= 2)
                {
                    var table = CreateTableFromRows(tableRows, columnPositions, page.Height);
                    if (table != null)
                    {
                        tables.Add(table);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Table detection error: {ex.Message}");
        }

        return tables;
    }

    /// <summary>
    /// Group words into rows based on Y position.
    /// </summary>
    private static List<List<Word>> GroupWordsIntoRows(List<Word> words, double lineHeight)
    {
        var rows = new List<List<Word>>();
        var sortedWords = words.OrderBy(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left).ToList();

        List<Word>? currentRow = null;
        double currentY = double.MinValue;

        foreach (var word in sortedWords)
        {
            var wordY = word.BoundingBox.Bottom;

            if (currentRow == null || Math.Abs(wordY - currentY) > lineHeight * 0.5)
            {
                currentRow = [];
                rows.Add(currentRow);
                currentY = wordY;
            }

            currentRow.Add(word);
        }

        // Sort words in each row by X position
        foreach (var row in rows)
        {
            row.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
        }

        return rows;
    }

    /// <summary>
    /// Maximum reasonable column count for table detection.
    /// Tables with more columns are likely false positives.
    /// </summary>
    private const int MaxReasonableColumnCount = 10;

    /// <summary>
    /// Minimum gap ratio to consider as column separator.
    /// Gap must be at least this multiple of average word spacing.
    /// </summary>
    private const double MinColumnGapRatio = 2.0;

    /// <summary>
    /// Detect column positions by analyzing word alignment across rows.
    /// Combines position-based and gap-based detection for better accuracy.
    /// </summary>
    private static List<double> DetectColumnPositions(List<List<Word>> rows)
    {
        if (rows.Count == 0) return [];

        var allWords = rows.SelectMany(r => r).ToList();
        if (allWords.Count == 0) return [];

        // Try gap-based detection first (more reliable for well-formatted tables)
        var gapBasedColumns = DetectColumnsByGaps(rows);

        // Try position-based detection as fallback
        var positionBasedColumns = DetectColumnsByPositions(rows, allWords);

        // Choose the better detection method based on quality metrics
        if (gapBasedColumns.Count >= 2 && gapBasedColumns.Count <= MaxReasonableColumnCount)
        {
            // Validate gap-based columns: check if rows align reasonably
            var gapAlignmentScore = CalculateColumnAlignmentScore(rows, gapBasedColumns);
            var posAlignmentScore = positionBasedColumns.Count >= 2
                ? CalculateColumnAlignmentScore(rows, positionBasedColumns)
                : 0;

            // Use gap-based if it has better or equal alignment
            if (gapAlignmentScore >= posAlignmentScore)
            {
                return gapBasedColumns;
            }
        }

        return positionBasedColumns;
    }

    /// <summary>
    /// Detect columns by analyzing consistent gaps between words across rows.
    /// </summary>
    private static List<double> DetectColumnsByGaps(List<List<Word>> rows)
    {
        if (rows.Count < 2) return [];

        // Calculate average within-word spacing for each row
        var rowGaps = new List<List<(double GapStart, double GapEnd, double Width)>>();

        foreach (var row in rows)
        {
            if (row.Count < 2) continue;

            var sortedWords = row.OrderBy(w => w.BoundingBox.Left).ToList();
            var gaps = new List<(double GapStart, double GapEnd, double Width)>();

            // Calculate average word spacing within this row
            var wordSpacings = new List<double>();
            for (int i = 0; i < sortedWords.Count - 1; i++)
            {
                var gap = sortedWords[i + 1].BoundingBox.Left - sortedWords[i].BoundingBox.Right;
                if (gap > 0)
                    wordSpacings.Add(gap);
            }

            if (wordSpacings.Count == 0) continue;

            var avgSpacing = wordSpacings.Average();
            var significantGapThreshold = avgSpacing * MinColumnGapRatio;

            // Find significant gaps (larger than average word spacing)
            for (int i = 0; i < sortedWords.Count - 1; i++)
            {
                var gapStart = sortedWords[i].BoundingBox.Right;
                var gapEnd = sortedWords[i + 1].BoundingBox.Left;
                var gapWidth = gapEnd - gapStart;

                if (gapWidth >= significantGapThreshold)
                {
                    gaps.Add((gapStart, gapEnd, gapWidth));
                }
            }

            if (gaps.Count > 0)
                rowGaps.Add(gaps);
        }

        if (rowGaps.Count < 2) return [];

        // Find consistent gap positions across rows
        var consistentGaps = FindConsistentGaps(rowGaps, rows.Count);

        if (consistentGaps.Count == 0) return [];

        // Convert gaps to column start positions
        var columnPositions = new List<double>();

        // First column starts at leftmost word position
        var leftmostX = rows.SelectMany(r => r).Min(w => w.BoundingBox.Left);
        columnPositions.Add(leftmostX);

        // Add column starts after each gap
        foreach (var gap in consistentGaps.OrderBy(g => g))
        {
            columnPositions.Add(gap);
        }

        return columnPositions;
    }

    /// <summary>
    /// Find gap positions that are consistent across multiple rows.
    /// </summary>
    private static List<double> FindConsistentGaps(
        List<List<(double GapStart, double GapEnd, double Width)>> rowGaps,
        int totalRows)
    {
        if (rowGaps.Count == 0) return [];

        // Calculate tolerance based on average gap width
        var allGaps = rowGaps.SelectMany(g => g).ToList();
        var avgGapWidth = allGaps.Average(g => g.Width);
        var tolerance = Math.Max(5, avgGapWidth * 0.5);

        // Group gaps by approximate position (using gap center)
        var gapClusters = new Dictionary<int, List<double>>();
        var bucketSize = Math.Max(10, (int)tolerance);

        foreach (var rowGap in rowGaps)
        {
            foreach (var gap in rowGap)
            {
                var gapCenter = (gap.GapStart + gap.GapEnd) / 2;
                var bucket = (int)(gapCenter / bucketSize) * bucketSize;

                if (!gapClusters.ContainsKey(bucket))
                    gapClusters[bucket] = [];

                gapClusters[bucket].Add(gap.GapEnd); // Use gap end as column start
            }
        }

        // Find clusters that appear in at least 50% of rows with gaps
        var threshold = Math.Max(2, rowGaps.Count / 2);
        var consistentGaps = gapClusters
            .Where(kvp => kvp.Value.Count >= threshold)
            .Select(kvp => kvp.Value.Average()) // Use average position for cluster
            .OrderBy(x => x)
            .ToList();

        return consistentGaps;
    }

    /// <summary>
    /// Detect column positions using position-based bucketing (original method).
    /// </summary>
    private static List<double> DetectColumnsByPositions(List<List<Word>> rows, List<Word> allWords)
    {
        var avgCharWidth = allWords
            .Where(w => !string.IsNullOrEmpty(w.Text))
            .Select(w => w.BoundingBox.Width / Math.Max(1, w.Text.Length))
            .DefaultIfEmpty(5.0)
            .Average();

        // Bucket size is approximately 1-2 character widths
        var bucketSize = Math.Max(3, (int)(avgCharWidth * 1.5));

        var xPositions = new Dictionary<int, int>(); // X position (bucketed) -> occurrence count

        foreach (var row in rows)
        {
            foreach (var word in row)
            {
                var bucket = (int)(word.BoundingBox.Left / bucketSize) * bucketSize;
                xPositions.TryAdd(bucket, 0);
                xPositions[bucket]++;
            }
        }

        // Find X positions that appear in multiple rows (likely column starts)
        // Use adaptive threshold: at least 40% of rows should have this alignment
        var threshold = Math.Max(2, (int)(rows.Count * 0.4));
        var rawPositions = xPositions
            .Where(kvp => kvp.Value >= threshold)
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => (double)kvp.Key)
            .ToList();

        // Merge columns that are too close together (within 2x bucket size)
        var mergedPositions = MergeCloseColumns(rawPositions, bucketSize * 2);

        // Limit to reasonable column count
        if (mergedPositions.Count > MaxReasonableColumnCount)
        {
            mergedPositions = mergedPositions.Take(MaxReasonableColumnCount).ToList();
        }

        return mergedPositions;
    }

    /// <summary>
    /// Calculate alignment score for column positions against row data.
    /// Higher score indicates better column detection quality.
    /// </summary>
    private static double CalculateColumnAlignmentScore(List<List<Word>> rows, List<double> columnPositions)
    {
        if (rows.Count == 0 || columnPositions.Count < 2) return 0;

        var totalWords = 0;
        var alignedWords = 0;
        var avgColumnWidth = columnPositions.Count > 1
            ? columnPositions.Zip(columnPositions.Skip(1), (a, b) => b - a).Average()
            : 50.0;
        var tolerance = Math.Max(10, avgColumnWidth * 0.2);

        foreach (var row in rows)
        {
            foreach (var word in row)
            {
                totalWords++;
                // Check if word aligns to any column start position
                if (columnPositions.Any(cp => Math.Abs(word.BoundingBox.Left - cp) <= tolerance))
                {
                    alignedWords++;
                }
            }
        }

        return totalWords > 0 ? (double)alignedWords / totalWords : 0;
    }

    /// <summary>
    /// Merge column positions that are too close together.
    /// </summary>
    private static List<double> MergeCloseColumns(List<double> positions, double minDistance)
    {
        if (positions.Count <= 1) return positions;

        var merged = new List<double> { positions[0] };

        for (int i = 1; i < positions.Count; i++)
        {
            var lastMerged = merged[^1];
            if (positions[i] - lastMerged >= minDistance)
            {
                merged.Add(positions[i]);
            }
            // If too close, keep the existing column position (skip this one)
        }

        return merged;
    }

    /// <summary>
    /// Check if a row aligns to detected column positions.
    /// </summary>
    private static bool RowAlignsToColumns(List<Word> row, List<double> columnPositions, double tolerance)
    {
        if (row.Count < 2 || columnPositions.Count < 2) return false;

        var alignedCount = 0;
        foreach (var word in row)
        {
            var x = word.BoundingBox.Left;
            if (columnPositions.Any(cp => Math.Abs(x - cp) < tolerance * 2))
            {
                alignedCount++;
            }
        }

        // At least half of words should align to column positions
        return alignedCount >= row.Count / 2;
    }

    /// <summary>
    /// Create a TableRegion from detected table rows with confidence scoring.
    /// </summary>
    private static TableRegion? CreateTableFromRows(List<List<Word>> tableRows, List<double> columnPositions, double pageHeight)
    {
        if (tableRows.Count < 2) return null;

        // Calculate bounding box
        var allWords = tableRows.SelectMany(r => r).ToList();
        var minX = allWords.Min(w => w.BoundingBox.Left);
        var maxX = allWords.Max(w => w.BoundingBox.Right);
        var minY = allWords.Min(w => w.BoundingBox.Bottom);
        var maxY = allWords.Max(w => w.BoundingBox.Top);

        // Build markdown table and collect cells for analysis
        var markdown = new StringBuilder();
        var plainText = new StringBuilder();
        var isFirstRow = true;
        var allCells = new List<List<string>>();
        var totalCellCount = 0;
        var emptyCellCount = 0;

        foreach (var row in tableRows)
        {
            var cells = AssignWordsToColumns(row, columnPositions);
            allCells.Add(cells);

            // Build markdown row
            markdown.Append('|');
            foreach (var cell in cells)
            {
                var trimmedCell = cell.Trim();
                markdown.Append(' ').Append(trimmedCell).Append(" |");
                totalCellCount++;
                if (string.IsNullOrWhiteSpace(trimmedCell))
                    emptyCellCount++;
            }
            markdown.AppendLine();

            // Build plain text fallback (space-separated cells per row)
            var rowText = string.Join("  ", cells.Select(c => c.Trim()).Where(c => !string.IsNullOrWhiteSpace(c)));
            if (!string.IsNullOrWhiteSpace(rowText))
                plainText.AppendLine(rowText);

            // Add header separator after first row
            if (isFirstRow)
            {
                markdown.Append('|');
                for (int i = 0; i < cells.Count; i++)
                {
                    markdown.Append(" --- |");
                }
                markdown.AppendLine();
                isFirstRow = false;
            }
        }

        // Calculate confidence score
        var confidenceScore = CalculateTableConfidence(allCells, columnPositions.Count, emptyCellCount, totalCellCount);

        return new TableRegion
        {
            MinX = minX,
            MaxX = maxX,
            MinY = minY,
            MaxY = maxY,
            TopY = pageHeight - maxY,
            Markdown = markdown.ToString().Trim(),
            PlainTextFallback = plainText.ToString().Trim(),
            ConfidenceScore = confidenceScore,
            RowCount = tableRows.Count,
            ColumnCount = columnPositions.Count
        };
    }

    /// <summary>
    /// Calculate confidence score for table detection.
    /// </summary>
    /// <returns>Score between 0.0 (low confidence) and 1.0 (high confidence)</returns>
    private static double CalculateTableConfidence(List<List<string>> allCells, int expectedColumns, int emptyCellCount, int totalCellCount)
    {
        if (allCells.Count == 0 || expectedColumns == 0) return 0.0;

        // Factor 1: Column count consistency (40% weight)
        // All rows should have the same number of columns
        var columnCounts = allCells.Select(r => r.Count).ToList();
        var modeColumnCount = columnCounts.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
        var consistentRowCount = columnCounts.Count(c => c == modeColumnCount);
        var columnConsistency = (double)consistentRowCount / allCells.Count;

        // Factor 2: Empty cell ratio (30% weight)
        // Too many empty cells indicates poor table detection
        var emptyCellRatio = totalCellCount > 0 ? (double)emptyCellCount / totalCellCount : 1.0;
        var contentScore = 1.0 - emptyCellRatio;

        // Factor 3: Reasonable column count (30% weight)
        // Tables with too many columns (>10) are often false positives
        var columnCountScore = expectedColumns switch
        {
            <= 2 => 0.7,  // Too few columns might not be a real table
            <= 6 => 1.0,  // Ideal range
            <= 10 => 0.8, // Acceptable
            <= 15 => 0.5, // Suspicious
            _ => 0.2      // Likely false positive
        };

        // Weighted average
        var score = (columnConsistency * 0.4) + (contentScore * 0.3) + (columnCountScore * 0.3);

        return Math.Round(score, 2);
    }

    /// <summary>
    /// Assign words in a row to their respective columns with adaptive tolerance.
    /// </summary>
    private static List<string> AssignWordsToColumns(List<Word> row, List<double> columnPositions)
    {
        var cells = new List<string>();
        var sortedColumns = columnPositions.OrderBy(x => x).ToList();

        if (sortedColumns.Count == 0) return cells;

        // Calculate adaptive tolerance based on average column width
        var avgColumnWidth = sortedColumns.Count > 1
            ? sortedColumns.Zip(sortedColumns.Skip(1), (a, b) => b - a).Average()
            : 50.0; // Default if only one column
        var tolerance = Math.Max(5, avgColumnWidth * 0.15); // 15% of column width, minimum 5

        // Track which words have been assigned to avoid double-counting
        var assignedWords = new HashSet<Word>();

        for (int i = 0; i < sortedColumns.Count; i++)
        {
            var colStart = sortedColumns[i];
            var colEnd = i < sortedColumns.Count - 1 ? sortedColumns[i + 1] : double.MaxValue;

            var cellWords = row
                .Where(w => !assignedWords.Contains(w) &&
                            w.BoundingBox.Left >= colStart - tolerance &&
                            w.BoundingBox.Left < colEnd - tolerance)
                .OrderBy(w => w.BoundingBox.Left)
                .ToList();

            foreach (var word in cellWords)
            {
                assignedWords.Add(word);
            }

            cells.Add(string.Join(" ", cellWords.Select(w => w.Text)));
        }

        // Ensure we have exactly the expected number of columns
        while (cells.Count < sortedColumns.Count)
        {
            cells.Add(""); // Pad with empty cells if needed
        }

        return cells;
    }

    /// <summary>
    /// Confidence threshold for table detection. Below this, plain text fallback is used.
    /// </summary>
    private const double TableConfidenceThreshold = 0.5;

    /// <summary>
    /// Extract text blocks from page, separating tables from regular text.
    /// Uses confidence-based fallback for low-quality table detection.
    /// </summary>
    private static List<TextBlock> ExtractTextBlocks(Page page, List<Word> words, List<TableRegion> tables)
    {
        var blocks = new List<TextBlock>();
        var lineHeight = EstimateLineHeight(words);

        // Add table blocks with confidence-based content selection
        foreach (var table in tables)
        {
            // Use plain text fallback for low-confidence tables
            var useMarkdown = table.ConfidenceScore >= TableConfidenceThreshold;
            blocks.Add(new TextBlock
            {
                Content = useMarkdown ? table.Markdown : table.PlainTextFallback,
                IsTable = useMarkdown,
                TopY = table.TopY,
                LeftX = table.MinX
            });
        }

        // Filter out words that are in table regions
        var nonTableWords = words.Where(w => !IsWordInTable(w, tables)).ToList();

        if (nonTableWords.Count > 0)
        {
            // Group remaining words into text blocks
            var sortedWords = nonTableWords
                .OrderBy(w => page.Height - w.BoundingBox.Bottom)
                .ThenBy(w => w.BoundingBox.Left)
                .ToList();

            var textBuilder = new StringBuilder();
            double currentLineY = double.MinValue;
            double blockTopY = 0;
            double blockLeftX = 0;
            var isFirstWord = true;

            foreach (var word in sortedWords)
            {
                var wordY = Math.Round(page.Height - word.BoundingBox.Bottom, 1);

                if (Math.Abs(wordY - currentLineY) > lineHeight * 0.5)
                {
                    if (textBuilder.Length > 0)
                    {
                        textBuilder.AppendLine();
                    }
                    currentLineY = wordY;

                    if (isFirstWord)
                    {
                        blockTopY = wordY;
                        blockLeftX = word.BoundingBox.Left;
                        isFirstWord = false;
                    }
                }
                else
                {
                    if (textBuilder.Length > 0 && !textBuilder.ToString().EndsWith('\n'))
                    {
                        textBuilder.Append(' ');
                    }
                }

                textBuilder.Append(word.Text);
            }

            if (textBuilder.Length > 0)
            {
                blocks.Add(new TextBlock
                {
                    Content = textBuilder.ToString().Trim(),
                    IsTable = false,
                    TopY = blockTopY,
                    LeftX = blockLeftX
                });
            }
        }

        return blocks;
    }

    /// <summary>
    /// Check if a word is within any detected table region.
    /// </summary>
    private static bool IsWordInTable(Word word, List<TableRegion> tables)
    {
        return tables.Any(t =>
            word.BoundingBox.Left >= t.MinX - 5 &&
            word.BoundingBox.Right <= t.MaxX + 5 &&
            word.BoundingBox.Bottom >= t.MinY - 5 &&
            word.BoundingBox.Top <= t.MaxY + 5);
    }

    /// <summary>
    /// Merge page texts with intelligent sentence boundary handling.
    /// </summary>
    private static string MergePageTexts(List<PageContent> pageTexts)
    {
        if (pageTexts.Count == 0) return "";
        if (pageTexts.Count == 1) return NormalizeText(pageTexts[0].Text);

        var result = new StringBuilder();

        for (int i = 0; i < pageTexts.Count; i++)
        {
            var currentPage = pageTexts[i];
            var normalizedText = NormalizeText(currentPage.Text);

            if (i == 0)
            {
                result.Append(normalizedText);
                continue;
            }

            var prevPage = pageTexts[i - 1];

            // Check if we need to merge sentences across page boundary
            if (prevPage.EndsWithIncompleteSentence && currentPage.StartsWithIncompleteSentence)
            {
                // Merge without paragraph break - just add a space
                if (result.Length > 0 && !char.IsWhiteSpace(result[^1]))
                {
                    result.Append(' ');
                }
                result.Append(normalizedText);
            }
            else
            {
                // Add paragraph break between pages
                result.AppendLine();
                result.AppendLine();
                result.Append(normalizedText);
            }
        }

        return result.ToString().Trim();
    }

    /// <summary>
    /// Build page range mappings after text merging.
    /// </summary>
    private static Dictionary<int, (int Start, int End)> BuildPageRanges(List<PageContent> pageTexts, string mergedText)
    {
        var ranges = new Dictionary<int, (int Start, int End)>();
        var currentPosition = 0;

        foreach (var page in pageTexts)
        {
            var normalizedLength = NormalizeText(page.Text).Length;
            if (normalizedLength > 0)
            {
                ranges[page.PageNumber] = (currentPosition, currentPosition + normalizedLength - 1);
                currentPosition += normalizedLength + 2; // Account for paragraph breaks
            }
        }

        return ranges;
    }

    /// <summary>
    /// Check if text ends with an incomplete sentence.
    /// </summary>
    private static bool IsIncompleteSentenceEnd(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.TrimEnd();
        if (trimmed.Length == 0) return false;

        // Check for sentence-ending punctuation
        var lastChar = trimmed[^1];
        if (lastChar == '.' || lastChar == '!' || lastChar == '?' ||
            lastChar == '。' || lastChar == '！' || lastChar == '？')
        {
            return false;
        }

        // Check for common incomplete endings
        return IncompleteEndPattern.IsMatch(trimmed);
    }

    /// <summary>
    /// Filter out page numbers from text lines.
    /// Page numbers are typically short lines that match common patterns.
    /// </summary>
    private static string FilterPageNumbers(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var lines = text.Split('\n');
        var filteredLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Skip empty lines (will be re-added as needed)
            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                filteredLines.Add(line);
                continue;
            }

            // Page numbers are typically very short (< 20 characters)
            // and match specific patterns
            if (trimmedLine.Length < 20 && IsPageNumber(trimmedLine))
            {
                // Skip this line (it's a page number)
                continue;
            }

            filteredLines.Add(line);
        }

        return string.Join('\n', filteredLines);
    }

    /// <summary>
    /// Check if a line is likely a page number.
    /// </summary>
    private static bool IsPageNumber(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;

        // Check against page number patterns
        return PageNumberPatterns.IsMatch(line);
    }

    /// <summary>
    /// Check if text starts with an incomplete sentence.
    /// </summary>
    private static bool IsIncompleteSentenceStart(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.TrimStart();
        if (trimmed.Length == 0) return false;

        // Check if starts with lowercase letter (likely continuation)
        var firstChar = trimmed[0];
        if (char.IsLower(firstChar))
        {
            return true;
        }

        // Check for continuation patterns
        return IncompleteStartPattern.IsMatch(trimmed);
    }

    private static double EstimateLineHeight(IList<Word> words)
    {
        if (!words.Any()) return 12.0; // 기본값

        // 단어들의 높이 분석
        var heights = words
            .Where(w => w.BoundingBox.Height > 0)
            .Select(w => w.BoundingBox.Height)
            .ToList();

        if (heights.Count == 0) return 12.0;

        // 중간값 사용 (이상치 제거)
        heights.Sort();
        var medianHeight = heights[heights.Count / 2];

        // 줄간격은 보통 글자 높이의 1.2~1.5배
        return medianHeight * 1.3;
    }

    /// <summary>
    /// 추출된 텍스트를 RAG에 최적화된 형태로 정규화
    /// </summary>
    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        // 0. Remove null bytes (invalid in UTF-8 text)
        text = TextSanitizer.RemoveNullBytes(text);

        // 1. 연속된 공백 및 탭 정리
        text = MyRegex().Replace(text, " ");

        // 2. 연속된 줄바꿈 정리 (3개 이상 → 2개로 제한)
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");

        // 3. 줄 끝의 불필요한 공백 제거
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd();
        }

        // 4. 빈 줄들 사이의 단일 공백 제거
        text = string.Join('\n', lines);

        // 5. 문서 시작/끝 공백 정리
        text = text.Trim();

        // 6. 하이픈으로 끝나는 단어 연결 (예: "docu-\nment" → "document")
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(\w+)-\s*\n\s*(\w+)", "$1$2");

        // 7. 단락 내 줄바꿈 정리 (문장 중간의 줄바꿈을 공백으로)
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?<![.!?])\n(?![A-Z•\d])", " ");

        return text;
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        return text
            .Split(separator, StringSplitOptions.RemoveEmptyEntries)
            .Length;
    }

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex MyRegex();

    // Pattern for text ending without sentence-ending punctuation (likely incomplete)
    [GeneratedRegex(@"[a-zA-Z가-힣,;:\-]$")]
    private static partial Regex IncompleteSentenceEndRegex();

    // Pattern for text starting with lowercase or continuation markers
    [GeneratedRegex(@"^[a-z]|^[,;:\-]|^[가-힣](?![.!?。！？])")]
    private static partial Regex IncompleteSentenceStartRegex();

    // Pattern for common page number formats:
    // - Standalone numbers: "1", "12"
    // - Dash-surrounded: "- 1 -", "-2-"
    // - Page prefix: "Page 1", "PAGE 12", "page 1 of 10", "p. 1"
    // - Korean: "페이지 1", "쪽 1"
    // - Fraction format: "1/10", "1 / 10"
    // - Roman numerals: "i", "ii", "iii", "iv", "v" (lowercase only for page numbers)
    [GeneratedRegex(@"^\s*(?:-\s*)?\d{1,4}(?:\s*-)?$|^(?:page|p\.?)\s*\d{1,4}(?:\s*(?:of|/)\s*\d{1,4})?\s*$|^(?:페이지|쪽)\s*\d{1,4}$|^\d{1,4}\s*/\s*\d{1,4}$|^[ivxlc]{1,4}$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PageNumberRegex();

    #region Internal Classes

    /// <summary>
    /// Represents extracted content from a single PDF page.
    /// </summary>
    private sealed class PageContent
    {
        public int PageNumber { get; set; }
        public string Text { get; set; } = "";
        public int TableCount { get; set; }
        /// <summary>
        /// Number of tables that used plain text fallback due to low confidence.
        /// </summary>
        public int LowConfidenceTableCount { get; set; }
        /// <summary>
        /// Minimum confidence score among detected tables (1.0 if no tables).
        /// </summary>
        public double MinTableConfidence { get; set; } = 1.0;
        public bool EndsWithIncompleteSentence { get; set; }
        public bool StartsWithIncompleteSentence { get; set; }
        public bool HasContent => !string.IsNullOrWhiteSpace(Text);
    }

    /// <summary>
    /// Represents a detected table region in the page.
    /// </summary>
    private sealed class TableRegion
    {
        public double MinX { get; set; }
        public double MaxX { get; set; }
        public double MinY { get; set; }
        public double MaxY { get; set; }
        public double TopY { get; set; }
        public string Markdown { get; set; } = "";
        /// <summary>
        /// Confidence score for table detection (0.0 to 1.0).
        /// Below 0.5 threshold, plain text fallback is recommended.
        /// </summary>
        public double ConfidenceScore { get; set; } = 1.0;
        /// <summary>
        /// Plain text representation as fallback for low-confidence tables.
        /// </summary>
        public string PlainTextFallback { get; set; } = "";
        public int RowCount { get; set; }
        public int ColumnCount { get; set; }
    }

    /// <summary>
    /// Represents a text block (either table or regular text).
    /// </summary>
    private sealed class TextBlock
    {
        public string Content { get; set; } = "";
        public bool IsTable { get; set; }
        public double TopY { get; set; }
        public double LeftX { get; set; }
    }

    #endregion
}
