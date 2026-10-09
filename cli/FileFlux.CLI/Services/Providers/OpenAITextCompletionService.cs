using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace FileFlux.CLI.Services.Providers;

/// <summary>
/// OpenAI text completion service implementation for CLI
/// </summary>
public class OpenAIDocumentAnalysisService : IDocumentAnalysisService
{
    private readonly ChatClient _chatClient;
    private readonly string _model;

    public OpenAIDocumentAnalysisService(string apiKey, string model, string? endpoint = null)
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

    public DocumentAnalysisServiceInfo ProviderInfo => new()
    {
        Name = "OpenAI",
        Type = DocumentAnalysisProviderType.OpenAI,
        SupportedModels = [_model],
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
                Temperature = 0.7f,
                MaxOutputTokenCount = 1000
            }, cancellationToken);

            return response.Value?.Content?.FirstOrDefault()?.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"OpenAI generation failed: {ex.Message}", ex);
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
