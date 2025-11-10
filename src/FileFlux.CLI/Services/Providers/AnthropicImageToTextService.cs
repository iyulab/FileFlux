using FileFlux;
using FileFlux.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileFlux.CLI.Services.Providers;

/// <summary>
/// Anthropic Claude Vision API implementation for image to text extraction using direct HTTP API
/// Uses Claude 3.5 Sonnet, Claude 3 Opus with vision capabilities
/// </summary>
public class AnthropicImageToTextService : IImageToTextService
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

            // Build prompt based on options
            var prompt = BuildPrompt(options);

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

            var extractedText = response.Content?.FirstOrDefault()?.Text ?? string.Empty;
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            return new ImageToTextResult
            {
                ExtractedText = extractedText,
                ConfidenceScore = 0.90, // Claude has high vision accuracy
                DetectedLanguage = options.Language == "auto" ? "unknown" : options.Language,
                ImageType = options.ImageTypeHint ?? "unknown",
                ProcessingTimeMs = processingTime,
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

    private static string BuildPrompt(ImageToTextOptions options)
    {
        var prompt = "Analyze this image and extract all visible text content.";

        if (options.ExtractStructure)
        {
            prompt += " Preserve the structure and layout of the text (tables, lists, headings, etc.).";
        }

        if (!string.IsNullOrWhiteSpace(options.ImageTypeHint))
        {
            prompt += options.ImageTypeHint switch
            {
                "chart" => " This is a chart or graph - extract titles, labels, legend, and data values.",
                "table" => " This is a table - extract headers and all cell values in a structured format.",
                "document" => " This is a document page - extract all text while preserving formatting.",
                "diagram" => " This is a diagram - describe the visual elements and extract any text labels.",
                _ => ""
            };
        }

        if (options.Quality == "high")
        {
            prompt += " Provide detailed and accurate extraction with high precision. Pay special attention to small text and details.";
        }

        prompt += " Return only the extracted text content without any additional commentary or explanation.";

        return prompt;
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
