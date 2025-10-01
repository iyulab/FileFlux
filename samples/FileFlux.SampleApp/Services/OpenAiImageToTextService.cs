using FileFlux;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace FileFlux.SampleApp.Services;

/// <summary>
/// OpenAI Vision API를 사용하는 실제 Image-to-Text 서비스 구현
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
            // Base64로 이미지 인코딩
            var base64Image = Convert.ToBase64String(imageData);
            var imageUrl = $"data:image/jpeg;base64,{base64Image}";

            // 이미지 타입에 따른 프롬프트 생성
            var prompt = GeneratePrompt(options?.ImageTypeHint, options?.ExtractStructure ?? false);

            var chatClient = _client.GetChatClient("gpt-5-nano");
            
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(prompt),
                new UserChatMessage(ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(imageData), "image/jpeg"))
            };

            var response = await chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                MaxOutputTokenCount = 1000,
                Temperature = 0.1f // 일관된 결과를 위해 낮은 온도
            }, cancellationToken);

            var extractedText = response.Value.Content.First().Text;
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            // 메타데이터 추출
            var metadata = new ImageMetadata
            {
                Format = DetectImageFormat(imageData),
                FileSize = imageData.Length,
                Width = 0, // OpenAI Vision에서는 직접 제공하지 않음
                Height = 0,
                Dpi = 96,
                ColorSpace = "RGB"
            };

            return new ImageToTextResult
            {
                ExtractedText = extractedText,
                ConfidenceScore = 0.85, // OpenAI Vision 일반적 신뢰도
                DetectedLanguage = options?.Language == "auto" ? "en" : options?.Language ?? "en",
                ImageType = DetermineImageType(options?.ImageTypeHint),
                ProcessingTimeMs = processingTime,
                Metadata = metadata,
                StructuralElements = new List<StructuralElement>() // 실제 구조 분석은 추가 프롬프팅 필요
            };
        }
        catch (Exception ex)
        {
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            
            return new ImageToTextResult
            {
                ExtractedText = $"<!-- IMAGE_PROCESSING_ERROR -->\\nFailed to process image: {ex.Message}\\n<!-- /IMAGE_PROCESSING_ERROR -->",
                ConfidenceScore = 0.0,
                DetectedLanguage = "en",
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

    private static string GeneratePrompt(string? imageTypeHint, bool extractStructure)
    {
        var basePrompt = "Extract all visible text from this image. ";

        return imageTypeHint switch
        {
            "chart" => basePrompt + "Focus on data values, labels, titles, and trends. Format as structured data with clear labels.",
            "table" => basePrompt + "Preserve table structure using markdown format with | separators. Include headers and maintain row alignment.",
            "document" => basePrompt + "Maintain document structure with headings, paragraphs, and lists. Preserve formatting hierarchy.",
            "photo" => basePrompt + "Extract any visible text including signs, labels, documents, or written content in the image.",
            _ => basePrompt + "Return the text in a clean, readable format preserving the original structure as much as possible."
        };
    }

    private static string DetermineImageType(string? hint)
    {
        return hint switch
        {
            "chart" => "chart",
            "table" => "table",
            "document" => "document", 
            "photo" => "photo",
            _ => "general"
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