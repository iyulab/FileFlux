using FileFlux.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using System.Text;
using System.Text.RegularExpressions;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// PDF document reader optimized for RAG preprocessing.
/// Uses PdfPig library for text extraction with:
/// - Table detection with raw cell data (no markdown conversion)
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

    /// <summary>
    /// Confidence threshold for table detection.
    /// Tables below this threshold will have NeedsLlmAssist = true.
    /// </summary>
    private const double TableConfidenceThreshold = 0.7;

    /// <summary>
    /// Maximum reasonable column count for table detection.
    /// </summary>
    private const int MaxReasonableColumnCount = 10;

    /// <summary>
    /// Minimum gap ratio to consider as column separator.
    /// </summary>
    private const double MinColumnGapRatio = 2.0;

    /// <summary>
    /// Minimum image dimension in pixels to extract.
    /// </summary>
    private const int MinImageDimension = 50;

    /// <summary>
    /// Minimum image data size in bytes to extract.
    /// </summary>
    private const int MinImageDataSize = 1000;

    public bool CanRead(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension == ".pdf";
    }

    #region Stage 0: Read

    /// <summary>
    /// Stage 0: Read - Parses document structure without content extraction.
    /// </summary>
    public async Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PDF file not found: {filePath}");

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        return await Task.Run(() => ReadPdfStructure(filePath), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stage 0: Read - Parses document structure from stream.
    /// </summary>
    public async Task<ReadResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!CanRead(fileName))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        return await Task.Run(() => ReadPdfStructureFromStream(stream, fileName), cancellationToken).ConfigureAwait(false);
    }

    private ReadResult ReadPdfStructure(string filePath)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var warnings = new List<string>();
        var pages = new List<PageInfo>();
        var docProps = new Dictionary<string, object>();

        var fileInfo = new FileInfo(filePath);

        try
        {
            using var document = PdfDocument.Open(filePath);

            // Collect PDF metadata
            CollectDocumentMetadata(document, docProps);

            // Collect page info
            for (int pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
            {
                try
                {
                    var page = document.GetPage(pageNum);
                    pages.Add(new PageInfo
                    {
                        Number = pageNum,
                        Width = page.Width,
                        Height = page.Height
                    });
                }
                catch (Exception ex)
                {
                    warnings.Add($"Page {pageNum} read error: {ex.Message}");
                }
            }

            stopwatch.Stop();

            return new ReadResult
            {
                File = new SourceFileInfo
                {
                    Name = Path.GetFileName(filePath),
                    Extension = ".pdf",
                    Size = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    ModifiedAt = fileInfo.LastWriteTimeUtc
                },
                ReaderType = ReaderType,
                Pages = pages,
                DocumentProps = docProps,
                Duration = stopwatch.Elapsed,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"PDF file read error: {ex.Message}", ex);
        }
    }

    private ReadResult ReadPdfStructureFromStream(Stream stream, string fileName)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var warnings = new List<string>();
        var pages = new List<PageInfo>();
        var docProps = new Dictionary<string, object>();

        var streamLength = stream.CanSeek ? stream.Length : -1;

        try
        {
            using var document = PdfDocument.Open(stream);

            CollectDocumentMetadata(document, docProps);

            for (int pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
            {
                try
                {
                    var page = document.GetPage(pageNum);
                    pages.Add(new PageInfo
                    {
                        Number = pageNum,
                        Width = page.Width,
                        Height = page.Height
                    });
                }
                catch (Exception ex)
                {
                    warnings.Add($"Page {pageNum} read error: {ex.Message}");
                }
            }

            stopwatch.Stop();

            return new ReadResult
            {
                File = new SourceFileInfo
                {
                    Name = fileName,
                    Extension = ".pdf",
                    Size = streamLength
                },
                ReaderType = ReaderType,
                Pages = pages,
                DocumentProps = docProps,
                Duration = stopwatch.Elapsed,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"PDF stream read error: {ex.Message}", ex);
        }
    }

    private static void CollectDocumentMetadata(PdfDocument document, Dictionary<string, object> docProps)
    {
        var info = document.Information;
        if (info != null)
        {
            docProps["Title"] = info.Title ?? "";
            docProps["Author"] = info.Author ?? "";
            docProps["Subject"] = info.Subject ?? "";
            docProps["Creator"] = info.Creator ?? "";
            docProps["Producer"] = info.Producer ?? "";
            if (info.CreationDate != null)
                docProps["CreationDate"] = info.CreationDate;
            if (info.ModifiedDate != null)
                docProps["ModifiedDate"] = info.ModifiedDate;
        }

        docProps["PageCount"] = document.NumberOfPages;
        docProps["Version"] = document.Version.ToString();
    }

    #endregion

    #region Stage 1: Extract

    /// <summary>
    /// Stage 1: Extract - Extracts raw content without markdown conversion.
    /// </summary>
    public async Task<RawContent> ExtractAsync(string filePath, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PDF file not found: {filePath}");

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        options ??= ExtractOptions.Default;
        return await Task.Run(() => ExtractPdfContent(filePath, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stage 1: Extract - Extracts raw content from stream.
    /// </summary>
    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!CanRead(fileName))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        options ??= ExtractOptions.Default;
        return await Task.Run(() => ExtractPdfContentFromStream(stream, fileName, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private RawContent ExtractPdfContent(string filePath, ExtractOptions options, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var warnings = new List<string>();
        var hints = new Dictionary<string, object>();
        var pageContents = new List<PageContentData>();
        var allBlocks = new List<TextBlock>();
        var allTables = new List<TableData>();
        var images = new List<ImageInfo>();

        var fileInfo = new FileInfo(filePath);

        try
        {
            using var document = PdfDocument.Open(filePath);

            CollectDocumentMetadata(document, hints);

            var totalPages = document.NumberOfPages;
            var processedPages = 0;

            // Handle page range
            var startPage = options.PageRange?.Start ?? 1;
            var endPage = options.PageRange?.End ?? totalPages;

            for (int pageNum = startPage; pageNum <= endPage; pageNum++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var page = document.GetPage(pageNum);
                    var pageData = ExtractPageContent(page, pageNum, options, warnings);

                    if (pageData.HasContent)
                    {
                        pageContents.Add(pageData);
                        allBlocks.AddRange(pageData.Blocks);
                        allTables.AddRange(pageData.Tables);
                    }

                    // Extract images if requested
                    if (options.ExtractImages)
                    {
                        ExtractImagesFromPage(page, pageNum, images, warnings, options.MaxImageSize);
                    }

                    processedPages++;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Page {pageNum} processing error: {ex.Message}");
                }
            }

            // Merge page texts
            var extractedText = MergePageTexts(pageContents);

            // Build page ranges
            var pageRanges = BuildPageRanges(pageContents, extractedText);

            // Extract bookmarks and apply to blocks for enhanced heading detection
            var bookmarks = ExtractBookmarks(document, warnings);
            if (bookmarks.Count > 0)
            {
                ApplyBookmarkHints(allBlocks, bookmarks);
                hints["BookmarksExtracted"] = bookmarks.Count;
            }

            // Extraction statistics
            hints["ProcessedPages"] = processedPages;
            hints["TotalCharacters"] = extractedText.Length;
            hints["WordCount"] = CountWords(extractedText);
            hints["LineCount"] = extractedText.Split('\n').Length;
            hints["PageRanges"] = pageRanges;
            hints["TablesDetected"] = allTables.Count;
            hints["LowConfidenceTables"] = allTables.Count(t => t.NeedsLlmAssist);
            hints["BlocksDetected"] = allBlocks.Count;
            hints["ImagesExtracted"] = images.Count;

            if (processedPages < totalPages)
            {
                warnings.Add($"Partial page processing: {processedPages}/{totalPages} pages processed");
            }

            stopwatch.Stop();

            return new RawContent
            {
                Text = extractedText,
                Blocks = allBlocks,
                Tables = allTables,
                Images = images,
                File = new SourceFileInfo
                {
                    Name = Path.GetFileName(filePath),
                    Extension = ".pdf",
                    Size = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    ModifiedAt = fileInfo.LastWriteTimeUtc
                },
                Hints = hints,
                Warnings = warnings,
                ReaderType = ReaderType,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"PDF file processing error: {ex.Message}", ex);
        }
    }

    private RawContent ExtractPdfContentFromStream(Stream stream, string fileName, ExtractOptions options, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var warnings = new List<string>();
        var hints = new Dictionary<string, object>();
        var pageContents = new List<PageContentData>();
        var allBlocks = new List<TextBlock>();
        var allTables = new List<TableData>();
        var images = new List<ImageInfo>();

        var streamLength = stream.CanSeek ? stream.Length : -1;

        try
        {
            using var document = PdfDocument.Open(stream);

            CollectDocumentMetadata(document, hints);

            var totalPages = document.NumberOfPages;
            var processedPages = 0;

            var startPage = options.PageRange?.Start ?? 1;
            var endPage = options.PageRange?.End ?? totalPages;

            for (int pageNum = startPage; pageNum <= endPage; pageNum++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var page = document.GetPage(pageNum);
                    var pageData = ExtractPageContent(page, pageNum, options, warnings);

                    if (pageData.HasContent)
                    {
                        pageContents.Add(pageData);
                        allBlocks.AddRange(pageData.Blocks);
                        allTables.AddRange(pageData.Tables);
                    }

                    if (options.ExtractImages)
                    {
                        ExtractImagesFromPage(page, pageNum, images, warnings, options.MaxImageSize);
                    }

                    processedPages++;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Page {pageNum} processing error: {ex.Message}");
                }
            }

            var extractedText = MergePageTexts(pageContents);

            // Extract bookmarks and apply to blocks for enhanced heading detection
            var bookmarks = ExtractBookmarks(document, warnings);
            if (bookmarks.Count > 0)
            {
                ApplyBookmarkHints(allBlocks, bookmarks);
                hints["BookmarksExtracted"] = bookmarks.Count;
            }

            hints["ProcessedPages"] = processedPages;
            hints["TotalCharacters"] = extractedText.Length;
            hints["WordCount"] = CountWords(extractedText);
            hints["LineCount"] = extractedText.Split('\n').Length;
            hints["TablesDetected"] = allTables.Count;
            hints["LowConfidenceTables"] = allTables.Count(t => t.NeedsLlmAssist);
            hints["BlocksDetected"] = allBlocks.Count;
            hints["ImagesExtracted"] = images.Count;

            if (processedPages < totalPages)
            {
                warnings.Add($"Partial page processing: {processedPages}/{totalPages} pages processed");
            }

            stopwatch.Stop();

            return new RawContent
            {
                Text = extractedText,
                Blocks = allBlocks,
                Tables = allTables,
                Images = images,
                File = new SourceFileInfo
                {
                    Name = fileName,
                    Extension = ".pdf",
                    Size = streamLength
                },
                Hints = hints,
                Warnings = warnings,
                ReaderType = ReaderType,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"PDF stream processing error: {ex.Message}", ex);
        }
    }

    #endregion

    #region Page Content Extraction

    /// <summary>
    /// Extract content from a single page with table detection and layout analysis.
    /// Returns raw data without markdown conversion.
    /// </summary>
    private PageContentData ExtractPageContent(Page page, int pageNum, ExtractOptions options, List<string> warnings)
    {
        var data = new PageContentData { PageNumber = pageNum };

        try
        {
            var words = page.GetWords().ToList();
            if (words.Count == 0)
            {
                warnings.Add($"Page {pageNum}: No text content");
                return data;
            }

            var lineHeight = EstimateLineHeight(words);
            List<TableRegion> tableRegions = [];

            // Detect tables if requested
            if (options.ExtractTables)
            {
                tableRegions = DetectTables(page, words, warnings);
                data.Tables = tableRegions
                    .Select(tr => CreateTableData(tr, pageNum, page.Height, options.PreserveCoordinates))
                    .ToList();
            }

            // Extract text blocks
            var nonTableWords = words.Where(w => !IsWordInTable(w, tableRegions)).ToList();
            data.Blocks = ExtractTextBlocks(page, nonTableWords, pageNum, lineHeight, options);

            // Build plain text (without tables)
            var textBuilder = new StringBuilder();
            foreach (var block in data.Blocks.OrderBy(b => b.Order))
            {
                if (block.HasContent)
                {
                    textBuilder.AppendLine(block.Content);
                }
            }

            var rawText = textBuilder.ToString().Trim();
            data.Text = FilterPageNumbers(rawText);
            data.EndsWithIncompleteSentence = IsIncompleteSentenceEnd(data.Text);
            data.StartsWithIncompleteSentence = IsIncompleteSentenceStart(data.Text);
        }
        catch (Exception ex)
        {
            warnings.Add($"Page {pageNum} extraction error: {ex.Message}");
        }

        return data;
    }

    /// <summary>
    /// Extract text blocks from page words.
    /// </summary>
    private static List<TextBlock> ExtractTextBlocks(Page page, List<Word> words, int pageNum, double lineHeight, ExtractOptions options)
    {
        var blocks = new List<TextBlock>();

        if (words.Count == 0) return blocks;

        var sortedWords = words
            .OrderBy(w => page.Height - w.BoundingBox.Bottom)
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();

        var currentBlockWords = new List<Word>();
        double currentLineY = double.MinValue;
        var blockOrder = 0;

        foreach (var word in sortedWords)
        {
            var wordY = Math.Round(page.Height - word.BoundingBox.Bottom, 1);

            // Check for paragraph break (larger gap than line height)
            if (currentBlockWords.Count > 0 && Math.Abs(wordY - currentLineY) > lineHeight * 1.5)
            {
                // Finalize current block
                var block = CreateTextBlock(currentBlockWords, pageNum, page.Height, blockOrder++, options);
                if (block != null)
                {
                    blocks.Add(block);
                }
                currentBlockWords = [];
            }

            currentBlockWords.Add(word);
            currentLineY = wordY;
        }

        // Finalize last block
        if (currentBlockWords.Count > 0)
        {
            var block = CreateTextBlock(currentBlockWords, pageNum, page.Height, blockOrder, options);
            if (block != null)
            {
                blocks.Add(block);
            }
        }

        // Detect block types if requested
        if (options.DetectBlockTypes)
        {
            DetectBlockTypes(blocks);
        }

        return blocks;
    }

    /// <summary>
    /// Create a TextBlock from a group of words.
    /// </summary>
    private static TextBlock? CreateTextBlock(List<Word> words, int pageNum, double pageHeight, int order, ExtractOptions options)
    {
        if (words.Count == 0) return null;

        var lineHeight = EstimateLineHeight(words);
        var sortedWords = words.OrderBy(w => pageHeight - w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left).ToList();

        var textBuilder = new StringBuilder();
        double currentY = double.MinValue;

        foreach (var word in sortedWords)
        {
            var wordY = Math.Round(pageHeight - word.BoundingBox.Bottom, 1);

            if (Math.Abs(wordY - currentY) > lineHeight * 0.5)
            {
                if (textBuilder.Length > 0)
                    textBuilder.AppendLine();
                currentY = wordY;
            }
            else if (textBuilder.Length > 0 && !textBuilder.ToString().EndsWith('\n'))
            {
                textBuilder.Append(' ');
            }

            textBuilder.Append(word.Text);
        }

        var content = textBuilder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(content)) return null;

        var block = new TextBlock
        {
            Content = content,
            PageNumber = pageNum,
            Order = order,
            Type = BlockType.Paragraph // Default, may be updated by DetectBlockTypes
        };

        // Add location if requested
        if (options.PreserveCoordinates)
        {
            var minX = words.Min(w => w.BoundingBox.Left);
            var maxX = words.Max(w => w.BoundingBox.Right);
            var minY = words.Min(w => w.BoundingBox.Bottom);
            var maxY = words.Max(w => w.BoundingBox.Top);

            block.Location = BoundingBox.FromCoordinates(minX, pageHeight - maxY, maxX, pageHeight - minY);
        }

        // Extract style info from first letter of first word
        var firstWord = words.First();
        var firstLetter = firstWord.Letters.FirstOrDefault();
        if (firstLetter != null)
        {
            var fontName = firstLetter.FontName ?? string.Empty;
            block.Style = new TextStyle
            {
                FontFamily = fontName,
                FontSize = firstLetter.PointSize,
                IsBold = fontName.Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
                         fontName.Contains("Black", StringComparison.OrdinalIgnoreCase) ||
                         fontName.Contains("Heavy", StringComparison.OrdinalIgnoreCase),
                IsItalic = fontName.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                           fontName.Contains("Oblique", StringComparison.OrdinalIgnoreCase)
            };
        }

        return block;
    }

    /// <summary>
    /// Detect block types (headings, lists, etc.) based on font analysis and content patterns.
    /// </summary>
    private static void DetectBlockTypes(List<TextBlock> blocks)
    {
        // Calculate median font size for heading detection (median avoids outliers from headings)
        var medianFontSize = CalculateMedianFontSize(blocks);

        foreach (var block in blocks)
        {
            var content = block.Content.Trim();
            var fontSize = block.Style?.FontSize ?? 0;
            var isBold = block.Style?.IsBold ?? false;

            // === FONT-BASED HEADING DETECTION (Primary method for --no-ai) ===
            if (medianFontSize > 0 && fontSize > 0 && content.Length < 150 && !content.Contains('\n'))
            {
                var sizeRatio = fontSize / medianFontSize;

                // Large font + Bold = Strong heading signal
                if (sizeRatio >= 1.5 && isBold)
                {
                    block.Type = BlockType.Heading;
                    block.HeadingLevel = 1;
                    continue;
                }
                if (sizeRatio >= 1.3 && isBold)
                {
                    block.Type = BlockType.Heading;
                    block.HeadingLevel = 2;
                    continue;
                }
                if (sizeRatio >= 1.2 && isBold)
                {
                    block.Type = BlockType.Heading;
                    block.HeadingLevel = 3;
                    continue;
                }
                if (sizeRatio >= 1.1 && isBold)
                {
                    block.Type = BlockType.Heading;
                    block.HeadingLevel = 4;
                    continue;
                }
                // Bold only (normal size) = Minor heading
                if (isBold && content.Length < 80 && !content.EndsWith('.'))
                {
                    block.Type = BlockType.Heading;
                    block.HeadingLevel = 5;
                    continue;
                }
                // Large font without bold = Still likely heading
                if (sizeRatio >= 1.4 && content.Length < 80 && !content.EndsWith('.'))
                {
                    block.Type = BlockType.Heading;
                    block.HeadingLevel = 2;
                    continue;
                }
            }

            // === PATTERN-BASED HEADING DETECTION (Fallback/Enhancement) ===
            if (content.Length < 100 && !content.Contains('\n'))
            {
                // Check for numbered heading patterns (1.2.3, Chapter, 제1장)
                if (Regex.IsMatch(content, @"^(?:\d+\.)+\s+\S") ||
                    Regex.IsMatch(content, @"^(?:Chapter|Section|Part)\s+\d+", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(content, @"^제\s*\d+\s*[장절]"))
                {
                    block.Type = BlockType.Heading;
                    block.HeadingLevel = DetectHeadingLevel(content);
                    continue;
                }

                // All-caps headings
                if (content.Length > 3 && content.ToUpper() == content && content.Any(char.IsLetter))
                {
                    block.Type = BlockType.Heading;
                    block.HeadingLevel = 2;
                    continue;
                }
            }

            // === LIST DETECTION ===
            if (Regex.IsMatch(content, @"^[\u2022\u2023\u25E6\u2043\u2219•\-\*]\s+"))
            {
                block.Type = BlockType.ListItem;
                block.IsOrderedList = false;
                continue;
            }

            if (Regex.IsMatch(content, @"^\d+[\.\)]\s+"))
            {
                block.Type = BlockType.ListItem;
                block.IsOrderedList = true;
                continue;
            }

            // === CODE BLOCK DETECTION ===
            if (content.Contains("```") || (content.Contains("    ") && content.Contains("();")))
            {
                block.Type = BlockType.CodeBlock;
                continue;
            }

            // === QUOTE DETECTION ===
            if ((content.StartsWith('"') && content.EndsWith('"')) ||
                (content.StartsWith("\"") && content.EndsWith("\"")))
            {
                block.Type = BlockType.Quote;
            }
        }
    }

    /// <summary>
    /// Calculate median font size from blocks (median avoids outliers from headings).
    /// </summary>
    private static double CalculateMedianFontSize(List<TextBlock> blocks)
    {
        var fontSizes = blocks
            .Where(b => b.Style?.FontSize is > 0)
            .Select(b => b.Style!.FontSize!.Value)
            .OrderBy(s => s)
            .ToList();

        if (fontSizes.Count == 0) return 0;

        // Return median
        var mid = fontSizes.Count / 2;
        return fontSizes.Count % 2 == 0
            ? (fontSizes[mid - 1] + fontSizes[mid]) / 2.0
            : fontSizes[mid];
    }

    private static int DetectHeadingLevel(string content)
    {
        // Count dots in numbered headings like "1.2.3"
        var match = Regex.Match(content, @"^((?:\d+\.)+)");
        if (match.Success)
        {
            var dots = match.Groups[1].Value.Count(c => c == '.');
            return Math.Min(dots, 6);
        }

        // Chapter = 1, Section = 2, etc.
        if (Regex.IsMatch(content, @"^Chapter\s+", RegexOptions.IgnoreCase))
            return 1;
        if (Regex.IsMatch(content, @"^Section\s+", RegexOptions.IgnoreCase))
            return 2;

        return 2; // Default heading level
    }

    #endregion

    #region Table Detection

    /// <summary>
    /// Detect table regions in the page based on word alignment patterns.
    /// </summary>
    private static List<TableRegion> DetectTables(Page page, List<Word> words, List<string> warnings)
    {
        var tables = new List<TableRegion>();

        try
        {
            var lineHeight = EstimateLineHeight(words);
            var rows = GroupWordsIntoRows(words, lineHeight);

            if (rows.Count < 2) return tables;

            var columnPositions = DetectColumnPositions(rows);

            if (columnPositions.Count >= 2)
            {
                var tableRows = new List<List<Word>>();
                var inTable = false;
                var alignedRowCount = 0;

                foreach (var row in rows)
                {
                    var alignsToColumns = RowAlignsToColumns(row, columnPositions, lineHeight);

                    if (alignsToColumns)
                    {
                        alignedRowCount++;
                        if (!inTable)
                        {
                            tableRows = [];
                            inTable = true;
                        }
                        tableRows.Add(row);
                    }
                    else if (inTable)
                    {
                        if (tableRows.Count >= 2)
                        {
                            var table = CreateTableRegion(tableRows, columnPositions, page.Height);
                            if (table != null)
                            {
                                tables.Add(table);
                            }
                        }
                        inTable = false;
                    }
                }

                if (inTable && tableRows.Count >= 2)
                {
                    var table = CreateTableRegion(tableRows, columnPositions, page.Height);
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
    /// Create a TableRegion from detected table rows with raw cell data.
    /// </summary>
    private static TableRegion? CreateTableRegion(List<List<Word>> tableRows, List<double> columnPositions, double pageHeight)
    {
        if (tableRows.Count < 2) return null;

        var allWords = tableRows.SelectMany(r => r).ToList();
        var minX = allWords.Min(w => w.BoundingBox.Left);
        var maxX = allWords.Max(w => w.BoundingBox.Right);
        var minY = allWords.Min(w => w.BoundingBox.Bottom);
        var maxY = allWords.Max(w => w.BoundingBox.Top);

        // Build raw cell data (no markdown!)
        var cells = new List<string[]>();
        var totalCellCount = 0;
        var emptyCellCount = 0;

        foreach (var row in tableRows)
        {
            var rowCells = AssignWordsToColumns(row, columnPositions);
            cells.Add(rowCells.ToArray());

            totalCellCount += rowCells.Count;
            emptyCellCount += rowCells.Count(c => string.IsNullOrWhiteSpace(c));
        }

        // Build plain text fallback
        var plainText = new StringBuilder();
        foreach (var rowCells in cells)
        {
            var rowText = string.Join("  ", rowCells.Where(c => !string.IsNullOrWhiteSpace(c)));
            if (!string.IsNullOrWhiteSpace(rowText))
                plainText.AppendLine(rowText);
        }

        var confidenceScore = CalculateTableConfidence(cells, columnPositions.Count, emptyCellCount, totalCellCount);

        return new TableRegion
        {
            MinX = minX,
            MaxX = maxX,
            MinY = minY,
            MaxY = maxY,
            TopY = pageHeight - maxY,
            Cells = cells.ToArray().Select(r => r).ToArray(),
            PlainTextFallback = plainText.ToString().Trim(),
            ConfidenceScore = confidenceScore,
            RowCount = tableRows.Count,
            ColumnCount = columnPositions.Count
        };
    }

    /// <summary>
    /// Convert TableRegion to TableData domain model.
    /// </summary>
    private static TableData CreateTableData(TableRegion region, int pageNum, double pageHeight, bool preserveCoordinates)
    {
        var tableData = new TableData
        {
            Cells = region.Cells,
            HasHeader = true, // Assume first row is header by default
            Confidence = region.ConfidenceScore,
            DetectionMethod = region.ConfidenceScore >= TableConfidenceThreshold
                ? TableDetectionMethod.AlignmentPattern
                : TableDetectionMethod.Heuristic,
            PageNumber = pageNum,
            PlainTextFallback = region.PlainTextFallback
        };

        if (preserveCoordinates)
        {
            tableData.Location = BoundingBox.FromCoordinates(
                region.MinX,
                region.TopY,
                region.MaxX,
                region.TopY + (region.MaxY - region.MinY));
        }

        return tableData;
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

        foreach (var row in rows)
        {
            row.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
        }

        return rows;
    }

    /// <summary>
    /// Detect column positions by analyzing word alignment across rows.
    /// </summary>
    private static List<double> DetectColumnPositions(List<List<Word>> rows)
    {
        if (rows.Count == 0) return [];

        var allWords = rows.SelectMany(r => r).ToList();
        if (allWords.Count == 0) return [];

        var gapBasedColumns = DetectColumnsByGaps(rows);
        var positionBasedColumns = DetectColumnsByPositions(rows, allWords);

        if (gapBasedColumns.Count >= 2 && gapBasedColumns.Count <= MaxReasonableColumnCount)
        {
            var gapAlignmentScore = CalculateColumnAlignmentScore(rows, gapBasedColumns);
            var posAlignmentScore = positionBasedColumns.Count >= 2
                ? CalculateColumnAlignmentScore(rows, positionBasedColumns)
                : 0;

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

        var rowGaps = new List<List<(double GapStart, double GapEnd, double Width)>>();

        foreach (var row in rows)
        {
            if (row.Count < 2) continue;

            var sortedWords = row.OrderBy(w => w.BoundingBox.Left).ToList();
            var gaps = new List<(double GapStart, double GapEnd, double Width)>();

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

        var consistentGaps = FindConsistentGaps(rowGaps, rows.Count);

        if (consistentGaps.Count == 0) return [];

        var columnPositions = new List<double>();
        var leftmostX = rows.SelectMany(r => r).Min(w => w.BoundingBox.Left);
        columnPositions.Add(leftmostX);

        foreach (var gap in consistentGaps.OrderBy(g => g))
        {
            columnPositions.Add(gap);
        }

        return columnPositions;
    }

    private static List<double> FindConsistentGaps(
        List<List<(double GapStart, double GapEnd, double Width)>> rowGaps,
        int totalRows)
    {
        if (rowGaps.Count == 0) return [];

        var allGaps = rowGaps.SelectMany(g => g).ToList();
        var avgGapWidth = allGaps.Average(g => g.Width);
        var tolerance = Math.Max(5, avgGapWidth * 0.5);

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

                gapClusters[bucket].Add(gap.GapEnd);
            }
        }

        var threshold = Math.Max(2, rowGaps.Count / 2);
        var consistentGaps = gapClusters
            .Where(kvp => kvp.Value.Count >= threshold)
            .Select(kvp => kvp.Value.Average())
            .OrderBy(x => x)
            .ToList();

        return consistentGaps;
    }

    private static List<double> DetectColumnsByPositions(List<List<Word>> rows, List<Word> allWords)
    {
        var avgCharWidth = allWords
            .Where(w => !string.IsNullOrEmpty(w.Text))
            .Select(w => w.BoundingBox.Width / Math.Max(1, w.Text.Length))
            .DefaultIfEmpty(5.0)
            .Average();

        var bucketSize = Math.Max(3, (int)(avgCharWidth * 1.5));

        var xPositions = new Dictionary<int, int>();

        foreach (var row in rows)
        {
            foreach (var word in row)
            {
                var bucket = (int)(word.BoundingBox.Left / bucketSize) * bucketSize;
                xPositions.TryAdd(bucket, 0);
                xPositions[bucket]++;
            }
        }

        var threshold = Math.Max(2, (int)(rows.Count * 0.4));
        var rawPositions = xPositions
            .Where(kvp => kvp.Value >= threshold)
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => (double)kvp.Key)
            .ToList();

        var mergedPositions = MergeCloseColumns(rawPositions, bucketSize * 2);

        if (mergedPositions.Count > MaxReasonableColumnCount)
        {
            mergedPositions = mergedPositions.Take(MaxReasonableColumnCount).ToList();
        }

        return mergedPositions;
    }

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
                if (columnPositions.Any(cp => Math.Abs(word.BoundingBox.Left - cp) <= tolerance))
                {
                    alignedWords++;
                }
            }
        }

        return totalWords > 0 ? (double)alignedWords / totalWords : 0;
    }

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
        }

        return merged;
    }

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

        return alignedCount >= row.Count / 2;
    }

    private static List<string> AssignWordsToColumns(List<Word> row, List<double> columnPositions)
    {
        var cells = new List<string>();
        var sortedColumns = columnPositions.OrderBy(x => x).ToList();

        if (sortedColumns.Count == 0) return cells;

        var avgColumnWidth = sortedColumns.Count > 1
            ? sortedColumns.Zip(sortedColumns.Skip(1), (a, b) => b - a).Average()
            : 50.0;
        var tolerance = Math.Max(5, avgColumnWidth * 0.15);

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

        while (cells.Count < sortedColumns.Count)
        {
            cells.Add("");
        }

        return cells;
    }

    private static double CalculateTableConfidence(IList<string[]> cells, int expectedColumns, int emptyCellCount, int totalCellCount)
    {
        if (cells.Count == 0 || expectedColumns == 0) return 0.0;

        var columnCounts = cells.Select(r => r.Length).ToList();
        var modeColumnCount = columnCounts.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
        var consistentRowCount = columnCounts.Count(c => c == modeColumnCount);
        var columnConsistency = (double)consistentRowCount / cells.Count;

        var emptyCellRatio = totalCellCount > 0 ? (double)emptyCellCount / totalCellCount : 1.0;
        var contentScore = 1.0 - emptyCellRatio;

        var columnCountScore = expectedColumns switch
        {
            <= 2 => 0.7,
            <= 6 => 1.0,
            <= 10 => 0.8,
            <= 15 => 0.5,
            _ => 0.2
        };

        var score = (columnConsistency * 0.4) + (contentScore * 0.3) + (columnCountScore * 0.3);
        return Math.Round(score, 2);
    }

    private static bool IsWordInTable(Word word, List<TableRegion> tables)
    {
        return tables.Any(t =>
            word.BoundingBox.Left >= t.MinX - 5 &&
            word.BoundingBox.Right <= t.MaxX + 5 &&
            word.BoundingBox.Bottom >= t.MinY - 5 &&
            word.BoundingBox.Top <= t.MaxY + 5);
    }

    #endregion

    #region Text Processing

    /// <summary>
    /// Merge page texts with intelligent sentence boundary handling.
    /// </summary>
    private static string MergePageTexts(List<PageContentData> pageContents)
    {
        if (pageContents.Count == 0) return "";
        if (pageContents.Count == 1) return NormalizeText(pageContents[0].Text);

        var result = new StringBuilder();

        for (int i = 0; i < pageContents.Count; i++)
        {
            var currentPage = pageContents[i];
            var normalizedText = NormalizeText(currentPage.Text);

            if (i == 0)
            {
                result.Append(normalizedText);
                continue;
            }

            var prevPage = pageContents[i - 1];

            if (prevPage.EndsWithIncompleteSentence && currentPage.StartsWithIncompleteSentence)
            {
                if (result.Length > 0 && !char.IsWhiteSpace(result[^1]))
                {
                    result.Append(' ');
                }
                result.Append(normalizedText);
            }
            else
            {
                result.AppendLine();
                result.AppendLine();
                result.Append(normalizedText);
            }
        }

        return result.ToString().Trim();
    }

    private static Dictionary<int, (int Start, int End)> BuildPageRanges(List<PageContentData> pageContents, string mergedText)
    {
        var ranges = new Dictionary<int, (int Start, int End)>();
        var currentPosition = 0;

        foreach (var page in pageContents)
        {
            var normalizedLength = NormalizeText(page.Text).Length;
            if (normalizedLength > 0)
            {
                ranges[page.PageNumber] = (currentPosition, currentPosition + normalizedLength - 1);
                currentPosition += normalizedLength + 2;
            }
        }

        return ranges;
    }

    private static bool IsIncompleteSentenceEnd(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.TrimEnd();
        if (trimmed.Length == 0) return false;

        var lastChar = trimmed[^1];
        if (lastChar is '.' or '!' or '?' or '。' or '！' or '？')
        {
            return false;
        }

        return IncompleteEndPattern.IsMatch(trimmed);
    }

    private static bool IsIncompleteSentenceStart(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.TrimStart();
        if (trimmed.Length == 0) return false;

        var firstChar = trimmed[0];
        if (char.IsLower(firstChar))
        {
            return true;
        }

        return IncompleteStartPattern.IsMatch(trimmed);
    }

    private static string FilterPageNumbers(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var lines = text.Split('\n');
        var filteredLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                filteredLines.Add(line);
                continue;
            }

            if (trimmedLine.Length < 20 && IsPageNumber(trimmedLine))
            {
                continue;
            }

            filteredLines.Add(line);
        }

        return string.Join('\n', filteredLines);
    }

    private static bool IsPageNumber(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        return PageNumberPatterns.IsMatch(line);
    }

    private static double EstimateLineHeight(IList<Word> words)
    {
        if (!words.Any()) return 12.0;

        var heights = words
            .Where(w => w.BoundingBox.Height > 0)
            .Select(w => w.BoundingBox.Height)
            .ToList();

        if (heights.Count == 0) return 12.0;

        heights.Sort();
        var medianHeight = heights[heights.Count / 2];

        return medianHeight * 1.3;
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        text = TextSanitizer.RemoveNullBytes(text);
        text = WhitespaceRegex().Replace(text, " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd();
        }

        text = string.Join('\n', lines);
        text = text.Trim();
        text = Regex.Replace(text, @"(\w+)-\s*\n\s*(\w+)", "$1$2");
        // Don't merge lines mid-sentence for plain text output
        // (markdown conversion will happen in Refine stage)

        return text;
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        return text
            .Split(separator, StringSplitOptions.RemoveEmptyEntries)
            .Length;
    }

    #endregion

    #region Image Extraction

    private static void ExtractImagesFromPage(Page page, int pageNum, List<ImageInfo> images, List<string> warnings, int? maxImageSize)
    {
        try
        {
            var pageImages = page.GetImages();

            foreach (var image in pageImages)
            {
                try
                {
                    var bounds = image.Bounds;
                    if (bounds.Width < MinImageDimension || bounds.Height < MinImageDimension)
                    {
                        continue;
                    }

                    byte[]? imageBytes = null;
                    string mimeType = "image/png";

                    if (image.TryGetPng(out var pngBytes) && pngBytes != null && pngBytes.Length > 0)
                    {
                        imageBytes = pngBytes;
                        mimeType = "image/png";
                    }
                    else
                    {
                        var rawBytes = image.RawBytes.ToArray();
                        if (rawBytes.Length > 0)
                        {
                            imageBytes = rawBytes;
                            mimeType = DetermineImageMimeType(rawBytes);
                        }
                    }

                    if (imageBytes == null || imageBytes.Length < MinImageDataSize)
                    {
                        continue;
                    }

                    // Check max size limit
                    if (maxImageSize.HasValue && imageBytes.Length > maxImageSize.Value)
                    {
                        warnings.Add($"Page {pageNum}: Image skipped (exceeds max size: {imageBytes.Length} > {maxImageSize.Value})");
                        continue;
                    }

                    var imageId = $"img_{images.Count:D3}";

                    images.Add(new ImageInfo
                    {
                        Id = imageId,
                        Data = imageBytes,
                        MimeType = mimeType,
                        Position = pageNum,
                        SourceUrl = $"embedded:{imageId}",
                        OriginalSize = imageBytes.Length,
                        Properties =
                        {
                            ["Width"] = (int)bounds.Width,
                            ["Height"] = (int)bounds.Height,
                            ["PageNumber"] = pageNum,
                            ["BoundsLeft"] = bounds.Left,
                            ["BoundsBottom"] = bounds.Bottom
                        }
                    });
                }
                catch (Exception ex)
                {
                    warnings.Add($"Page {pageNum}: Image extraction failed - {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Page {pageNum}: Image enumeration failed - {ex.Message}");
        }
    }

    private static string DetermineImageMimeType(byte[] bytes)
    {
        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xD8)
            {
                return "image/jpeg";
            }
            if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            {
                return "image/png";
            }
            if (bytes.Length >= 3 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            {
                return "image/gif";
            }
            if (bytes[0] == 0x42 && bytes[1] == 0x4D)
            {
                return "image/bmp";
            }
            if (bytes.Length >= 4 &&
                ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) ||
                 (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A)))
            {
                return "image/tiff";
            }
        }

        return "application/octet-stream";
    }

    #endregion

    #region Regex Patterns

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[a-zA-Z가-힣,;:\-]$")]
    private static partial Regex IncompleteSentenceEndRegex();

    [GeneratedRegex(@"^[a-z]|^[,;:\-]|^[가-힣](?![.!?。！？])")]
    private static partial Regex IncompleteSentenceStartRegex();

    [GeneratedRegex(@"^\s*(?:-\s*)?\d{1,4}(?:\s*-)?$|^(?:page|p\.?)\s*\d{1,4}(?:\s*(?:of|/)\s*\d{1,4})?\s*$|^(?:페이지|쪽)\s*\d{1,4}$|^\d{1,4}\s*/\s*\d{1,4}$|^[ivxlc]{1,4}$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PageNumberRegex();

    #endregion

    #region Bookmark Extraction

    /// <summary>
    /// Bookmark entry from PDF document outline/TOC.
    /// </summary>
    private sealed class BookmarkEntry
    {
        public string Title { get; init; } = string.Empty;
        public int PageNumber { get; init; }
        public int Level { get; init; } = 1;
    }

    /// <summary>
    /// Extracts bookmarks (outline/TOC) from PDF document.
    /// Bookmarks provide reliable heading information for enhanced heading detection.
    /// </summary>
    private static List<BookmarkEntry> ExtractBookmarks(PdfDocument document, List<string> warnings)
    {
        var bookmarks = new List<BookmarkEntry>();

        try
        {
            // Try to get bookmarks from document
            if (!document.TryGetBookmarks(out var pdfBookmarks) || pdfBookmarks == null)
            {
                return bookmarks;
            }

            // Recursively extract all bookmark entries
            ExtractBookmarkNodes(pdfBookmarks.GetNodes(), bookmarks, 1);
        }
        catch (Exception ex)
        {
            warnings.Add($"Bookmark extraction warning: {ex.Message}");
        }

        return bookmarks;
    }

    /// <summary>
    /// Recursively extracts bookmark nodes from PDF outline.
    /// </summary>
    private static void ExtractBookmarkNodes(
        IEnumerable<UglyToad.PdfPig.Outline.BookmarkNode> nodes,
        List<BookmarkEntry> bookmarks,
        int level)
    {
        foreach (var node in nodes)
        {
            // Get page number (0 if not available)
            var pageNumber = 0;
            if (node is UglyToad.PdfPig.Outline.DocumentBookmarkNode docNode)
            {
                pageNumber = docNode.PageNumber;
            }

            bookmarks.Add(new BookmarkEntry
            {
                Title = node.Title?.Trim() ?? string.Empty,
                PageNumber = pageNumber,
                Level = Math.Min(level, 6)
            });

            // Recursively process children
            if (node.Children.Any())
            {
                ExtractBookmarkNodes(node.Children, bookmarks, level + 1);
            }
        }
    }

    /// <summary>
    /// Applies bookmark hints to text blocks for enhanced heading detection.
    /// Matches bookmark titles to block content and sets heading type.
    /// </summary>
    private static void ApplyBookmarkHints(List<TextBlock> blocks, List<BookmarkEntry> bookmarks)
    {
        if (bookmarks.Count == 0 || blocks.Count == 0)
            return;

        // Create lookup by page for efficiency
        var bookmarksByPage = bookmarks
            .Where(b => b.PageNumber > 0)
            .GroupBy(b => b.PageNumber)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var block in blocks)
        {
            // Skip if already marked as heading
            if (block.Type == BlockType.Heading)
                continue;

            // Look for bookmark match on this page or adjacent pages
            var pagesToCheck = new[] { block.PageNumber, block.PageNumber - 1, block.PageNumber + 1 };

            foreach (var pageNum in pagesToCheck)
            {
                if (!bookmarksByPage.TryGetValue(pageNum, out var pageBookmarks))
                    continue;

                foreach (var bookmark in pageBookmarks)
                {
                    if (IsBookmarkMatch(block.Content, bookmark.Title))
                    {
                        block.Type = BlockType.Heading;
                        block.HeadingLevel = bookmark.Level;
                        break;
                    }
                }

                if (block.Type == BlockType.Heading)
                    break;
            }
        }
    }

    /// <summary>
    /// Checks if block content matches a bookmark title.
    /// Uses fuzzy matching to handle minor formatting differences.
    /// </summary>
    private static bool IsBookmarkMatch(string blockContent, string bookmarkTitle)
    {
        if (string.IsNullOrWhiteSpace(blockContent) || string.IsNullOrWhiteSpace(bookmarkTitle))
            return false;

        // Normalize both strings
        var normalizedBlock = NormalizeForComparison(blockContent);
        var normalizedBookmark = NormalizeForComparison(bookmarkTitle);

        // Exact match
        if (normalizedBlock.Equals(normalizedBookmark, StringComparison.OrdinalIgnoreCase))
            return true;

        // Block starts with bookmark (common: "1. Introduction" vs "Introduction")
        if (normalizedBlock.StartsWith(normalizedBookmark, StringComparison.OrdinalIgnoreCase) ||
            normalizedBlock.EndsWith(normalizedBookmark, StringComparison.OrdinalIgnoreCase))
            return true;

        // Bookmark starts with block (common: bookmark "Chapter 1: Introduction", block "Introduction")
        if (normalizedBookmark.StartsWith(normalizedBlock, StringComparison.OrdinalIgnoreCase) ||
            normalizedBookmark.EndsWith(normalizedBlock, StringComparison.OrdinalIgnoreCase))
            return true;

        // Levenshtein-like similarity for short titles (handles OCR errors, minor typos)
        if (normalizedBlock.Length < 50 && normalizedBookmark.Length < 50)
        {
            var similarity = CalculateSimilarity(normalizedBlock, normalizedBookmark);
            if (similarity > 0.85)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Normalizes text for comparison by removing extra whitespace and common prefixes.
    /// </summary>
    private static string NormalizeForComparison(string text)
    {
        // Remove leading numbers and punctuation (e.g., "1.", "1.1", "Chapter 1:")
        var normalized = Regex.Replace(text.Trim(), @"^[\d.]+\s*", "");
        normalized = Regex.Replace(normalized, @"^(chapter|section|part)\s*[\d.:]+\s*", "", RegexOptions.IgnoreCase);

        // Collapse whitespace
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

        return normalized;
    }

    /// <summary>
    /// Calculates similarity ratio between two strings (0 to 1).
    /// Uses simple character-based similarity for efficiency.
    /// </summary>
    private static double CalculateSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return 0;

        var longer = a.Length > b.Length ? a.ToLowerInvariant() : b.ToLowerInvariant();
        var shorter = a.Length > b.Length ? b.ToLowerInvariant() : a.ToLowerInvariant();

        // Simple containment check
        if (longer.Contains(shorter))
            return (double)shorter.Length / longer.Length;

        // Character overlap
        var overlap = shorter.Count(c => longer.Contains(c));
        return (double)overlap / longer.Length;
    }

    #endregion

    #region Internal Classes

    /// <summary>
    /// Internal representation of page content during extraction.
    /// </summary>
    private sealed class PageContentData
    {
        public int PageNumber { get; set; }
        public string Text { get; set; } = "";
        public List<TextBlock> Blocks { get; set; } = [];
        public List<TableData> Tables { get; set; } = [];
        public bool EndsWithIncompleteSentence { get; set; }
        public bool StartsWithIncompleteSentence { get; set; }
        public bool HasContent => !string.IsNullOrWhiteSpace(Text) || Blocks.Count > 0 || Tables.Count > 0;
    }

    /// <summary>
    /// Internal representation of a detected table region.
    /// Contains raw cell data without markdown conversion.
    /// </summary>
    private sealed class TableRegion
    {
        public double MinX { get; set; }
        public double MaxX { get; set; }
        public double MinY { get; set; }
        public double MaxY { get; set; }
        public double TopY { get; set; }
        /// <summary>
        /// Raw cell data: Cells[row][column]
        /// </summary>
        public string[][] Cells { get; set; } = [];
        public string PlainTextFallback { get; set; } = "";
        public double ConfidenceScore { get; set; } = 1.0;
        public int RowCount { get; set; }
        public int ColumnCount { get; set; }
    }

    #endregion
}
