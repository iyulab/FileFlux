using FileFlux.Core;
using FileFlux.Infrastructure.Adapters;
using FluxCurator.Core;
using FluxCurator.Core.Core;
using FluxCurator.Core.Infrastructure.Refining;
using FluxImprover;
using FluxImprover.ContextualRetrieval;
using FluxImprover.Enrichment;
using FluxImprover.Models;
using Microsoft.Extensions.Logging;
using FluxCuratorStrategy = FluxCurator.Core.Domain.ChunkingStrategy;
using FluxCuratorChunkOptions = FluxCurator.Core.Domain.ChunkOptions;
using TextRefineOptions = FluxCurator.Core.Domain.TextRefineOptions;

namespace FileFlux.Infrastructure;

/// <summary>
/// Legacy document processor that delegates chunking to FluxCurator and enhancement to FluxImprover.
/// </summary>
/// <remarks>
/// This is a legacy implementation kept for backward compatibility with CLI commands.
/// For new code, use <see cref="IDocumentProcessor"/> via <see cref="DocumentProcessorFactory"/>.
/// </remarks>
public sealed partial class FluxDocumentProcessor
{
    private readonly IDocumentReaderFactory _readerFactory;
    private readonly IDocumentParserFactory _parserFactory;
    private readonly IChunkerFactory _chunkerFactory;
    private readonly FluxImproverServices? _improverServices;
    private readonly ITextRefiner _textRefiner;
    private readonly ILogger<FluxDocumentProcessor> _logger;
    private readonly IMarkdownConverter? _markdownConverter;
    private readonly IImageToTextService? _imageToTextService;

    /// <summary>
    /// Creates a new FluxDocumentProcessor with required dependencies.
    /// </summary>
    /// <param name="readerFactory">Factory for document readers (from FileFlux.Core)</param>
    /// <param name="parserFactory">Factory for document parsers</param>
    /// <param name="chunkerFactory">Factory for chunkers (from FluxCurator)</param>
    /// <param name="improverServices">FluxImprover services for enhancement (optional)</param>
    /// <param name="logger">Logger instance</param>
    public FluxDocumentProcessor(
        IDocumentReaderFactory readerFactory,
        IDocumentParserFactory parserFactory,
        IChunkerFactory chunkerFactory,
        FluxImproverServices? improverServices = null,
        IMarkdownConverter? markdownConverter = null,
        IImageToTextService? imageToTextService = null,
        ILogger<FluxDocumentProcessor>? logger = null)
    {
        _readerFactory = readerFactory ?? throw new ArgumentNullException(nameof(readerFactory));
        _parserFactory = parserFactory ?? throw new ArgumentNullException(nameof(parserFactory));
        _chunkerFactory = chunkerFactory ?? throw new ArgumentNullException(nameof(chunkerFactory));
        _improverServices = improverServices;
        _markdownConverter = markdownConverter;
        _imageToTextService = imageToTextService;
        _textRefiner = new TextRefiner();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FluxDocumentProcessor>.Instance;
    }

    #region Full Pipeline

