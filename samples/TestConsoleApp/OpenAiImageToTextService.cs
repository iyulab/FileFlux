using FileFlux;
using OpenAI;
using OpenAI.Chat;

namespace TestConsoleApp;

/// <summary>
/// TestConsoleApp용 OpenAI Vision API 구현
/// FileFlux SampleApp의 구현을 참조하되 TestConsoleApp에 맞게 단순화
/// </summary>
public class OpenAiImageToTextService : IImageToTextService
{
    private readonly OpenAIClient _client;

    public IEnumerable<string> SupportedImageFormats => new[]
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp"
    };

    public string ProviderName => "OpenAI-Vision";

    public OpenAiImageToTextService(string apiKey)
    {
        _client = new OpenAIClient(apiKey);
    }

    public async Task<ImageToTextResult> ExtractTextAsync(
        byte[] imageData,
        ImageToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            var chatClient = _client.GetChatClient("gpt-5-nano");

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("이미지에서 텍스트를 추출해주세요. 모든 읽을 수 있는 텍스트를 정확하게 추출해주세요."),
                new UserChatMessage(ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(imageData), "image/jpeg"))
            };

            var response = await chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                MaxOutputTokenCount = 1000,
                Temperature = 0.1f
            }, cancellationToken);

            var extractedText = response.Value.Content.First().Text;
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            var metadata = new ImageMetadata
            {
                Format = DetectImageFormat(imageData),
                FileSize = imageData.Length,
                Width = 0,
                Height = 0,
                Dpi = 96,
                ColorSpace = "RGB"
            };

            return new ImageToTextResult
            {
                ExtractedText = $"<!-- IMAGE_START -->\n{extractedText}\n<!-- IMAGE_END -->",
                ConfidenceScore = 0.85,
                DetectedLanguage = options?.Language == "auto" ? "ko" : options?.Language ?? "ko",
                ImageType = DetermineImageType(options?.ImageTypeHint),
                ProcessingTimeMs = processingTime,
                Metadata = metadata,
                StructuralElements = new List<StructuralElement>()
            };
        }
        catch (Exception ex)
        {
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            return new ImageToTextResult
            {
                ExtractedText = $"<!-- IMAGE_PROCESSING_ERROR -->\nFailed to process image: {ex.Message}\n<!-- /IMAGE_PROCESSING_ERROR -->",
                ConfidenceScore = 0.0,
                DetectedLanguage = "ko",
                ImageType = "unknown",
                ProcessingTimeMs = processingTime,
                Metadata = new ImageMetadata
                {
                    Format = "unknown",
                    FileSize = imageData.Length,
                    Width = 0,
                    Height = 0,
                    Dpi = 0,
                    ColorSpace = "unknown"
                },
                StructuralElements = new List<StructuralElement>()
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
        return await ExtractTextAsync(memoryStream.ToArray(), options, cancellationToken);
    }

    public async Task<ImageToTextResult> ExtractTextAsync(
        string imagePath,
        ImageToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"Image file not found: {imagePath}");

        var imageData = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        return await ExtractTextAsync(imageData, options, cancellationToken);
    }

    private static string DetermineImageType(string? hint)
    {
        return hint switch
        {
            "chart" => "chart",
            "table" => "table",
            "document" => "document",
            "photo" => "photo",
            _ => "document"
        };
    }

    private static string DetectImageFormat(byte[] imageData)
    {
        if (imageData.Length < 8) return "unknown";

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (imageData[0] == 0x89 && imageData[1] == 0x50 && imageData[2] == 0x4E && imageData[3] == 0x47)
            return "PNG";

        // JPEG: FF D8 FF
        if (imageData[0] == 0xFF && imageData[1] == 0xD8 && imageData[2] == 0xFF)
            return "JPEG";

        // GIF: GIF87a or GIF89a
        if (imageData[0] == 0x47 && imageData[1] == 0x49 && imageData[2] == 0x46)
            return "GIF";

        // BMP: 42 4D
        if (imageData[0] == 0x42 && imageData[1] == 0x4D)
            return "BMP";

        // WebP: RIFF ... WEBP
        if (imageData.Length >= 12 &&
            imageData[0] == 0x52 && imageData[1] == 0x49 && imageData[2] == 0x46 && imageData[3] == 0x46 &&
            imageData[8] == 0x57 && imageData[9] == 0x45 && imageData[10] == 0x42 && imageData[11] == 0x50)
            return "WEBP";

        return "unknown";
    }
}