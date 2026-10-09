using FileFlux;
using FileFlux.Domain;
using OpenAI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FileFlux.Tests.Services;

/// <summary>
/// Test용 OpenAI 텍스트 완성 서비스 - RAG 품질 벤치마크용
/// </summary>
public class TestOpenAITextGenerationService : IDocumentAnalysisService
{
    private readonly ChatClient _chatClient;

    public TestOpenAITextGenerationService(string apiKey, string model = "gpt-5-nano")
    {
        if (string.IsNullOrEmpty(apiKey))
            throw new ArgumentException("API key is required", nameof(apiKey));

        var openAiClient = new OpenAIClient(apiKey);
        _chatClient = openAiClient.GetChatClient(model);
    }

    public DocumentAnalysisServiceInfo ProviderInfo => new()
    {
        Name = "OpenAI (Test)",
        Type = DocumentAnalysisProviderType.OpenAI,
        SupportedModels = new[] { "gpt-5-nano", "gpt-4o" },
        MaxContextLength = 128000,
        InputTokenCost = 0.00015m,
        OutputTokenCost = 0.0006m,
        ApiVersion = "2024-08-01"
    };

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
                MaxOutputTokenCount = 1000
            }, cancellationToken);

            return response.Value?.Content?.FirstOrDefault()?.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"OpenAI API call failed: {ex.Message}", ex);
        }
    }

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
}