using System.Globalization;
using System.Text;
using FileFlux.Core;

namespace FileFlux.Infrastructure.Readers;

/// <summary>
/// What differs between the multimodal readers in how they write image descriptions into the text: the document type the
/// relevance evaluator is told, the hint passed to the image service, the section marker around the description blocks,
/// the label line above each description, and the subject of each <c>ImageProcessingResults</c> entry.
/// </summary>
internal sealed class ImageDescriptionFormat
{
    /// <summary><see cref="DocumentContext.DocumentType"/> for the relevance evaluator ("PDF", "Word", ...).</summary>
    public required string DocumentType { get; init; }

    /// <summary><see cref="ImageToTextOptions.ImageTypeHint"/> passed to the image service.</summary>
    public required string ImageTypeHint { get; init; }

    /// <summary>
    /// The name of the marker pair around the description blocks (<c>&lt;!-- NAME_START --&gt;</c> ...
    /// <c>&lt;!-- NAME_END --&gt;</c>); null appends the blocks to the text without one.
    /// </summary>
    public string? SectionMarker { get; init; }

    /// <summary>The line above a description: the image and its 1-based number among the described images.</summary>
    public required Func<ImageInfo, int, string> Label { get; init; }

    /// <summary>
    /// The start of an <c>ImageProcessingResults</c> entry, from the image's number and the service's
    /// <see cref="ImageToTextResult.ImageType"/>; the entry continues with <c> INCLUDED - reason</c> or <c> EXCLUDED - reason</c>.
    /// </summary>
    public required Func<int, string?, string> ResultSubject { get; init; }
}

/// <summary>
/// Describes the images a base reader extracted (<see cref="RawContent.Images"/>) through the registered
/// <see cref="IImageToTextService"/> and appends each description to the text. Because it works from the extracted
/// images, the file and the stream path describe the same images, and the extraction options the base reader applied
/// (<see cref="ExtractOptions.ExtractImages"/>, <see cref="ExtractOptions.MaxImageSize"/>) decide what is described.
/// </summary>
internal static class ImageDescriptions
{
    private static readonly char[] s_keywordSeparators = [' ', '\n', '\r', '\t', '|'];