    /// <inheritdoc/>
    public async Task<DocumentChunk[]> ProcessAsync(
        string filePath,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFilePath(filePath);
        options ??= new ChunkingOptions();

        _logger.LogDebug("Processing file: {FilePath}", filePath);

        // Stage 1: Extract (FileFlux.Core)
        var rawContent = await ExtractAsync(filePath, cancellationToken).ConfigureAwait(false);

        // Stage 2: Parse
        var parsedContent = await ParseAsync(rawContent, null, cancellationToken).ConfigureAwait(false);

        // Stage 3: Refine (optional - when RefiningOptions is set)
        if (options.RefiningOptions != null)
        {
            // Pass RawContent via Extra for IMarkdownConverter usage
            options.RefiningOptions.Extra["_rawContent"] = rawContent;
            parsedContent = await RefineAsync(parsedContent, options.RefiningOptions, cancellationToken).ConfigureAwait(false);
        }

        // Stage 4: Chunk (FluxCurator)
        // Pass PageRanges for page number calculation (from PDF extraction)
        if (rawContent.Hints.TryGetValue("PageRanges", out var pageRangesObj) && pageRangesObj is Dictionary<int, (int Start, int End)> pageRanges)
        {
            options.CustomProperties["_pageRanges"] = pageRanges;
        }
        var chunks = await ChunkAsync(parsedContent, options, cancellationToken).ConfigureAwait(false);

        // Stage 5: Enhance (FluxImprover) - if available and enabled
        if (_improverServices != null && ShouldEnhance(options))
        {
            chunks = await EnhanceChunksAsync(chunks, rawContent.Text, options, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Processed {FileName}: {ChunkCount} chunks", rawContent.File.Name, chunks.Length);
        return chunks;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<DocumentChunk> ProcessStreamAsync(
        string filePath,
        ChunkingOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chunks = await ProcessAsync(filePath, options, cancellationToken).ConfigureAwait(false);
        foreach (var chunk in chunks)
        {
            yield return chunk;
        }
    }

    #endregion

    #region Stage 1: Extract (FileFlux.Core)

    /// <inheritdoc/>
    public async Task<RawContent> ExtractAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ValidateFilePath(filePath);

        _logger.LogDebug("Extracting content from: {FilePath}", filePath);

        try
        {
            var reader = _readerFactory.GetReader(filePath)
                ?? throw new UnsupportedFileFormatException(filePath, $"No reader found for: {filePath}");

            var rawContent = await reader.ExtractAsync(filePath, null, cancellationToken).ConfigureAwait(false);

            LogWarnings("Extraction", rawContent.Warnings);
            return rawContent;
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Extraction failed: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<RawContent> ExtractStreamAsync(
        string filePath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return await ExtractAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Stage 2: Parse

    /// <inheritdoc/>
    public async Task<ParsedContent> ParseAsync(
        RawContent raw,
        ParsingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(raw);
        options ??= new ParsingOptions();

        _logger.LogDebug("Parsing document: {FileName}", raw.File.Name);

        try
        {
            var parser = _parserFactory.GetParser(raw);
            var parsed = await parser.ParseAsync(raw, new DocumentParsingOptions
            {
                UseLlmParsing = options.UseLlm,
                StructuringLevel = StructuringLevel.Medium
            }, cancellationToken).ConfigureAwait(false);

            LogWarnings("Parsing", parsed.Info.Warnings);
            return parsed;
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(raw.File.Name, $"Parsing failed: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ParsedContent> ParseStreamAsync(
        RawContent raw,
        ParsingOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return await ParseAsync(raw, options, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Stage 2.5: Refine

    /// <inheritdoc/>
    public async Task<ParsedContent> RefineAsync(
        ParsedContent parsed,
        RefiningOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        options ??= new RefiningOptions();

        _logger.LogDebug("Refining document: {FileName}", parsed.Metadata.FileName);

        try
        {
            var refinedText = parsed.Text;

            // Stage 1: Markdown conversion (if enabled and IMarkdownConverter available)
            if (options.ConvertToMarkdown && _markdownConverter != null)
            {
                // Get RawContent from Extra if provided (for full conversion support)
                if (options.Extra.TryGetValue("_rawContent", out var rawObj) && rawObj is RawContent rawContent)
                {
                    var markdownResult = await _markdownConverter.ConvertAsync(rawContent, new MarkdownConversionOptions
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
                        _logger.LogDebug("Converted to Markdown: {Method}, {HeadingCount} headings, {TableCount} tables",
                            markdownResult.Method, markdownResult.Statistics.HeadingCount, markdownResult.Statistics.TableCount);
                    }
                    else
                    {
                        _logger.LogWarning("Markdown conversion failed, using original text");
                    }
                }
                else
                {
                    _logger.LogDebug("ConvertToMarkdown enabled but RawContent not provided in Extra, skipping Markdown conversion");
                }
            }

            // Stage 2: Image-to-text processing (if enabled and IImageToTextService available)
            if (options.ProcessImagesToText && _imageToTextService != null)
            {
                // Get RawContent for image data
                if (options.Extra.TryGetValue("_rawContent", out var rawObj) && rawObj is RawContent rawContent && rawContent.Images.Count > 0)
                {
                    refinedText = await ProcessImagesToTextAsync(refinedText, rawContent.Images, cancellationToken).ConfigureAwait(false);
                }
            }

            // Stage 3: Document-level refinement (FileFlux-specific)
            // Remove artificial paragraph headings (# Paragraph N)
            refinedText = RemoveArtificialParagraphHeadings(refinedText);

            // Remove image placeholders like [그림], [Figure], [Image] (only if not processing images)
            if (!options.ProcessImagesToText)
            {
                refinedText = RemoveImagePlaceholders(refinedText);
            }

            // Clean whitespace
            if (options.CleanWhitespace)
            {
                refinedText = CleanWhitespace(refinedText);
            }

            // Remove headers/footers patterns
            if (options.RemoveHeadersFooters)
            {
                refinedText = RemoveHeadersFooters(refinedText);
            }

            // Remove page numbers
            if (options.RemovePageNumbers)
            {
                refinedText = RemovePageNumbers(refinedText);
            }

            // Remove TOC noise (dot/dash leaders)
            refinedText = RemoveTocNoise(refinedText);

            // Restructure headings
            if (options.RestructureHeadings)
            {
                refinedText = RestructureHeadings(refinedText);
            }

            // Stage 4: Text-level refinement (delegated to FluxCurator)
            // Handles: empty bullets, duplicate lines, custom patterns, etc.
            var textRefineOptions = MapTextRefinementPreset(options.TextRefinementPreset);
            refinedText = _textRefiner.Refine(refinedText, textRefineOptions);

            // Create refined parsed content
            var refined = new ParsedContent
            {
                Text = refinedText,
                Metadata = parsed.Metadata,
                Structure = parsed.Structure,
                Info = parsed.Info
            };

            _logger.LogDebug("Refined document: {OriginalLength} -> {RefinedLength} chars",
                parsed.Text.Length, refinedText.Length);

            return refined;
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(parsed.Metadata.FileName, $"Refining failed: {ex.Message}", ex);
        }
    }

    private static string RemoveArtificialParagraphHeadings(string text)
    {
        // Remove "# Paragraph N" headings (auto-generated, not meaningful structure)
        // Matches: # Paragraph 1, ## Paragraph 2, ### Paragraph 10, etc.
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"^#{1,6}\s*Paragraph\s+\d+\s*$",
            "",
            System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Clean up resulting empty lines (but keep paragraph spacing)
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.Trim();
    }

    private static string RemoveImagePlaceholders(string text)
    {
        // Remove common image placeholder patterns
        // Matches: [그림], [그림] 설명, [Figure], [Image], [Image: description]
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"^\[(?:그림|Figure|Image|이미지|사진|도표|표)\].*$",
            "",
            System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Clean up resulting empty lines
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.Trim();
    }

    private static string CleanWhitespace(string text)
    {
        // Replace multiple newlines with double newline
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");

        // Replace multiple spaces with single space
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[ \t]{2,}", " ");

        // Trim lines
        var lines = text.Split('\n').Select(l => l.Trim());
        return string.Join("\n", lines);
    }

    private static string RemoveHeadersFooters(string text)
    {
        var lines = text.Split('\n').ToList();
        var result = new List<string>();

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();

            // Skip common header/footer patterns
            if (IsHeaderFooterLine(line))
                continue;

            result.Add(lines[i]);
        }

        return string.Join("\n", result);
    }

    private static bool IsHeaderFooterLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        // Common header/footer patterns for various document types
        var patterns = new[]
        {
            // Page numbering patterns
            @"^Page\s+\d+\s*(of\s+\d+)?$",
            @"^\d+\s*/\s*\d+$",
            @"^-\s*\d+\s*-$",  // - 1 -
            @"^\[\s*\d+\s*\]$",  // [ 1 ]
            @"^페이지\s*\d+",  // Korean: 페이지 1
            @"^\d+\s*페이지$",  // Korean: 1 페이지

            // Copyright and legal patterns
            @"^(CONFIDENTIAL|DRAFT|INTERNAL|PROPRIETARY|SECRET)$",
            @"^©\s*\d{4}",
            @"^Copyright\s+",
            @"^All [Rr]ights [Rr]eserved",

            // Document header patterns
            @"^(Version|Rev\.|Revision)\s*[\d\.]+$",
            @"^\d{4}[-/]\d{2}[-/]\d{2}$",  // Date alone: 2024-01-01
            @"^(Document|Doc)\s*(ID|#|No\.?):\s*",

            // Korean document patterns
            @"^#\s*댓글\s*$",
            @"^-\s*댓글\s*\d+\s*개\s*$",
            @"^(주)|(주식회사)\s*\S+$",  // Company name as header

            // Repeated separator patterns (often headers/footers)
            @"^[-_=]{3,}\s*$",
            @"^[─━═]{3,}\s*$",  // Unicode line characters
        };

        foreach (var pattern in patterns)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(line, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return true;
        }

        // Check for very short lines that are likely headers (e.g., company name, document title repeated)
        if (line.Length < 30 && IsLikelyRepeatedHeader(line))
            return true;

        return false;
    }

    /// <summary>
    /// Check if a short line is likely a repeated header (document title, company name, etc.)
    /// </summary>
    private static bool IsLikelyRepeatedHeader(string line)
    {
        // Lines that are all uppercase and short are often headers
        if (line.Length < 20 && line == line.ToUpperInvariant() && !line.Any(char.IsDigit) && line.Length > 2)
            return true;

        return false;
    }

    /// <summary>
    /// Remove Table of Contents noise patterns (dots, dashes used for page alignment).
    /// </summary>
    private static string RemoveTocNoise(string text)
    {
        // Remove TOC-style dot leaders: "Chapter 1 .............. 5"
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"\.{3,}\s*\d+\s*$",
            "",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        // Remove dash leaders: "Section 1 ------ 10"
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"-{3,}\s*\d+\s*$",
            "",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        // Remove Korean TOC patterns: "제1장 ··············· 5"
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"[·•]{3,}\s*\d+\s*$",
            "",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        // Remove underscore leaders: "Introduction ___________ 3"
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"_{3,}\s*\d+\s*$",
            "",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        return text;
    }

    private static string RemovePageNumbers(string text)
    {
        // Remove standalone page numbers (lines with just numbers)
        var lines = text.Split('\n');
        var result = lines.Where(l =>
        {
            var trimmed = l.Trim();
            // Skip lines that are just numbers (page numbers)
            return !System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^-?\s*\d+\s*-?$");
        });

        return string.Join("\n", result);
    }

    // NOTE: RemoveEmptyBulletPoints and RemoveDuplicateLines removed.
    // These are now handled by FluxCurator's TextRefiner via RefiningOptions.TextRefinementPreset.

    /// <summary>
    /// Maps preset name string to FluxCurator TextRefineOptions.
    /// </summary>
    private static TextRefineOptions MapTextRefinementPreset(string presetName)
    {
        return presetName?.ToUpperInvariant() switch
        {
            "NONE" => TextRefineOptions.None,
            "LIGHT" => TextRefineOptions.Light,
            "STANDARD" => TextRefineOptions.Standard,
            "FORWEBCONTENT" => TextRefineOptions.ForWebContent,
            "FORKOREAN" => TextRefineOptions.ForKorean,
            "FORPDFCONTENT" => TextRefineOptions.ForPdfContent,
            _ => TextRefineOptions.Light // Default fallback
        };
    }

    private static string RestructureHeadings(string text)
    {
        var lines = text.Split('\n').ToList();
        var result = new List<string>();

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // Detect potential headings (short lines followed by longer content)
            if (trimmed.Length > 0 && trimmed.Length < 100 &&
                !trimmed.EndsWith('.') && !trimmed.EndsWith(',') &&
                i + 1 < lines.Count && lines[i + 1].Trim().Length > trimmed.Length)
            {
                // Check if it looks like a heading (starts with number or capital)
                if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^(\d+[\.\)]\s*)?[A-Z]"))
                {
                    // Ensure blank line before heading (if not first line and prev isn't blank)
                    if (result.Count > 0 && !string.IsNullOrWhiteSpace(result[^1]))
                    {
                        result.Add("");
                    }
                }
            }

            result.Add(line);
        }

        return string.Join("\n", result);
    }

    /// <summary>
    /// Process images in the document and replace placeholders with extracted text.
    /// </summary>
    private async Task<string> ProcessImagesToTextAsync(
        string text,
        IReadOnlyList<Core.ImageInfo> images,
        CancellationToken cancellationToken)
    {
        if (_imageToTextService == null || images.Count == 0)
            return text;

        var result = text;

        foreach (var image in images)
        {
            if (image.Data == null || image.Data.Length == 0)
                continue;

            try
            {
                var extractionResult = await _imageToTextService.ExtractTextAsync(
                    image.Data,
                    new ImageToTextOptions
                    {
                        Language = "auto",
                        Quality = "medium",
                        ExtractStructure = true
                    },
                    cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(extractionResult.ExtractedText))
                {
                    // Replace image placeholder with extracted text
                    // Common patterns: ![alt](embedded:img_001), [Image: img_001], etc.
                    var placeholder = $"embedded:{image.Id}";
                    var replacement = $"\n\n[Image Content: {extractionResult.ExtractedText.Trim()}]\n\n";

                    result = result.Replace(placeholder, replacement);

                    _logger.LogDebug("Processed image {ImageId}: extracted {CharCount} chars",
                        image.Id, extractionResult.ExtractedText.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract text from image {ImageId}", image.Id);
            }
        }

        return result;
    }

    #endregion

    #region Stage 3: Chunk (FluxCurator delegation)

    /// <inheritdoc/>
    public async Task<DocumentChunk[]> ChunkAsync(
        ParsedContent parsed,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        options ??= new ChunkingOptions();

        if (string.IsNullOrWhiteSpace(parsed.Text))
            return [];

        _logger.LogDebug("Chunking with strategy: {Strategy}", options.Strategy);

        try
        {
            // Map FileFlux strategy to FluxCurator strategy
            var fcStrategy = MapToFluxCuratorStrategy(options.Strategy);
            var chunker = _chunkerFactory.CreateChunker(fcStrategy);

            // Convert options
            var fcOptions = new FluxCuratorChunkOptions
            {
                MaxChunkSize = options.MaxChunkSize,
                MinChunkSize = options.MinChunkSize,
                OverlapSize = options.OverlapSize,
                TargetChunkSize = options.MaxChunkSize / 2,
                LanguageCode = options.LanguageCode == "auto" ? null : options.LanguageCode,
                PreserveParagraphs = options.PreserveParagraphs,
                PreserveSentences = options.PreserveSentences,
                PreserveSectionHeaders = true,
                IncludeMetadata = true,
                TrimWhitespace = true,
                EnableChunkBalancing = options.EnableChunkBalancing
            };

            // Execute chunking via FluxCurator
            var fcChunks = await chunker.ChunkAsync(parsed.Text, fcOptions, cancellationToken).ConfigureAwait(false);

            // Convert to FileFlux chunks
            var parsedId = Guid.NewGuid();
            var rawId = Guid.NewGuid();
            var chunks = fcChunks.ToFileFluxChunks(parsedId, rawId);

            // Enrich with document metadata and page numbers
            // Extract PageRanges from options if available (passed from ProcessAsync)
            Dictionary<int, (int Start, int End)>? pageRanges = null;
            if (options.CustomProperties.TryGetValue("_pageRanges", out var prObj) && prObj is Dictionary<int, (int Start, int End)> pr)
            {
                pageRanges = pr;
            }
            EnrichChunksWithMetadata(chunks, parsed, pageRanges);

            _logger.LogDebug("Created {Count} chunks using {Strategy}", chunks.Count, chunker.StrategyName);
            return [.. chunks];
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(parsed.Metadata.FileName, $"Chunking failed: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<DocumentChunk> ChunkStreamAsync(
        ParsedContent parsed,
        ChunkingOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chunks = await ChunkAsync(parsed, options, cancellationToken).ConfigureAwait(false);
        foreach (var chunk in chunks)
        {
            yield return chunk;
        }
    }

    #endregion

    #region Stage 4: Enhance (FluxImprover delegation)

    /// <summary>
    /// Enhance chunks using FluxImprover services.
    /// </summary>
    private async Task<DocumentChunk[]> EnhanceChunksAsync(
        DocumentChunk[] chunks,
        string fullDocumentText,
        ChunkingOptions options,
        CancellationToken cancellationToken)
    {
        if (_improverServices == null || chunks.Length == 0)
            return chunks;

        _logger.LogDebug("Enhancing {Count} chunks with FluxImprover", chunks.Length);

        try
        {
            // Convert to FluxImprover Chunk format
            var improverChunks = chunks.Select(c => new Chunk
            {
                Id = c.Id.ToString(),
                Content = c.Content,
                Metadata = c.Props
            }).ToList();

            // Apply contextual enrichment (Anthropic pattern)
            if (ShouldUseContextualRetrieval(options))
            {
                var contextualChunks = await _improverServices.ContextualEnrichment
                    .EnrichBatchAsync(improverChunks, fullDocumentText, null, cancellationToken)
                    .ConfigureAwait(false);

                // Merge contextual info back to chunks
                for (int i = 0; i < chunks.Length && i < contextualChunks.Count; i++)
                {
                    var contextSummary = contextualChunks[i].ContextSummary;
                    if (contextSummary != null)
                        chunks[i].Props[ChunkPropsKeys.EnrichedSummary] = contextSummary;
                    chunks[i].Props[ChunkPropsKeys.EnrichedContextualText] = contextualChunks[i].GetContextualizedText();
                }
            }

            // Apply keyword extraction and summarization
            if (ShouldEnrichWithKeywords(options))
            {
                // Build enrichment options with conditional enrichment support
                var enrichmentOptions = new FluxImprover.Options.EnrichmentOptions();

                // Enable conditional enrichment if configured
                if (options.EnableConditionalEnrichment)
                {
                    enrichmentOptions = new FluxImprover.Options.EnrichmentOptions
                    {
                        ConditionalOptions = new FluxImprover.Options.ConditionalEnrichmentOptions
                        {
                            EnableConditionalEnrichment = true,
                            SkipEnrichmentThreshold = options.ConditionalEnrichmentThreshold,
                            MinSummarizationLength = options.MinSummarizationLength,
                            IncludeQualityMetrics = true
                        }
                    };
                    _logger.LogDebug("Using conditional enrichment with threshold {Threshold}", options.ConditionalEnrichmentThreshold);
                }

                var enrichedChunks = await _improverServices.ChunkEnrichment
                    .EnrichBatchAsync(improverChunks, enrichmentOptions, cancellationToken)
                    .ConfigureAwait(false);

                for (int i = 0; i < chunks.Length && i < enrichedChunks.Count; i++)
                {
                    var summary = enrichedChunks[i].Summary;
                    if (summary != null)
                        chunks[i].Props[ChunkPropsKeys.EnrichedSummary] = summary;
                    var keywords = enrichedChunks[i].Keywords;
                    if (keywords != null)
                        chunks[i].Props[ChunkPropsKeys.EnrichedKeywords] = keywords;

                    // Include quality metrics if available
                    if (enrichedChunks[i].Metadata?.TryGetValue("quality_score", out var qualityScore) == true)
                        chunks[i].Props[ChunkPropsKeys.QualityScore] = qualityScore;
                    if (enrichedChunks[i].Metadata?.TryGetValue("was_skipped", out var wasSkipped) == true)
                        chunks[i].Props[ChunkPropsKeys.EnrichmentSkipped] = wasSkipped;
                }
            }

            _logger.LogDebug("Enhancement completed for {Count} chunks", chunks.Length);
            return chunks;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Enhancement failed, returning original chunks");
            return chunks;
        }
    }

    #endregion

    #region Helper Methods

    private static FluxCuratorStrategy MapToFluxCuratorStrategy(string strategy)
    {
        return strategy?.ToLowerInvariant() switch
        {
            "auto" => FluxCuratorStrategy.Auto,
            "sentence" => FluxCuratorStrategy.Sentence,
            "paragraph" => FluxCuratorStrategy.Paragraph,
            "token" => FluxCuratorStrategy.Token,
            "semantic" => FluxCuratorStrategy.Semantic,
            "hierarchical" => FluxCuratorStrategy.Hierarchical,
            // Legacy mappings
            "smart" => FluxCuratorStrategy.Sentence,
            "intelligent" => FluxCuratorStrategy.Semantic,
            "fixedsize" => FluxCuratorStrategy.Token,
            "pagelevel" => FluxCuratorStrategy.Paragraph,
            _ => FluxCuratorStrategy.Auto
        };
    }

    private static void EnrichChunksWithMetadata(
        IReadOnlyList<DocumentChunk> chunks,
        ParsedContent parsed,
        Dictionary<int, (int Start, int End)>? pageRanges = null)
    {
        // Build flattened section list for heading path calculation
        var allSections = FlattenSections(parsed.Structure.Sections);

        foreach (var chunk in chunks)
        {
            chunk.SourceInfo.Title = parsed.Metadata.Title ?? parsed.Metadata.FileName;
            chunk.SourceInfo.SourceType = parsed.Metadata.FileType ?? "unknown";
            chunk.SourceInfo.FilePath = parsed.Metadata.FileName;
            chunk.SourceInfo.ChunkCount = chunks.Count;

            // Add structure info using standard keys
            if (!string.IsNullOrEmpty(parsed.Structure.Topic))
                chunk.Props[ChunkPropsKeys.DocumentTopic] = parsed.Structure.Topic;
            if (parsed.Structure.Keywords.Count > 0)
                chunk.Props[ChunkPropsKeys.DocumentKeywords] = parsed.Structure.Keywords;

            // Calculate heading path based on chunk position
            var headingPath = CalculateHeadingPath(allSections, chunk.Location.StartChar, chunk.Location.EndChar);
            if (headingPath.Count > 0)
            {
                chunk.Location.HeadingPath = headingPath;
                chunk.Props[ChunkPropsKeys.HierarchyPath] = string.Join(" > ", headingPath);
            }

            // Calculate page numbers based on PageRanges (for PDF documents)
            if (pageRanges != null && pageRanges.Count > 0)
            {
                var (startPage, endPage) = CalculatePageNumbers(pageRanges, chunk.Location.StartChar, chunk.Location.EndChar);
                if (startPage.HasValue)
                {
                    chunk.Location.StartPage = startPage;
                    chunk.Location.EndPage = endPage ?? startPage;
                }
            }
        }
    }

    /// <summary>
    /// Calculate page numbers for a chunk based on its character position and PageRanges.
    /// </summary>
    /// <param name="pageRanges">Dictionary mapping page number (1-based) to character range</param>
    /// <param name="startChar">Chunk start character position</param>
    /// <param name="endChar">Chunk end character position</param>
    /// <returns>Tuple of (startPage, endPage), both 1-based</returns>
    private static (int? StartPage, int? EndPage) CalculatePageNumbers(
        Dictionary<int, (int Start, int End)> pageRanges,
        int startChar,
        int endChar)
    {
        int? startPage = null;
        int? endPage = null;

        // Find pages that contain the chunk's start and end positions
        foreach (var (pageNum, range) in pageRanges)
        {
            // Check if this page contains the start position
            if (startPage == null && startChar >= range.Start && startChar <= range.End)
            {
                startPage = pageNum;
            }

            // Check if this page contains the end position
            if (endChar >= range.Start && endChar <= range.End)
            {
                endPage = pageNum;
            }

            // Early exit if both found
            if (startPage.HasValue && endPage.HasValue)
                break;
        }

        // Fallback: find closest page if exact match not found
        if (startPage == null && pageRanges.Count > 0)
        {
            // Find the page whose range is closest to startChar
            startPage = pageRanges
                .OrderBy(p => Math.Min(Math.Abs(p.Value.Start - startChar), Math.Abs(p.Value.End - startChar)))
                .First().Key;
        }

        if (endPage == null && startPage.HasValue)
        {
            endPage = startPage;
        }

        return (startPage, endPage);
    }

    /// <summary>
    /// Flatten nested sections into a single list for efficient lookup
    /// </summary>
    private static List<Section> FlattenSections(List<Section> sections)
    {
        var result = new List<Section>();
        foreach (var section in sections)
        {
            result.Add(section);
            if (section.Children.Count > 0)
            {
                result.AddRange(FlattenSections(section.Children));
            }
        }
        return result;
    }

    /// <summary>
    /// Calculate heading path for a chunk based on its character position
    /// </summary>
    private static List<string> CalculateHeadingPath(List<Section> sections, int startChar, int endChar)
    {
        // Find all sections that contain this chunk's start position
        var containingSections = sections
            .Where(s => s.Start <= startChar && s.End >= startChar && !string.IsNullOrEmpty(s.Title))
            .OrderBy(s => s.Level)
            .ThenBy(s => s.Start)
            .ToList();

        return containingSections.Select(s => s.Title).ToList();
    }

    private static bool ShouldEnhance(ChunkingOptions options)
    {
        return options.CustomProperties.TryGetValue("enableEnhancement", out var val) && val is true;
    }

    private static bool ShouldUseContextualRetrieval(ChunkingOptions options)
    {
        return options.CustomProperties.TryGetValue("useContextualRetrieval", out var val) && val is true;
    }

    private static bool ShouldEnrichWithKeywords(ChunkingOptions options)
    {
        return options.CustomProperties.TryGetValue("enrichKeywords", out var val) && val is true;
    }

    private static void ValidateFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");
    }

    private void LogWarnings(string stage, IReadOnlyList<string> warnings)
    {
        foreach (var warning in warnings)
        {
            _logger.LogWarning("{Stage} warning: {Warning}", stage, warning);
        }
    }

    #endregion
}
