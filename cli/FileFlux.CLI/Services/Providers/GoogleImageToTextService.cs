using FileFlux;
using FileFlux.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileFlux.CLI.Services.Providers;

/// <summary>
/// Google Gemini Vision API implementation for image to text extraction using direct HTTP API
/// Uses Gemini models with vision capabilities (gemini-2.5-flash, gemini-2.5-pro)
/// </summary>
public class GoogleImageToTextService : IImageToTextService, IDisposable
{
    private static readonly JsonSerializerOptions s_serializeOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly JsonSerializerOptions s_deserializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly bool _verbose;
    private const string ApiEndpoint = "https://generativelanguage.googleapis.com/v1beta/models";

    private static readonly string[] SupportedFormats = new[]
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".heic", ".heif"
    };

    public GoogleImageToTextService(string apiKey, string model, bool verbose = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
        _model = model;
        _verbose = verbose;
    }

    public IEnumerable<string> SupportedImageFormats => SupportedFormats;

    public string ProviderName => $"Google-Gemini-Vision ({_model})";

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
            var mimeType = GetMimeType(imageData);

            // Build prompt based on options (using Core prompt builder)
            var prompt = ImageExtractionPromptBuilder.BuildPrompt(options);

            if (_verbose)
            {
                Console.WriteLine($"[Google Vision] Processing image ({imageData.Length} bytes, {mimeType})");
                Console.WriteLine($"[Google Vision] Prompt: {prompt[..Math.Min(100, prompt.Length)]}...");
            }

            // Create multimodal request with image
            var request = new GeminiVisionRequest
            {
                Contents = new[]
                {
                    new GeminiContent
                    {
                        Parts = new GeminiPart[]
                        {
                            new GeminiPart
                            {
                                InlineData = new GeminiInlineData
                                {
                                    MimeType = mimeType,
                                    Data = base64Image
                                }
                            },
                            new GeminiPart
                            {
                                Text = prompt
                            }
                        }
                    }
                },
                GenerationConfig = new GeminiGenerationConfig
                {
                    Temperature = 0.1,
                    MaxOutputTokens = 4000
                }
            };

            var response = await PostAsync<GeminiResponse>(request, cancellationToken);

            var rawResponse = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;

            if (_verbose)
            {
                Console.WriteLine($"[Google Vision] Response length: {rawResponse.Length} chars");
            }

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
                confidenceScore = 0.88; // Gemini has high vision accuracy
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
                    Format = mimeType
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
                ErrorMessage = $"Google Gemini Vision API error: {ex.Message}",
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

    private async Task<T> PostAsync<T>(GeminiVisionRequest requestBody, CancellationToken cancellationToken)
    {
        var url = $"{ApiEndpoint}/{_model}:generateContent";
        var json = JsonSerializer.Serialize(requestBody, s_serializeOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(responseJson, s_deserializeOptions)
            ?? throw new InvalidOperationException("Failed to deserialize response");
    }

    private static string GetMimeType(byte[] imageData)
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

        // HEIC/HEIF: Check for ftyp box with heic/heif brand
        if (imageData.Length >= 12 &&
            imageData[4] == 0x66 && imageData[5] == 0x74 && imageData[6] == 0x79 && imageData[7] == 0x70)
        {
            // Check brand
            if (imageData[8] == 0x68 && imageData[9] == 0x65 && imageData[10] == 0x69)
            {
                return "image/heic";
            }
        }

        // Default to JPEG
        return "image/jpeg";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    // DTOs for Google Gemini Vision API
    private class GeminiVisionRequest
    {
        [JsonPropertyName("contents")]
        public GeminiContent[]? Contents { get; set; }

        [JsonPropertyName("generationConfig")]
        public GeminiGenerationConfig? GenerationConfig { get; set; }
    }

    private class GeminiContent
    {
        [JsonPropertyName("parts")]
        public GeminiPart[]? Parts { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }
    }

    private class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("inlineData")]
        public GeminiInlineData? InlineData { get; set; }
    }

    private class GeminiInlineData
    {
        [JsonPropertyName("mimeType")]
        public string? MimeType { get; set; }

        [JsonPropertyName("data")]
        public string? Data { get; set; }
    }

    private class GeminiGenerationConfig
    {
        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        [JsonPropertyName("maxOutputTokens")]
        public int? MaxOutputTokens { get; set; }
    }

    private class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public GeminiCandidate[]? Candidates { get; set; }

        [JsonPropertyName("usageMetadata")]
        public GeminiUsageMetadata? UsageMetadata { get; set; }
    }

    private class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }

        [JsonPropertyName("finishReason")]
        public string? FinishReason { get; set; }
    }

    private class GeminiUsageMetadata
    {
        [JsonPropertyName("promptTokenCount")]
        public int PromptTokenCount { get; set; }

        [JsonPropertyName("candidatesTokenCount")]
        public int CandidatesTokenCount { get; set; }

        [JsonPropertyName("totalTokenCount")]
        public int TotalTokenCount { get; set; }
    }
}
