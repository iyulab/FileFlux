using FileFlux.Core;
using System.Text.RegularExpressions;
using Unpdf;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// PDF document reader using Unpdf (Rust FFI).
/// High-performance native library for PDF content extraction to Markdown.
/// </summary>
public partial class PdfDocumentReader : IDocumentReader
{
    public string ReaderType => "PdfReader";

    public IEnumerable<string> SupportedExtensions => [".pdf"];

    /// <summary>
    /// Diagnostic key naming the Unpdf error kind behind a failed extraction.
    /// The exception path has no <see cref="RawContent"/> to hang hints on and
    /// consumers persist only <see cref="Exception.Message"/>, so on that path the
    /// kind travels as a producer-emitted <c>key=value</c> token inside the message.
    /// On the partial path it is a real hint. Complements
    /// <c>extraction_failure_reason</c>, which explains the non-exception outcomes.
    /// </summary>
    internal const string ErrorKindKey = "extraction_error_kind";

    /// <summary>
    /// Diagnostic key stating that pages are missing from an extraction that otherwise
    /// succeeded. A damaged document whose page tree only partly resolves still parses,
    /// so without this flag a two-thirds document is indistinguishable from a whole one.
    /// It is a boolean, never a count: one unresolved node can cost a single page or an
    /// entire subtree, so the number of lost pages is not knowable here.
    /// </summary>
    internal const string PagesIncompleteKey = "pages_incomplete";

    /// <summary>
    /// Page count the document itself declares, published alongside
    /// <see cref="PagesIncompleteKey"/> so consumers can see how far the extracted
    /// <c>page_count</c> falls short without the reader asserting a loss figure.
    /// </summary>
    internal const string DeclaredPageCountKey = "declared_page_count";

    /// <summary>
    /// Diagnostic key stating how many text runs the font decoder could not resolve and
    /// discarded (Unpdf 0.12.0 <c>ExtractionQuality.SuppressedTextRuns</c>). A run is one
    /// text string handed to the decoder (a <c>Tj</c> operand); the decoder drops it rather
    /// than emit mojibake when the font's character codes cannot be mapped, so the content
    /// is gone from the output with no exception raised. Never folded into
    /// <c>extraction_failure_reason=no_text_layer</c>: that reason's wording asserts "OCR
    /// required", which is wrong for this case — the document is not scanned, it lost text
    /// at decode time.
    /// </summary>
    internal const string SuppressedTextRunsKey = "suppressed_text_runs";

    /// <summary>
    /// Formats an Unpdf error kind for diagnostics. Deliberately
    /// <see cref="Enum.ToString()"/> and not <see cref="Enum.GetName(Type, object)"/>:
    /// Unpdf's ABI assigns new reasons new numbers and never reuses old ones, so a
    /// value minted by a newer native build must round-trip as its number rather
    /// than collapse to null.
    /// </summary>
    internal static string FormatErrorKind(UnpdfErrorKind kind) => kind.ToString();

    private static string WithErrorKind(string message, UnpdfErrorKind kind)
        => $"{message} [{ErrorKindKey}={FormatErrorKind(kind)}]";

    /// <summary>
    /// Joins the distinct kinds observed across failed pages. Mixed causes stay
    /// visible (<c>PdfParse+MissingObject</c>) instead of being flattened to one label.
    /// </summary>
    internal static string SummarizeErrorKinds(IEnumerable<UnpdfErrorKind> kinds)
    {
        var distinct = kinds.Select(FormatErrorKind).Distinct(StringComparer.Ordinal).ToArray();
        return distinct.Length == 0
            ? FormatErrorKind(UnpdfErrorKind.Other)
            : string.Join("+", distinct);
    }

    /// <summary>
    /// True for error kinds raised at the interop boundary. Unpdf's ABI reserves 100 and
    /// above for failures that have no library-side counterpart, so such a kind is not a
    /// statement about the document — it says the call could not be completed.
    /// </summary>
    internal static bool IsInteropBoundaryKind(UnpdfErrorKind kind) => (int)kind >= 100;

