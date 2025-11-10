using FileFlux;
using FileFlux.Domain;
using OpenAI;
using OpenAI.Chat;

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

    public OpenAIImageToTextService(string apiKey, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        var client = new OpenAIClient(apiKey);
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

            // Build prompt based on options
            var prompt = BuildPrompt(options);
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

            var extractedText = response.Value?.Content?.FirstOrDefault()?.Text ?? string.Empty;
            Console.WriteLine($"[OpenAI-Vision] Extracted content length: {extractedText.Length} characters");

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                Console.WriteLine($"[OpenAI-Vision] WARNING: Empty response from OpenAI");
                Console.WriteLine($"[OpenAI-Vision] Response value: {response.Value}");
                Console.WriteLine($"[OpenAI-Vision] Content parts count: {response.Value?.Content?.Count ?? 0}");
            }

            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            return new ImageToTextResult
            {
                ExtractedText = extractedText,
                ConfidenceScore = 0.85, // OpenAI doesn't provide confidence scores
                DetectedLanguage = options.Language == "auto" ? "unknown" : options.Language,
                ImageType = options.ImageTypeHint ?? "unknown",
                ProcessingTimeMs = processingTime,
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

    private static string BuildPrompt(ImageToTextOptions options)
    {
        var prompt = "Analyze this image and provide the following:\n";
        prompt += "1. If there is readable text (OCR): Extract all visible text content\n";
        prompt += "2. If there is no readable text: Describe the visual content of the image (objects, scenes, activities, etc.)\n";
        prompt += "You MUST return either extracted text OR image description - never return empty content.";

        if (options.ExtractStructure)
        {
            prompt += " When extracting text, preserve the structure and layout (tables, lists, headings, etc.).";
        }

        if (!string.IsNullOrWhiteSpace(options.ImageTypeHint))
        {
            prompt += options.ImageTypeHint switch
            {
                "chart" => " This is a chart/graph - extract titles, labels, data values, or describe the chart type and trends.",
                "table" => " This is a table - extract headers and cell values in structured format, or describe the table structure.",
                "document" => " This is a document page - extract all text while preserving formatting.",
                "diagram" => " This is a diagram - extract text labels and describe visual elements and their relationships.",
                _ => ""
            };
        }

        if (options.Quality == "high")
        {
            prompt += " Provide detailed and accurate extraction/description with high precision.";
        }

        prompt += "\n\nIMPORTANT: Return the content directly without any introductory phrases like 'This image shows' or 'The text says'.";

        return prompt;
    }
}
