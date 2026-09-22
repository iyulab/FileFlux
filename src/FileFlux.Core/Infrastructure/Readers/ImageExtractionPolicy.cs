namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Applies the two image members of <see cref="ExtractOptions"/> to what a reader collected —
/// <see cref="ExtractOptions.ExtractImages"/> (off: no images in the result) and
/// <see cref="ExtractOptions.MaxImageSize"/> (an image above it is dropped, with a warning naming it).
/// One policy for every reader, applied where each reader hands its result back, so the option means the
/// same on PDF, DOCX, PPTX and HWP. Before 0.24.2 no reader read either member.
/// </summary>
public static class ImageExtractionPolicy
{
    /// <summary>Applies the policy in place and returns <paramref name="raw"/>.</summary>
    public static RawContent Apply(RawContent raw, ExtractOptions? options)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (options is null || raw.Images.Count == 0)
            return raw;

        if (!options.ExtractImages)
        {
            raw.Images = [];
            raw.Hints.Remove("has_images");
            raw.Hints.Remove("image_count");
            return raw;
        }

        if (options.MaxImageSize is { } max && max > 0)
        {
            var kept = new List<ImageInfo>(raw.Images.Count);
            foreach (var image in raw.Images)
            {
                var size = image.Data?.Length ?? image.OriginalSize;
                if (size > max)
                {
                    raw.Warnings.Add($"Image '{image.Id}' ({size} bytes) exceeds ExtractOptions.MaxImageSize ({max} bytes) and was skipped.");
                    continue;
                }
                kept.Add(image);
            }
            raw.Images = kept;
            if (raw.Hints.ContainsKey("image_count"))
                raw.Hints["image_count"] = kept.Count;
            if (kept.Count == 0)
                raw.Hints.Remove("has_images");
        }

        return raw;
    }
}
