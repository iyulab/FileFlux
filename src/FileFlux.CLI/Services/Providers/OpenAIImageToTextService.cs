using FileFlux;
using FileFlux.Domain;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace FileFlux.CLI.Services.Providers;

/// <summary>
/// OpenAI Vision API implementation for image to text extraction
/// Uses multimodal models like GPT-4-vision, GPT-5-nano for image analysis
/// </summary>
public class OpenAIImageToTextService : IImageToTextService
{
    private readonly ChatClient _chatClient;
    private readonly string _model;

    private static readonly string[] SupportedFormats = new[]
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"
    };

    public OpenAIImageToTextService(string apiKey, string model, string? endpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        OpenAIClient client;
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            // Custom endpoint for OpenAI-compatible APIs (e.g., GPU-Stack)
            var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
            client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
        }
        else
        {
            client = new OpenAIClient(apiKey);
        }
        _chatClient = client.GetChatClient(model);
        _model = model;
    }

    public IEnumerable<string> SupportedImageFormats => SupportedFormats;

    public string ProviderName => $"OpenAI-Vision ({_model})";

    public async Task<ImageToTextResult> ExtractTextAsync(
        byte[] imageData,
        ImageToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        options ??= new ImageToTextOptions();

        try
        {
            Console.WriteLine($"[OpenAI-Vision] Processing image: {imageData.Length} bytes");

            // Build prompt based on options (using Core prompt builder)
            var prompt = ImageExtractionPromptBuilder.BuildPrompt(options);
            Console.WriteLine($"[OpenAI-Vision] Prompt: {prompt}");

            // Create messages with image using BinaryData (avoids URI length limit)
            var imageContent = BinaryData.FromBytes(imageData);
            var messages = new List<ChatMessage>
            {
                new UserChatMessage(
                    ChatMessageContentPart.CreateTextPart(prompt),
                    ChatMessageContentPart.CreateImagePart(imageContent, "image/jpeg")
                )
            };

            Console.WriteLine($"[OpenAI-Vision] Calling OpenAI API with model: {_model}");

            // Call OpenAI Vision API
            // Note: Some models (like gpt-5-nano) don't support temperature parameter
            var response = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                MaxOutputTokenCount = 4000
            }, cancellationToken);

            Console.WriteLine($"[OpenAI-Vision] Response received, processing...");

            var rawResponse = response.Value?.Content?.FirstOrDefault()?.Text ?? string.Empty;
            Console.WriteLine($"[OpenAI-Vision] Raw response length: {rawResponse.Length} characters");

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

                Console.WriteLine($"[OpenAI-Vision] Extraction failed: {failureReason}");

                extractedText = string.Empty;
                errorMessage = failureReason;
                confidenceScore = 0.0;
            }
            else if (string.IsNullOrWhiteSpace(rawResponse))
            {
                Console.WriteLine($"[OpenAI-Vision] WARNING: Empty response from OpenAI");
                Console.WriteLine($"[OpenAI-Vision] Response value: {response.Value}");
                Console.WriteLine($"[OpenAI-Vision] Content parts count: {response.Value?.Content?.Count ?? 0}");

                extractedText = string.Empty;
                errorMessage = "Vision API returned empty response";
                confidenceScore = 0.0;
            }
            else
            {
                Console.WriteLine($"[OpenAI-Vision] Extraction successful: {rawResponse.Length} characters");

                extractedText = rawResponse;
                errorMessage = null;
                confidenceScore = 0.85; // OpenAI doesn't provide confidence scores
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
                    Format = "image/jpeg"
                }
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OpenAI-Vision] ERROR: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[OpenAI-Vision] Stack trace: {ex.StackTrace}");

            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            return new ImageToTextResult
            {
                ExtractedText = string.Empty,
                ConfidenceScore = 0.0,
                ErrorMessage = $"OpenAI Vision API error: {ex.Message}",
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
}