    /// <summary>
    /// Describes <paramref name="baseContent"/>'s images and returns the content with the descriptions appended, the image
    /// hints set and <see cref="RawContent.ReaderType"/> set to <paramref name="readerType"/>. Without a service the base
    /// content is returned as is. An image the service fails on is skipped; a failure of the whole step keeps the base text
    /// and adds a warning. Cancellation is not a failure: it propagates.
    /// </summary>
    /// <remarks>
    /// Skipped: an image without data, an image on a page already replaced by a read of its render
    /// (<see cref="ImageInfo.ReadAsPage"/>), and a decorative embedded image (smaller than
    /// <see cref="ImageProcessingConstants.MinImageWidth"/> x <see cref="ImageProcessingConstants.MinImageHeight"/>; a
    /// rendered page is never decorative).
    /// </remarks>
    public static async Task<RawContent> AppendAsync(
        RawContent baseContent,
        string title,
        string readerType,
        ImageDescriptionFormat format,
        IImageToTextService? imageToTextService,
        IImageRelevanceEvaluator? relevanceEvaluator,
        CancellationToken cancellationToken)
    {
        if (imageToTextService is null)
            return baseContent;

        var enhancedText = new StringBuilder(baseContent.Text);
        var imageProcessingResults = new List<string>();
        var structuralHints = baseContent.Hints?.ToDictionary(kv => kv.Key, kv => kv.Value)
                             ?? new Dictionary<string, object>();

        try
        {
            var imageCount = 0;
            var includedImageCount = 0;
            var excludedImageCount = 0;

            var documentImages = await DescribeAsync(imageToTextService, baseContent.Images, format.ImageTypeHint, cancellationToken).ConfigureAwait(false);

            if (documentImages.Count != 0)
            {
                List<ImageRelevanceResult>? relevanceResults = null;
                if (relevanceEvaluator != null)
                {
                    var documentContext = PrepareDocumentContext(baseContent, title, format.DocumentType);
                    var imageTexts = documentImages.Select(img => img.Result.ExtractedText).ToList();
                    relevanceResults = (await relevanceEvaluator.EvaluateBatchAsync(
                        imageTexts, documentContext, cancellationToken).ConfigureAwait(false)).ToList();
                }

                var hasRelevantImages = false;
                // With a section marker the blocks are collected and appended as one section; without one they go
                // straight after the text.
                var blocks = format.SectionMarker is null ? enhancedText : new StringBuilder();

                for (int i = 0; i < documentImages.Count; i++)
                {
                    var (imageResult, image) = documentImages[i];
                    imageCount++;

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

                    var subject = format.ResultSubject(imageCount, imageResult.ImageType);
                    if (shouldInclude)
                    {
                        if (!hasRelevantImages && format.SectionMarker is null)
                            StartOnNewLine(enhancedText);
                        if (!hasRelevantImages && format.SectionMarker is { } marker)
                            blocks.AppendLine(CultureInfo.InvariantCulture, $"<!-- {marker}_START -->");
                        hasRelevantImages = true;

                        blocks.AppendLine(CultureInfo.InvariantCulture, $"<!-- IMAGE_START:IMG_{imageCount} -->");
                        blocks.AppendLine(format.Label(image, imageCount));
                        blocks.AppendLine(processedText);
                        blocks.AppendLine(CultureInfo.InvariantCulture, $"<!-- IMAGE_END:IMG_{imageCount} -->");

                        includedImageCount++;
                        imageProcessingResults.Add($"{subject} INCLUDED - {inclusionReason}");
                    }
                    else
                    {
                        excludedImageCount++;
                        imageProcessingResults.Add($"{subject} EXCLUDED - {inclusionReason}");
                    }
                }

                if (hasRelevantImages && format.SectionMarker is { } sectionMarker)
                {
                    blocks.AppendLine(CultureInfo.InvariantCulture, $"<!-- {sectionMarker}_END -->");
                    StartOnNewLine(enhancedText);
                    enhancedText.AppendLine(blocks.ToString());
                }
            }

            if (imageCount > 0)
            {
                structuralHints["HasImages"] = true;
                structuralHints["TotalImageCount"] = imageCount;
                structuralHints["IncludedImageCount"] = includedImageCount;
                structuralHints["ExcludedImageCount"] = excludedImageCount;
                structuralHints["ImageProcessingResults"] = imageProcessingResults;

                if (relevanceEvaluator != null)
                {
                    structuralHints["ImageRelevanceEvaluationEnabled"] = true;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var warnings = baseContent.Warnings?.ToList() ?? new List<string>();
            warnings.Add($"Image processing failed: {ex.Message}");

            var fallback = baseContent.WithText(baseContent.Text);
            fallback.Warnings = warnings;
            fallback.ReaderType = readerType;
            return fallback;
        }

        var enhanced = baseContent.WithText(enhancedText.ToString());
        enhanced.Hints = structuralHints;
        enhanced.ReaderType = readerType;
        return enhanced;
    }

    /// <summary>
    /// Describes each image that has data, is not on a page already read as a whole, and is not decorative. An image the
    /// service fails on is skipped; the others are still described.
    /// </summary>
    private static async Task<List<(ImageToTextResult Result, ImageInfo Image)>> DescribeAsync(
        IImageToTextService imageToTextService,
        IEnumerable<ImageInfo> images,
        string imageTypeHint,
        CancellationToken cancellationToken)
    {
        var results = new List<(ImageToTextResult Result, ImageInfo Image)>();

        foreach (var image in images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (image.Data is not { Length: > 0 } imageBytes || image.ReadAsPage)
                continue;

            if (image.RenderedPage is null)
            {
                var (width, height) = DimensionsOf(image, imageBytes);
                if (ImageProcessingConstants.IsDecorativeImage(width, height))
                    continue;
            }

            try
            {
                var options = new ImageToTextOptions
                {
                    ImageTypeHint = imageTypeHint,
                    Quality = "medium",
                    ExtractStructure = true
                };

                var result = await imageToTextService.ExtractTextAsync(imageBytes, options, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(result.ExtractedText))
                    results.Add((result, image));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // One image the service cannot read does not stop the others.
            }
        }

        return results;
    }

    /// <summary>
    /// The image's pixel size: what the reader recorded (<c>width</c>/<c>height</c> in <see cref="ImageInfo.Properties"/>,
    /// from the parser's resource metadata), else what a PNG header says; other formats count as large enough.
    /// </summary>
    /// <summary>
    /// A marker appended straight after text that does not end its last line would join that line (a table row, a
    /// heading) and break it.
    /// </summary>
    private static void StartOnNewLine(StringBuilder text)
    {
        if (text.Length > 0 && text[^1] != '\n')
            text.Append('\n');
    }

    private static (int Width, int Height) DimensionsOf(ImageInfo image, byte[] imageBytes)
    {
        if (image.Properties.TryGetValue("width", out var w) && w is int width
            && image.Properties.TryGetValue("height", out var h) && h is int height)
            return (width, height);

        // PNG: IHDR width and height, big-endian, at bytes 16-23.
        if (imageBytes.Length > 24 &&
            imageBytes[0] == 0x89 && imageBytes[1] == 0x50 &&
            imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
        {
            return ((imageBytes[16] << 24) | (imageBytes[17] << 16) | (imageBytes[18] << 8) | imageBytes[19],
                    (imageBytes[20] << 24) | (imageBytes[21] << 16) | (imageBytes[22] << 8) | imageBytes[23]);
        }

        return (1000, 1000);
    }

    /// <summary>The document as the relevance evaluator sees it.</summary>
    private static DocumentContext PrepareDocumentContext(RawContent baseContent, string title, string documentType)
    {
        var context = new DocumentContext
        {
            DocumentType = documentType,
            DocumentText = TruncateText(baseContent.Text, 1000),
            SurroundingText = TruncateText(baseContent.Text, 500),
            Title = title
        };

        if (baseContent.Hints != null)
        {
            foreach (var hint in baseContent.Hints)
            {
                context.Metadata[hint.Key] = hint.Value?.ToString() ?? "";
            }
        }

        // Simple keywords: distinct words of five or more characters.
        var words = baseContent.Text.Split(s_keywordSeparators, StringSplitOptions.RemoveEmptyEntries);
        context.Keywords = words
            .Where(w => w.Length >= 5)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        return context;
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return string.Concat(text.AsSpan(0, maxLength), "...");
    }
}
