using FileFlux;
using OpenAI;
using OpenAI.Chat;

namespace FileFlux.SampleApp.Services;

/// <summary>
/// SampleApp용 OpenAI 텍스트 완성 서비스 구현
/// FileFlux의 역할 분리 원칙에 따라 소비 애플리케이션에서 구현
/// </summary>
public class OpenAITextGenerationService : IDocumentAnalysisService
{
    private readonly ChatClient _chatClient;

    public OpenAITextGenerationService(ChatClient chatClient)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
    }

    public DocumentAnalysisServiceInfo ProviderInfo => new()
    {
        Name = "OpenAI",
        Type = DocumentAnalysisProviderType.OpenAI,
        SupportedModels = new[] { "gpt-5-nano", "gpt-4o" },
        MaxContextLength = 128000,
        InputTokenCost = 0.00015m, // gpt-5-nano 가격
        OutputTokenCost = 0.0006m,
        ApiVersion = "2024-08-01"
    };

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = new List<ChatMessage>
            {
                new UserChatMessage("Hello")
            };

            var response = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                MaxOutputTokenCount = 10
            }, cancellationToken);

            return response.Value != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 범용 LLM 호출 (BasicDocumentParser 호환성을 위한 간단한 인터페이스)
    /// </summary>
    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = new List<ChatMessage>
            {
                new UserChatMessage(prompt)
            };

            var response = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                Temperature = 0.7f,
                MaxOutputTokenCount = 1000
            }, cancellationToken);

            return response.Value?.Content?.FirstOrDefault()?.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"OpenAI LLM 호출 실패: {ex.Message}", ex);
        }
    }
}