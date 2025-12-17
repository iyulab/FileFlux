using System.Text;
using System.Text.RegularExpressions;
using FileFlux.Core;
using FileFlux.Domain;

namespace FileFlux.Infrastructure.Services;

/// <summary>
/// Image processing service for extracting and analyzing images from document content
/// </summary>
public class ImageProcessor
{
    private readonly OutputOptions _options;
    private readonly bool _verbose;

    public ImageProcessor(OutputOptions options, bool verbose = false)
    {
        _options = options;
        _verbose = verbose;
    }

    /// <summary>
    /// Process pre-extracted images from RawContent.Images (e.g., HTML embedded images)
    /// </summary>
    public async Task<ImageProcessingResult> ProcessPreExtractedImagesAsync(
        string content,
        IReadOnlyList<ImageInfo> preExtractedImages,
        string imagesDirectory,
        IImageToTextService? imageToTextService,
        CancellationToken cancellationToken = default)
    {
        var result = content;
        var processedImages = new List<ProcessedImage>();
        var skippedCount = 0;
        var savedIndex = 0;

        if (_verbose)
        {
            Console.WriteLine($"[Verbose] Processing {preExtractedImages.Count} pre-extracted images, filters: minSize={_options.MinImageSize}bytes, minDimension={_options.MinImageDimension}px");
        }

        foreach (var imageInfo in preExtractedImages)
        {
            // Skip images without binary data (external URLs)
            if (imageInfo.Data == null || imageInfo.Data.Length == 0)
            {
                if (_verbose)
                {
                    Console.WriteLine($"[Verbose] Skipped {imageInfo.Id}: no binary data (external URL)");
                }
                continue;
            }

            // Check file size
            if (imageInfo.Data.Length < _options.MinImageSize)
            {
                skippedCount++;
                if (_verbose)
                {
                    Console.WriteLine($"[Verbose] Skipped {imageInfo.Id}: size={imageInfo.Data.Length}bytes < {_options.MinImageSize}bytes");
                }
                // Remove placeholder from content
                result = result.Replace($"![{imageInfo.Caption}](embedded:{imageInfo.Id})", "");
                result = result.Replace($"![]( embedded:{imageInfo.Id})", "");
                continue;
            }

            // Check dimensions
            var format = GetFormatFromMimeType(imageInfo.MimeType);
            var dimensions = GetImageDimensions(imageInfo.Data, format);
            if (dimensions.Width < _options.MinImageDimension || dimensions.Height < _options.MinImageDimension)
            {
                skippedCount++;
                if (_verbose)
                {
                    Console.WriteLine($"[Verbose] Skipped {imageInfo.Id}: dimensions={dimensions.Width}x{dimensions.Height} < {_options.MinImageDimension}px");
                }
                result = result.Replace($"![{imageInfo.Caption}](embedded:{imageInfo.Id})", "");
                result = result.Replace($"![](embedded:{imageInfo.Id})", "");
                continue;
            }

            savedIndex++;
            var fileName = $"image_{savedIndex}.{format}";
            var filePath = Path.Combine(imagesDirectory, fileName);

            try
            {
                // Create directory on first image
                if (!Directory.Exists(imagesDirectory))
                {
                    Directory.CreateDirectory(imagesDirectory);
                }

                await File.WriteAllBytesAsync(filePath, imageInfo.Data, cancellationToken).ConfigureAwait(false);

                var processedImage = new ProcessedImage
                {
                    FileName = fileName,
                    FileSize = imageInfo.Data.Length,
                    Width = dimensions.Width,
                    Height = dimensions.Height
                };

                // AI image-to-text if enabled
                if (imageToTextService != null && _options.EnableAI)
                {
                    try
                    {
                        if (_verbose)
                        {
                            Console.WriteLine($"[Verbose] Analyzing image {savedIndex} with AI...");
                        }
                        var aiResult = await imageToTextService.ExtractTextAsync(
                            imageInfo.Data, null, cancellationToken).ConfigureAwait(false);
                        processedImage.AIDescription = aiResult.ExtractedText;
                    }
                    catch (Exception ex)
                    {
                        if (_verbose)
                        {
                            Console.WriteLine($"[Verbose] AI analysis failed: {ex.Message}");
                        }
                        processedImage.AIError = ex.Message;
                    }
                }

                processedImages.Add(processedImage);

                // Build replacement text
                var relativePath = $"./images/{fileName}";
                var replacement = BuildImageReplacement(processedImage, relativePath, imageInfo.Caption ?? "", savedIndex);

                // Replace embedded placeholder with actual file path
                result = result.Replace($"![{imageInfo.Caption}](embedded:{imageInfo.Id})", replacement);
                result = result.Replace($"![](embedded:{imageInfo.Id})", replacement);

                if (_verbose)
                {
                    Console.WriteLine($"[Verbose] Extracted: {fileName} ({dimensions.Width}x{dimensions.Height}, {imageInfo.Data.Length} bytes)");
                }
            }
            catch (Exception ex)
            {
                result = result.Replace($"![{imageInfo.Caption}](embedded:{imageInfo.Id})", $"[Image {savedIndex}: {ex.Message}]");
            }
        }

        if (_verbose && skippedCount > 0)
        {
            Console.WriteLine($"[Verbose] Skipped {skippedCount} small images (icons/decorations)");
        }

        return new ImageProcessingResult
        {
            ProcessedContent = result,
            Images = processedImages,
            SkippedCount = skippedCount
        };
    }

