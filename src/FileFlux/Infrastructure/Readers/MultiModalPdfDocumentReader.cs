using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Unpdf;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace FileFlux.Infrastructure.Readers;

/// <summary>
/// Multimodal PDF document reader with integrated image processing.
/// When IImageToTextService is provided, extracts text from images for enrichment.
/// When IImageRelevanceEvaluator is provided, selectively includes relevant images.
/// </summary>
public class MultiModalPdfDocumentReader : IDocumentReader
{
    private static readonly char[] s_keywordSeparators = [' ', '\n', '\r', '\t'];
    private readonly IImageToTextService? _imageToTextService;
    private readonly IImageRelevanceEvaluator? _relevanceEvaluator;
    private readonly PdfDocumentReader _basePdfReader;

    public string ReaderType => "MultiModalPdfReader";

    public IEnumerable<string> SupportedExtensions => new[] { ".pdf" };

    public MultiModalPdfDocumentReader(IServiceProvider serviceProvider)
    {
        // IImageToTextService is an optional dependency
        _imageToTextService = serviceProvider.GetService<IImageToTextService>();
        // IImageRelevanceEvaluator is an optional dependency
        _relevanceEvaluator = serviceProvider.GetService<IImageRelevanceEvaluator>();
        _basePdfReader = new PdfDocumentReader();
    }

    public bool CanRead(string fileName)
    {
        return _basePdfReader.CanRead(fileName);
    }

    // ========================================
    // Stage 0: Read (Document Structure)
    // ========================================

