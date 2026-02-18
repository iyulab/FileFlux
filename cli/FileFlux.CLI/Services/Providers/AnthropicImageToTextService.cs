using FileFlux;
using FileFlux.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileFlux.CLI.Services.Providers;

/// <summary>
/// Anthropic Claude Vision API implementation for image to text extraction using direct HTTP API
/// Uses Claude 3.5 Sonnet, Claude 3 Opus with vision capabilities
/// </summary>
public class AnthropicImageToTextService : IImageToTextService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private const string ApiEndpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private static readonly string[] SupportedFormats = new[]
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp"
    };

    public AnthropicImageToTextService(string apiKey, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
        _model = model;
    }

    public IEnumerable<string> SupportedImageFormats => SupportedFormats;

    public string ProviderName => $"Anthropic-Vision ({_model})";

    public async Task<ImageToTextResult> ExtractTextAsync(
        byte[] imageData,
        ImageToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        options ??= new ImageToTextOptions();

        try
        {
            // Convert image to base64
            var base64Image = Convert.ToBase64String(imageData);
            var mediaType = GetMediaType(imageData);

            // Build prompt based on options (using Core prompt builder)
            var prompt = ImageExtractionPromptBuilder.BuildPrompt(options);

            // Create message with image using Vision API
            var request = new
            {
                model = _model,
                max_tokens = 4000,
                temperature = 0.1,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "image",
                                source = new
                                {
                                    type = "base64",
                                    media_type = mediaType,
                                    data = base64Image
                                }
                            },
                            new
                            {
                                type = "text",
                                text = prompt
                            }
                        }
                    }
                }
            };

            var response = await PostAsync<AnthropicResponse>(request, cancellationToken);

            var rawResponse = response.Content?.FirstOrDefault()?.Text ?? string.Empty;

            // Check for extraction failure
            const string failurePrefix = "EXTRACTION_FAILED:";
            bool extractionFailed = rawResponse.TrimStart().StartsWith(failurePrefix, StringComparison.OrdinalIgnoreCase);

            string extractedText;
            string? errorMessage = null;
            double confidenceScore;

            if (extractionFailed)
            {
                // Extract failure reason
                var failureReason = rawResponse.TrimStart();
                if (failureReason.StartsWith(failurePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    failureReason = failureReason.Substring(failurePrefix.Length).Trim();
                }

                extractedText = string.Empty;
                errorMessage = failureReason;
                confidenceScore = 0.0;
            }
            else if (string.IsNullOrWhiteSpace(rawResponse))
            {
                extractedText = string.Empty;
                errorMessage = "Vision API returned empty response";
                confidenceScore = 0.0;
            }
            else
            {
                extractedText = rawResponse;
                errorMessage = null;
                confidenceScore = 0.90; // Claude has high vision accuracy
            }

            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            return new ImageToTextResult
            {
                ExtractedText = extractedText,
                ConfidenceScore = confidenceScore,
                DetectedLanguage = options.Language == "auto" ? "unknown" : options.Language,
                ImageType = options.ImageTypeHint ?? "unknown",
                ProcessingTimeMs = processingTime,
                ErrorMessage = errorMessage,
                Metadata = new ImageMetadata
                {
                    FileSize = imageData.Length,
                    Format = mediaType
                }
            };
        }
        catch (Exception ex)
        {
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            return new ImageToTextResult
            {
                ExtractedText = string.Empty,
                ConfidenceScore = 0.0,
                ErrorMessage = $"Anthropic Vision API error: {ex.Message}",
                ProcessingTimeMs = processingTime
            };
        }
    }

    public async Task<ImageToTextResult> ExtractTextAsync(
        Stream imageStream,
        ImageToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var memoryStream = new MemoryStream();
        await imageStream.CopyToAsync(memoryStream, cancellationToken);
        var imageData = memoryStream.ToArray();
        return await ExtractTextAsync(imageData, options, cancellationToken);
    }

    public async Task<ImageToTextResult> ExtractTextAsync(
        string imagePath,
        ImageToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(imagePath))
        {
            return new ImageToTextResult
            {
                ExtractedText = string.Empty,
                ConfidenceScore = 0.0,
                ErrorMessage = $"Image file not found: {imagePath}"
            };
        }

        var imageData = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        return await ExtractTextAsync(imageData, options, cancellationToken);
    }

    private async Task<T> PostAsync<T>(object requestBody, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(ApiEndpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(responseJson) ?? throw new InvalidOperationException("Failed to deserialize response");
    }

    private static string GetMediaType(byte[] imageData)
    {
        // Check image signature (magic bytes)
        if (imageData.Length < 4)
        {
            return "image/jpeg"; // default
        }

        // PNG: 89 50 4E 47
        if (imageData[0] == 0x89 && imageData[1] == 0x50 && imageData[2] == 0x4E && imageData[3] == 0x47)
        {
            return "image/png";
        }

        // JPEG: FF D8 FF
        if (imageData[0] == 0xFF && imageData[1] == 0xD8 && imageData[2] == 0xFF)
        {
            return "image/jpeg";
        }

        // GIF: 47 49 46
        if (imageData[0] == 0x47 && imageData[1] == 0x49 && imageData[2] == 0x46)
        {
            return "image/gif";
        }

        // WebP: 52 49 46 46 ... 57 45 42 50
        if (imageData.Length >= 12 &&
            imageData[0] == 0x52 && imageData[1] == 0x49 && imageData[2] == 0x46 && imageData[3] == 0x46 &&
            imageData[8] == 0x57 && imageData[9] == 0x45 && imageData[10] == 0x42 && imageData[11] == 0x50)
        {
            return "image/webp";
        }

        // Default to JPEG
        return "image/jpeg";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    // DTOs for Anthropic API
    private class AnthropicResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public List<AnthropicContent>? Content { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("usage")]
        public AnthropicUsage? Usage { get; set; }
    }

    private class AnthropicContent
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private class AnthropicUsage
    {
        [JsonPropertyName("input_tokens")]
        public int InputTokens { get; set; }

        [JsonPropertyName("output_tokens")]
        public int OutputTokens { get; set; }
    }
}