    private static string GetFormatFromMimeType(string? mimeType)
    {
        return mimeType?.ToLowerInvariant() switch
        {
            "image/png" => "png",
            "image/jpeg" => "jpg",
            "image/jpg" => "jpg",
            "image/gif" => "gif",
            "image/webp" => "webp",
            "image/bmp" => "bmp",
            _ => "png"
        };
    }

    /// <summary>
    /// Process images in content - extract to files and optionally analyze with AI
    /// </summary>
    public async Task<ImageProcessingResult> ProcessImagesAsync(
        string content,
        string imagesDirectory,
        IImageToTextService? imageToTextService,
        CancellationToken cancellationToken = default)
    {
        var pattern = @"!?\[([^\]]*)\]\(data:image/(\w+);base64,((?>[A-Za-z0-9+/\s]+)=*)\)";
        var regex = new Regex(pattern, RegexOptions.Compiled);

        var result = content;
        var matches = regex.Matches(content);
        var processedImages = new List<ProcessedImage>();
        var skippedCount = 0;
        var imageIndex = 0;

        if (_verbose)
        {
            Console.WriteLine($"[Verbose] Found {matches.Count} images, filters: minSize={_options.MinImageSize}bytes, minDimension={_options.MinImageDimension}px");
        }

        var dirName = Path.GetFileName(imagesDirectory);

        foreach (Match match in matches)
        {
            var altText = match.Groups[1].Value;
            var imageFormat = match.Groups[2].Value.ToLowerInvariant();
            var base64Data = match.Groups[3].Value;

            var cleanBase64 = Regex.Replace(base64Data, @"\s", "");
            var imageBytes = Convert.FromBase64String(cleanBase64);

            // Check file size
            if (imageBytes.Length < _options.MinImageSize)
            {
                skippedCount++;
                if (_verbose)
                {
                    Console.WriteLine($"[Verbose] Skipped: size={imageBytes.Length}bytes < {_options.MinImageSize}bytes");
                }
                result = result.Replace(match.Value, "");
                continue;
            }

            // Check dimensions
            var dimensions = GetImageDimensions(imageBytes, imageFormat);
            if (dimensions.Width < _options.MinImageDimension || dimensions.Height < _options.MinImageDimension)
            {
                skippedCount++;
                if (_verbose)
                {
                    Console.WriteLine($"[Verbose] Skipped: dimensions={dimensions.Width}x{dimensions.Height} < {_options.MinImageDimension}px");
                }
                result = result.Replace(match.Value, "");
                continue;
            }

            imageIndex++;
            var fileName = $"image_{imageIndex}.{imageFormat}";
            var filePath = Path.Combine(imagesDirectory, fileName);

            try
            {
                // Create directory on first image
                if (!Directory.Exists(imagesDirectory))
                {
                    Directory.CreateDirectory(imagesDirectory);
                }

                await File.WriteAllBytesAsync(filePath, imageBytes, cancellationToken).ConfigureAwait(false);

                var processedImage = new ProcessedImage
                {
                    FileName = fileName,
                    FileSize = imageBytes.Length,
                    Width = dimensions.Width,
                    Height = dimensions.Height
                };

                // AI image-to-text if enabled
                if (imageToTextService != null && _options.EnableAI)
                {
                    try
                    {
                        if (_verbose)
                        {
                            Console.WriteLine($"[Verbose] Analyzing image {imageIndex} with AI...");
                        }
                        var aiResult = await imageToTextService.ExtractTextAsync(
                            imageBytes, null, cancellationToken).ConfigureAwait(false);
                        processedImage.AIDescription = aiResult.ExtractedText;
                    }
                    catch (Exception ex)
                    {
                        if (_verbose)
                        {
                            Console.WriteLine($"[Verbose] AI analysis failed: {ex.Message}");
                        }
                        processedImage.AIError = ex.Message;
                    }
                }

                processedImages.Add(processedImage);

                // Build replacement text
                var relativePath = $"./images/{fileName}";
                var replacement = BuildImageReplacement(processedImage, relativePath, altText, imageIndex);
                result = result.Replace(match.Value, replacement);

                if (_verbose)
                {
                    Console.WriteLine($"[Verbose] Extracted: {fileName} ({dimensions.Width}x{dimensions.Height}, {imageBytes.Length} bytes)");
                }
            }
            catch (Exception ex)
            {
                result = result.Replace(match.Value, $"[Image {imageIndex}: {ex.Message}]");
            }
        }

        if (_verbose && skippedCount > 0)
        {
            Console.WriteLine($"[Verbose] Skipped {skippedCount} small images (icons/decorations)");
        }

        return new ImageProcessingResult
        {
            ProcessedContent = result,
            Images = processedImages,
            SkippedCount = skippedCount
        };
    }