    /// <summary>
    /// Wording for the every-page-failed outcome.
    /// <para>
    /// It names no cause of its own. The previous wording filed every such failure under
    /// "parse error" and appended "OCR required" unconditionally, which pointed readers of
    /// the message at parser robustness and OCR even when the cause was neither (consumer
    /// field report, 2026-07-31). Scanned documents never reach this path anyway: a page
    /// with no text layer extracts as empty rather than throwing, and is classified as
    /// <c>extraction_failure_reason=no_text_layer</c> instead. The kind itself travels
    /// separately, as the <see cref="ErrorKindKey"/> token the callers append.
    /// </para>
    /// </summary>
    internal static string DescribePagesExhausted(
        int pageCount, string firstError, IEnumerable<UnpdfErrorKind> kinds)
    {
        var message = $"All {pageCount} page(s) failed extraction (first: {firstError}).";

        return kinds.Any(IsInteropBoundaryKind)
            ? message + " At least one failure was raised at the library's interop boundary " +
              "(error kind 100 or above) rather than by the document itself: that band means the " +
              "call could not be completed, and is worth reporting upstream rather than treated " +
              "as a defect in the file."
            : message;
    }

    /// <summary>
    /// Internal control signal: every page failed extraction. Carries the kinds
    /// observed on those pages — a fabricated <see cref="UnpdfException"/> cannot,
    /// because its message-only constructor assigns <see cref="UnpdfErrorKind.Other"/>
    /// and would erase the one diagnostic the consumer receives. Never escapes the
    /// reader; the <c>ExtractAsync</c> overloads translate it into
    /// <see cref="DocumentProcessingException"/>.
    /// </summary>
    private sealed class PdfPagesExhaustedException(string message, string kindSummary) : Exception(message)
    {
        public string KindSummary { get; } = kindSummary;
    }

    public bool CanRead(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension == ".pdf";
    }

    // ========================================
    // Stage 0: Read (Document Structure)
    // ========================================

    public async Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PDF file not found: {filePath}");

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        var startTime = DateTime.UtcNow;
        var fileInfo = new FileInfo(filePath);

        try
        {
            var result = new ReadResult
            {
                File = new SourceFileInfo
                {
                    Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                    Extension = ".pdf",
                    Size = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    ModifiedAt = fileInfo.LastWriteTimeUtc
                },
                ReaderType = ReaderType
            };

            // Get document info using Unpdf
            using var doc = UnpdfDocument.ParseFile(filePath);

            if (!string.IsNullOrWhiteSpace(doc.Title))
                result.DocumentProps["title"] = doc.Title;
            if (!string.IsNullOrWhiteSpace(doc.Author))
                result.DocumentProps["author"] = doc.Author;

            var pageCount = doc.SectionCount;
            result.DocumentProps["page_count"] = pageCount;

            // Stage 0 states the page count, so it is the surface where a short page set is
            // most easily mistaken for a whole document. The same signal the extract stage
            // publishes has to reach here too.
            var (pagesIncomplete, declaredPageCount) = ReadPageIntegrity(doc);
            if (pagesIncomplete)
            {
                result.DocumentProps[PagesIncompleteKey] = true;
                if (declaredPageCount is long declared)
                    result.DocumentProps[DeclaredPageCountKey] = declared;

                result.Warnings.Add("Pages are missing from this document: the PDF is damaged and only " +
                                    "part of its page tree could be read. The page count below is not " +
                                    "the document's own.");
                result.Status = ProcessingStatus.Partial;
            }

            var suppressedTextRuns = ReadSuppressedTextRunCount(doc);
            if (suppressedTextRuns > 0)
            {
                result.DocumentProps[SuppressedTextRunsKey] = suppressedTextRuns;
                result.Warnings.Add($"{suppressedTextRuns} text run(s) in this document could not be " +
                                    "decoded and were silently dropped by the parser (the font's " +
                                    "character codes could not be resolved).");
                result.Status = ProcessingStatus.Partial;
            }

            // Add page info
            for (int i = 1; i <= pageCount; i++)
            {
                result.Pages.Add(new PageInfo
                {
                    Number = i,
                    HasContent = true,
                    Props = { ["file_type"] = "pdf_document" }
                });
            }

            result.Duration = DateTime.UtcNow - startTime;
            return await Task.FromResult(result).ConfigureAwait(false);
        }
        catch (UnpdfException ex)
        {
            throw new DocumentProcessingException(
                filePath, WithErrorKind($"Failed to read PDF document: {ex.Message}", ex.Kind), ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to read PDF document: {ex.Message}", ex);
        }
    }