    public Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return _basePdfReader.ReadAsync(filePath, cancellationToken);
    }

    public Task<ReadResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        return _basePdfReader.ReadAsync(stream, fileName, cancellationToken);
    }

    // ========================================
    // Stage 1: Extract (Raw Content)
    // ========================================

    public async Task<RawContent> ExtractAsync(string filePath, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Base PDF text extraction
        var baseContent = await _basePdfReader.ExtractAsync(filePath, options, cancellationToken);

        // If no image service, return base result
        if (_imageToTextService == null)
            return baseContent;

        // If image service available, perform enhanced extraction
        return await ExtractWithImageProcessing(filePath, baseContent, cancellationToken);
    }

    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Base PDF text extraction
        var baseContent = await _basePdfReader.ExtractAsync(stream, fileName, options, cancellationToken);

        // If no image service, return base result
        if (_imageToTextService == null)
            return baseContent;

        // Stream-based image processing is complex, return base result (future extension)
        return baseContent;
    }

    /// <summary>
    /// Enhanced PDF text extraction with image processing.
    /// </summary>
    private async Task<RawContent> ExtractWithImageProcessing(
        string filePath,
        RawContent baseContent,
        CancellationToken cancellationToken)
    {
        var enhancedText = new StringBuilder(baseContent.Text);
        var imageProcessingResults = new List<string>();
        var structuralHints = baseContent.Hints?.ToDictionary(kv => kv.Key, kv => kv.Value)
                             ?? new Dictionary<string, object>();

        // Prepare document context (for relevance evaluation)
        var documentContext = PrepareDocumentContext(baseContent, filePath);

        try
        {
            // Extract images using Unpdf native library
            using var doc = UnpdfDocument.ParseFile(filePath);
            var resourceIds = doc.GetResourceIds();

            var imageCount = 0;
            var includedImageCount = 0;
            var excludedImageCount = 0;

            // Process extracted image resources
            var imagesToProcess = new List<byte[]>();

            foreach (var id in resourceIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var imageBytes = doc.GetResourceData(id);
                if (imageBytes == null || imageBytes.Length <= 100)
                    continue;

                // Filter decorative images by size using resource metadata
                int width = 0, height = 0;
                using var resourceInfo = doc.GetResourceInfo(id);
                if (resourceInfo != null)
                {
                    if (resourceInfo.RootElement.TryGetProperty("width", out var w))
                        width = w.GetInt32();
                    if (resourceInfo.RootElement.TryGetProperty("height", out var h))
                        height = h.GetInt32();
                }

                if (ImageProcessingConstants.IsDecorativeImage(width, height))
                    continue;

                imagesToProcess.Add(imageBytes);
            }

            // Process images through IImageToTextService
            var imageTextResults = new List<ImageToTextResult>();
            foreach (var bytes in imagesToProcess)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var options = new ImageToTextOptions
                    {
                        ImageTypeHint = "document",
                        Quality = "medium",
                        ExtractStructure = true
                    };

                    var result = await _imageToTextService!.ExtractTextAsync(bytes, options, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(result.ExtractedText))
                    {
                        imageTextResults.Add(result);
                    }
                }
                catch
                {
                    // Individual image processing failure is ignored
                }
            }

            // Batch relevance evaluation if evaluator is available
            List<ImageRelevanceResult>? relevanceResults = null;
            if (_relevanceEvaluator != null && imageTextResults.Count != 0)
            {
                var imageTexts = imageTextResults.Select(r => r.ExtractedText).ToList();
                relevanceResults = (await _relevanceEvaluator.EvaluateBatchAsync(
                    imageTexts, documentContext, cancellationToken)).ToList();
            }

            // Build enhanced content
            for (int i = 0; i < imageTextResults.Count; i++)
            {
                var imageResult = imageTextResults[i];
                imageCount++;

                // Check relevance evaluation result
                bool shouldInclude = true;
                string? processedText = imageResult.ExtractedText;
                string inclusionReason = "No relevance evaluation";

                if (relevanceResults != null && i < relevanceResults.Count)
                {
                    var relevance = relevanceResults[i];
                    shouldInclude = relevance.Recommendation != InclusionRecommendation.MustExclude &&
                                  relevance.Recommendation != InclusionRecommendation.ShouldExclude;

                    if (!string.IsNullOrEmpty(relevance.ProcessedText))
                    {
                        processedText = relevance.ProcessedText;
                    }

                    inclusionReason = $"{relevance.Category}: {relevance.Reasoning} (Score: {relevance.RelevanceScore:F2})";
                }

                if (shouldInclude)
                {
                    enhancedText.AppendLine(CultureInfo.InvariantCulture, $"<!-- IMAGE_START:IMG_{imageCount} -->");
                    enhancedText.AppendLine(CultureInfo.InvariantCulture, $"Image {imageCount}:");
                    enhancedText.AppendLine(processedText);
                    enhancedText.AppendLine(CultureInfo.InvariantCulture, $"<!-- IMAGE_END:IMG_{imageCount} -->");

                    includedImageCount++;
                    imageProcessingResults.Add($"Image {imageCount}: {imageResult.ImageType} INCLUDED - {inclusionReason}");
                }
                else
                {
                    excludedImageCount++;
                    imageProcessingResults.Add($"Image {imageCount}: {imageResult.ImageType} EXCLUDED - {inclusionReason}");
                }
            }

            // Add image processing info to structural hints
            if (imageCount > 0)
            {
                structuralHints["HasImages"] = true;
                structuralHints["TotalImageCount"] = imageCount;
                structuralHints["IncludedImageCount"] = includedImageCount;
                structuralHints["ExcludedImageCount"] = excludedImageCount;
                structuralHints["ImageProcessingResults"] = imageProcessingResults;

                if (_relevanceEvaluator != null)
                {
                    structuralHints["ImageRelevanceEvaluationEnabled"] = true;
                }
            }
        }
        catch (Exception ex)
        {
            // On image processing failure, use base result with warning
            var warnings = baseContent.Warnings?.ToList() ?? new List<string>();
            warnings.Add($"Image processing failed: {ex.Message}");

            return new RawContent
            {
                Text = baseContent.Text,
                Blocks = baseContent.Blocks,
                Tables = baseContent.Tables,
                Images = baseContent.Images,
                File = baseContent.File,
                Hints = baseContent.Hints ?? new Dictionary<string, object>(),
                Warnings = warnings,
                ReaderType = ReaderType
            };
        }
        finally
        {
            // No temp files to clean up (resources extracted directly from native library)
        }

        return new RawContent
        {
            Text = enhancedText.ToString(),
            Blocks = baseContent.Blocks,
            Tables = baseContent.Tables,
            Images = baseContent.Images,
            File = baseContent.File,
            Hints = structuralHints,
            Warnings = baseContent.Warnings,
            ReaderType = ReaderType
        };
    }

    /// <summary>
    /// Prepare document context for relevance evaluation.
    /// </summary>
    private static DocumentContext PrepareDocumentContext(RawContent baseContent, string filePath)
    {
        var context = new DocumentContext
        {
            DocumentType = "PDF",
            DocumentText = TruncateText(baseContent.Text, 1000)
        };

        // Extract title from filename
        context.Title = Path.GetFileNameWithoutExtension(filePath);

        // Extract metadata from structural hints
        if (baseContent.Hints != null)
        {
            foreach (var hint in baseContent.Hints)
            {
                context.Metadata[hint.Key.ToString()] = hint.Value?.ToString() ?? "";
            }
        }

        // Simple keyword extraction (words with length >= 5)
        var words = baseContent.Text.Split(s_keywordSeparators, StringSplitOptions.RemoveEmptyEntries);
        context.Keywords = words
            .Where(w => w.Length >= 5)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        return context;
    }

    /// <summary>
    /// Text truncation helper.
    /// </summary>
    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return string.Concat(text.AsSpan(0, maxLength), "...");
    }
}