    /// <summary>
    /// Remove all base64 images from content (for non-extraction mode)
    /// </summary>
    public static string RemoveBase64Images(string content)
    {
        var pattern = @"!?\[([^\]]*)\]\(data:image/\w+;base64,(?>[A-Za-z0-9+/\s]+)=*\)";
        var regex = new Regex(pattern, RegexOptions.Compiled);
        return regex.Replace(content, "");
    }

    private static string BuildImageReplacement(ProcessedImage image, string relativePath, string altText, int index)
    {
        var sb = new StringBuilder();

        // Image reference
        var displayAlt = string.IsNullOrEmpty(altText) ? $"Image {index}" : altText;
        sb.AppendLine($"![{displayAlt}]({relativePath})");

        // AI description if available
        if (!string.IsNullOrEmpty(image.AIDescription))
        {
            sb.AppendLine();
            sb.AppendLine($"> **AI Analysis**: {image.AIDescription}");
        }

        return sb.ToString();
    }

    private static (int Width, int Height) GetImageDimensions(byte[] imageBytes, string format)
    {
        try
        {
            return format.ToLowerInvariant() switch
            {
                "png" => GetPngDimensions(imageBytes),
                "jpg" or "jpeg" => GetJpegDimensions(imageBytes),
                "gif" => GetGifDimensions(imageBytes),
                "bmp" => GetBmpDimensions(imageBytes),
                "webp" => GetWebpDimensions(imageBytes),
                _ => (0, 0)
            };
        }
        catch
        {
            return (0, 0);
        }
    }

    private static (int Width, int Height) GetPngDimensions(byte[] data)
    {
        if (data.Length < 24) return (0, 0);
        var width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
        var height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
        return (width, height);
    }

    private static (int Width, int Height) GetJpegDimensions(byte[] data)
    {
        if (data.Length < 2) return (0, 0);

        var i = 2;
        while (i < data.Length - 1)
        {
            if (data[i] != 0xFF) break;

            var marker = data[i + 1];
            if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2)
            {
                if (i + 9 >= data.Length) return (0, 0);
                var height = (data[i + 5] << 8) | data[i + 6];
                var width = (data[i + 7] << 8) | data[i + 8];
                return (width, height);
            }

            if (i + 3 >= data.Length) break;
            var segmentLength = (data[i + 2] << 8) | data[i + 3];
            i += 2 + segmentLength;
        }
        return (0, 0);
    }

    private static (int Width, int Height) GetGifDimensions(byte[] data)
    {
        if (data.Length < 10) return (0, 0);
        var width = data[6] | (data[7] << 8);
        var height = data[8] | (data[9] << 8);
        return (width, height);
    }

    private static (int Width, int Height) GetBmpDimensions(byte[] data)
    {
        if (data.Length < 26) return (0, 0);
        var width = data[18] | (data[19] << 8) | (data[20] << 16) | (data[21] << 24);
        var height = data[22] | (data[23] << 8) | (data[24] << 16) | (data[25] << 24);
        return (width, Math.Abs(height));
    }

    private static (int Width, int Height) GetWebpDimensions(byte[] data)
    {
        if (data.Length < 30) return (0, 0);
        if (data[12] == 'V' && data[13] == 'P' && data[14] == '8' && data[15] == ' ')
        {
            var width = ((data[26] | (data[27] << 8)) & 0x3FFF);
            var height = ((data[28] | (data[29] << 8)) & 0x3FFF);
            return (width, height);
        }
        return (0, 0);
    }
}

/// <summary>
/// Result of image processing
/// </summary>
public class ImageProcessingResult
{
    public string ProcessedContent { get; set; } = string.Empty;
    public List<ProcessedImage> Images { get; set; } = new();
    public int SkippedCount { get; set; }
}