    public async Task<ReadResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!CanRead(fileName))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        // Unpdf requires file path, so save stream to temp file
        var tempPath = Path.Combine(Path.GetTempPath(), $"unpdf_{Guid.NewGuid():N}.pdf");
        try
        {
            await using (var fileStream = File.Create(tempPath))
            {
                await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            var result = await ReadAsync(tempPath, cancellationToken).ConfigureAwait(false);

            // Update file info to reflect original stream
            result.File = new SourceFileInfo
            {
                Name = fileName,
                Extension = ".pdf",
                Size = new FileInfo(tempPath).Length,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            };

            return result;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* ignore cleanup errors */ }
            }
        }
    }

    // ========================================
    // Stage 1: Extract (Raw Content)
    // ========================================

    public async Task<RawContent> ExtractAsync(string filePath, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PDF file not found: {filePath}");

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        try
        {
            return await Task.Run(() => ExtractPdfContent(filePath, options, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (PdfPagesExhaustedException ex)
        {
            throw new DocumentProcessingException(
                filePath, $"Failed to extract PDF document: {ex.Message} [{ErrorKindKey}={ex.KindSummary}]", ex);
        }
        catch (UnpdfException ex)
        {
            throw new DocumentProcessingException(
                filePath, WithErrorKind($"Failed to extract PDF document: {ex.Message}", ex.Kind), ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(filePath, $"Failed to extract PDF document: {ex.Message}", ex);
        }
    }

    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!CanRead(fileName))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        // Unpdf requires file path, so save stream to temp file
        var tempPath = Path.Combine(Path.GetTempPath(), $"unpdf_{Guid.NewGuid():N}.pdf");
        try
        {
            await using (var fileStream = File.Create(tempPath))
            {
                await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            var result = await Task.Run(() => ExtractPdfContent(tempPath, options, cancellationToken), cancellationToken).ConfigureAwait(false);

            // Update file info to reflect original stream
            result.File = new SourceFileInfo
            {
                Name = fileName,
                Extension = ".pdf",
                Size = new FileInfo(tempPath).Length,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            };

            return result;
        }
        catch (PdfPagesExhaustedException ex)
        {
            throw new DocumentProcessingException(
                fileName,
                $"Failed to extract PDF document from stream: {ex.Message} [{ErrorKindKey}={ex.KindSummary}]",
                ex);
        }
        catch (UnpdfException ex)
        {
            throw new DocumentProcessingException(
                fileName, WithErrorKind($"Failed to extract PDF document from stream: {ex.Message}", ex.Kind), ex);
        }
        catch (Exception ex) when (ex is not FileFluxException)
        {
            throw new DocumentProcessingException(fileName, $"Failed to extract PDF document from stream: {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* ignore cleanup errors */ }
            }
        }
    }

    private static RawContent ExtractPdfContent(string filePath, ExtractOptions? options, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var warnings = new List<string>();
        var errors = new List<ProcessingError>();
        var structuralHints = new Dictionary<string, object>();
        var status = ProcessingStatus.Completed;

        cancellationToken.ThrowIfCancellationRequested();

        // Parse using Unpdf native library. ExtractResources opts into the embedded-image
        // resource inventory (off by default upstream to bound peak memory on large PDFs —
        // Unpdf 0.15.0, see docket iyulab/unpdf#125): without it, GetResourceIds() always
        // returns empty and the docx/pptx/hwp readers' image parity (below) is unreachable.
        using var doc = UnpdfDocument.ParseFile(filePath, new ParseOptions { ExtractResources = true });
        string markdown;

        // Fast path: try whole-document extraction
        try
        {
            markdown = doc.ToMarkdown();
        }
        catch (UnpdfException ex)
        {
            // The whole-document kind is the first thing upstream needs when a file
            // reaches the slow path at all — before 0.15.0 it was discarded here.
            warnings.Add($"Whole-document extraction failed " +
                         $"({ErrorKindKey}={FormatErrorKind(ex.Kind)}); retrying page by page");

            // Slow path: per-page extraction with error accumulation
            (markdown, status) = ExtractPerPage(doc, errors, warnings, structuralHints, cancellationToken);
        }

        // Remove null bytes
        markdown = TextSanitizer.RemoveNullBytes(markdown);

        // Unpdf 0.12.0 ExtractionQuality.SuppressedTextRuns: text runs the font decoder
        // could not resolve and discarded. Read before the empty-document classification
        // below, which needs it to tell "no readable text layer" (scanned, OCR required)
        // apart from "text was here and got dropped at decode time" (not scanned).
        var suppressedTextRuns = ReadSuppressedTextRunCount(doc);

        // Classify the no-text outcome: the document parsed fine but yielded no
        // text at all. Unpdf 0.9.0 page introspection (GetPageStats) separates
        // image-only/scanned pages (no readable text layer, OCR required) from
        // genuinely blank pages, instead of returning a silently-empty result.
        if (string.IsNullOrWhiteSpace(markdown) && status == ProcessingStatus.Completed)
        {
            var reason = ClassifyEmptyDocument(doc, suppressedTextRuns);
            structuralHints["extraction_failure_reason"] = reason;
            warnings.Add(reason switch
            {
                "no_text_layer" => "PDF contains no extractable text (image-only/scanned document). " +
                    "Text extraction requires OCR, which is outside the text extractor's scope.",
                "text_runs_suppressed" => $"PDF text could not be fully decoded and was silently dropped " +
                    $"by the parser ({suppressedTextRuns} text run(s) discarded — the font's character " +
                    "codes could not be resolved). This is not a scanned document and does not need OCR.",
                _ => "PDF parsed successfully but every page is blank (no text or image content)."
            });
        }
        else if (suppressedTextRuns > 0)
        {
            // Partial loss: some text survived, but not all of it. The empty-document
            // branch above already explains the all-lost case; this covers the rest.
            warnings.Add($"{suppressedTextRuns} text run(s) in this document could not be decoded and " +
                         "were silently dropped by the parser (the font's character codes could not be " +
                         "resolved). The extracted text is not complete.");
        }

        if (suppressedTextRuns > 0)
        {
            structuralHints[SuppressedTextRunsKey] = suppressedTextRuns;
            status = ProcessingStatus.Partial;
        }

        // Damage does not always fail: the parser recovers what it can from a broken page
        // tree and returns a shorter document, successfully. Unpdf 0.11.0 reports that as
        // PagesIncomplete. Without it, a page that never arrived is indistinguishable from
        // a page that never existed, and downstream indexing treats the surviving fraction
        // as the whole document — a silent deletion of answers rather than a wrong one.
        // Placed after the empty-document classification, which only runs while the status
        // is still Completed: both facts can hold at once and neither may mask the other.
        var (pagesIncomplete, declaredPageCount) = ReadPageIntegrity(doc);
        if (pagesIncomplete)
        {
            structuralHints[PagesIncompleteKey] = true;
            if (declaredPageCount is long declared)
                structuralHints[DeclaredPageCountKey] = declared;

            warnings.Add("Pages are missing from this extraction: the PDF is damaged and only part " +
                         "of its page tree could be read. The extracted text is not the whole document.");
            status = ProcessingStatus.Partial;
        }

        if (!string.IsNullOrWhiteSpace(doc.Title))
            structuralHints["document_title"] = doc.Title;
        if (!string.IsNullOrWhiteSpace(doc.Author))
            structuralHints["author"] = doc.Author;

        structuralHints["page_count"] = doc.SectionCount;

        // Update structural hints
        structuralHints["file_type"] = "pdf_document";
        structuralHints["character_count"] = markdown.Length;
        structuralHints["word_count"] = CountWords(markdown);
        structuralHints["paragraph_count"] = CountParagraphs(markdown);
        structuralHints["conversion_method"] = "unpdf_native";

        // Detect structural elements from markdown
        var hasHeaders = HeaderRegex().IsMatch(markdown);
        var hasTables = markdown.Contains("|---", StringComparison.Ordinal) ||
                       markdown.Contains("| ---", StringComparison.Ordinal);
        var hasLists = ListRegex().IsMatch(markdown);
        var hasLinks = LinkRegex().IsMatch(markdown);
        var hasImages = ImageRegex().IsMatch(markdown);

        if (hasHeaders) structuralHints["has_headers"] = true;
        if (hasTables) structuralHints["has_tables"] = true;
        if (hasLists) structuralHints["has_lists"] = true;
        if (hasLinks) structuralHints["has_links"] = true;
        if (hasImages) structuralHints["has_images"] = true;

        // Embedded raster images (XObject resources — screenshots/diagrams a Word/PowerPoint
        // export carries into the PDF), extracted now that ParseOptions.ExtractResources is set
        // above. Parity with the docx/pptx/hwp readers, which have always populated this. Inline
        // images (BI/ID/EI content-stream operators, no resource-dictionary entry) and scanned
        // full-page rasterizations are out of scope — GetResourceIds() does not surface either.
        var extractedImages = ExtractEmbeddedImages(doc, warnings);
        if (extractedImages.Count > 0)
        {
            structuralHints["has_images"] = true;
            structuralHints["image_count"] = extractedImages.Count;
        }

        return new RawContent
        {
            Text = markdown.Trim(),
            File = new SourceFileInfo
            {
                Name = FileNameHelper.ExtractSafeFileName(fileInfo),
                Extension = ".pdf",
                Size = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc,
                ModifiedAt = fileInfo.LastWriteTimeUtc
            },
            Hints = structuralHints,
            Warnings = warnings,
            Errors = errors,
            Status = status,
            Images = extractedImages,
            ReaderType = "PdfReader"
        };
    }

    /// <summary>
    /// Reads the embedded-image resource inventory <see cref="UnpdfDocument.GetResourceIds"/>
    /// exposes once <see cref="ParseOptions.ExtractResources"/> is set. Failure here degrades to
    /// zero images with a warning rather than failing the whole extraction — the document's text
    /// already extracted successfully by this point, and losing images is a lesser outcome than
    /// losing the document.
    /// </summary>
    private static List<ImageInfo> ExtractEmbeddedImages(UnpdfDocument doc, List<string> warnings)
    {
        var images = new List<ImageInfo>();

        try
        {
            foreach (var resourceId in doc.GetResourceIds())
            {
                var resourceData = doc.GetResourceData(resourceId);
                if (resourceData is not { Length: > 0 })
                    continue;

                images.Add(new ImageInfo
                {
                    Id = resourceId,
                    MimeType = ImageMimeTypeDetector.Detect(resourceData, resourceId),
                    Data = resourceData,
                    OriginalSize = resourceData.Length,
                    SourceUrl = $"embedded:{resourceId}"
                });
            }
        }
        catch (UnpdfException ex)
        {
            warnings.Add($"Embedded image extraction failed: {ex.Message} " +
                         $"[{ErrorKindKey}={FormatErrorKind(ex.Kind)}]");
        }

        return images;
    }

    /// <summary>
    /// Reads Unpdf's page-tree integrity signals. <c>PagesIncomplete</c> is the primary
    /// fact — the parser's own account of whether every page reached the output — and the
    /// declared page count is supporting evidence, absent when the declaration itself was
    /// unreadable. Introspection failure is reported as "no known damage": the reader must
    /// not invent an incompleteness warning it has no evidence for.
    /// </summary>
    private static (bool PagesIncomplete, long? DeclaredPageCount) ReadPageIntegrity(UnpdfDocument doc)
    {
        try
        {
            var quality = doc.GetExtractionQuality();
            return (quality.PagesIncomplete, quality.DeclaredPageCount);
        }
        catch (UnpdfException)
        {
            return (false, null);
        }
    }

    /// <summary>
    /// Reads Unpdf's decode-loss counter. <c>SuppressedTextRuns</c> is the document-level
    /// total, counted in runs rather than characters since the discarded text was never
    /// decoded. Unpdf 0.14.0 also exposes the per-page breakdown via
    /// <c>PageStats.SuppressedTextRuns</c> (see <see cref="ClassifyEmptyDocument"/>), but
    /// the document total is kept as its own independent fact for the document-level
    /// structural hint and the partial-loss warning — a document can lose pages, lose text
    /// runs, both, or neither, and collapsing them into one tuple would make "only one is
    /// true" branch on the caller. Introspection failure is reported as "no known
    /// suppression" — zero, not a guess.
    /// </summary>
    private static long ReadSuppressedTextRunCount(UnpdfDocument doc)
    {
        try
        {
            return doc.GetExtractionQuality().SuppressedTextRuns;
        }
        catch (UnpdfException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Classifies a parsed-but-empty document via Unpdf page introspection:
    /// "no_text_layer" when any page draws images without a readable text layer
    /// (scanned document — searchable scans whose OCR layer was discarded
    /// surface as OcrTextSuppressed), "text_runs_suppressed" when a page's own text runs
    /// were discarded by the decoder (Unpdf 0.14.0 per-page <c>PageStats.SuppressedTextRuns</c>,
    /// checked before falling back to the whole-document total for introspection paths that
    /// only see the aggregate), "blank_page" when no page has text or image content.
    /// Introspection failures fall back to "no_text_layer" (the pre-0.14.0 single-label
    /// behavior).
    /// </summary>
    private static string ClassifyEmptyDocument(UnpdfDocument doc, long suppressedTextRuns)
    {
        try
        {
            var sawContent = false;
            for (var page = 1; page <= doc.SectionCount; page++)
            {
                var stats = doc.GetPageStats(page);
                if (stats.ImageOpCount > 0 && (stats.TextOpCount == 0 || stats.OcrTextSuppressed))
                    return "no_text_layer";
                if (stats.SuppressedTextRuns > 0)
                    return "text_runs_suppressed";
                if (stats.TextOpCount > 0 || stats.ImageOpCount > 0)
                    sawContent = true;
            }

            if (!sawContent)
                return "blank_page";

            // No page individually reported suppression, but the whole-document total
            // did — keep the aggregate as a fallback for introspection gaps the per-page
            // loop above did not catch.
            return suppressedTextRuns > 0 ? "text_runs_suppressed" : "no_text_layer";
        }
        catch (UnpdfException)
        {
            return "no_text_layer";
        }
    }

    /// <summary>
    /// Per-page extraction with error accumulation.
    /// Tries markdown per page, falls back to plaintext per page, skips completely failed pages.
    /// Every Unpdf error kind seen along the way is retained: page-level kinds are the only
    /// evidence available for documents that open cleanly but yield no page content, and no
    /// synthesizable corruption reaches this path (crude damage fails at parse time instead),
    /// so nothing here can be reconstructed after the fact.
    /// </summary>
    private static (string markdown, ProcessingStatus status) ExtractPerPage(
        UnpdfDocument doc,
        List<ProcessingError> errors,
        List<string> warnings,
        Dictionary<string, object> structuralHints,
        CancellationToken cancellationToken)
    {
        var pageCount = doc.SectionCount;
        var parts = new List<string>(pageCount);
        var failedPages = new List<int>();
        var failedKinds = new List<UnpdfErrorKind>();

        for (int page = 1; page <= pageCount; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            UnpdfErrorKind markdownKind;

            // Try markdown first
            try
            {
                var pageMarkdown = doc.PageToMarkdown(page);
                if (!string.IsNullOrWhiteSpace(pageMarkdown))
                    parts.Add(pageMarkdown);
                continue;
            }
            catch (UnpdfException ex)
            {
                // Markdown failed for this page — try plaintext
                markdownKind = ex.Kind;
            }

            // Try plaintext fallback
            try
            {
                var pageText = doc.PageToText(page);
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    parts.Add(pageText);
                    warnings.Add($"Page {page}: using plaintext fallback " +
                                 $"(markdown {ErrorKindKey}={FormatErrorKind(markdownKind)})");
                }
                continue;
            }
            catch (UnpdfException ex)
            {
                // Both failed — record error and skip page
                failedPages.Add(page);
                failedKinds.Add(ex.Kind);
                errors.Add(new ProcessingError
                {
                    Code = "PDF_PAGE_EXTRACTION_FAILED",
                    Message = $"Page {page}: {ex.Message}",
                    Stage = "extraction",
                    Details = new Dictionary<string, object>
                    {
                        ["page"] = page,
                        [ErrorKindKey] = FormatErrorKind(ex.Kind)
                    }
                });
            }
        }

        // If all pages failed, throw to let caller handle. Carry the first
        // per-page error so the failure cause is diagnosable — the bare
        // "All N pages failed" phrasing read like a parser defect and gave
        // consumers nothing to classify on (consumer field report, 2026-07-22).
        if (parts.Count == 0 && failedPages.Count > 0)
        {
            var firstError = errors.Count > 0 ? errors[0].Message : "unknown error";
            throw new PdfPagesExhaustedException(
                DescribePagesExhausted(pageCount, firstError, failedKinds),
                SummarizeErrorKinds(failedKinds));
        }

        if (failedPages.Count > 0)
        {
            warnings.Add($"Skipped {failedPages.Count} page(s): [{string.Join(", ", failedPages)}]");
            // Partial results do carry a RawContent, so here the kind is a real hint
            // rather than a token smuggled through an exception message.
            structuralHints[ErrorKindKey] = SummarizeErrorKinds(failedKinds);
        }

        var status = failedPages.Count > 0 ? ProcessingStatus.Partial : ProcessingStatus.Completed;
        return (string.Join("\n\n", parts), status);
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        return text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static int CountParagraphs(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        // Count paragraphs by splitting on double newlines
        var paragraphs = text.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);
        return Math.Max(1, paragraphs.Length);
    }

    // Generated regex patterns for performance
    [GeneratedRegex(@"^#{1,6}\s", RegexOptions.Multiline)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"^[\*\-\+]\s|^\d+\.\s", RegexOptions.Multiline)]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"\[.+?\]\(.+?\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"!\[.*?\]\(.+?\)")]
    private static partial Regex ImageRegex();
}
