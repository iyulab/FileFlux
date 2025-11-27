using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Runtime.CompilerServices;
using FI = FluxImprover.Abstractions.Services;

namespace FileFlux.CLI.Services.Providers.FluxImprover;

/// <summary>
/// OpenAI implementation of FluxImprover's ITextCompletionService
/// </summary>
public class OpenAICompletionService : FI.ITextCompletionService
{
    private readonly ChatClient _chatClient;

    public OpenAICompletionService(string apiKey, string model, string? endpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        OpenAIClient client;
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
            client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
        }
        else
        {
            client = new OpenAIClient(apiKey);
        }
        _chatClient = client.GetChatClient(model);
    }

    public async Task<string> CompleteAsync(
        string prompt,
        FI.CompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(options?.SystemPrompt))
        {
            messages.Add(new SystemChatMessage(options.SystemPrompt));
        }

        if (options?.Messages is { Count: > 0 })
        {
            foreach (var msg in options.Messages)
            {
                messages.Add(msg.Role.ToLowerInvariant() switch
                {
                    "system" => new SystemChatMessage(msg.Content),
                    "assistant" => new AssistantChatMessage(msg.Content),
                    _ => new UserChatMessage(msg.Content)
                });
            }
        }

        messages.Add(new UserChatMessage(prompt));

        var chatOptions = new ChatCompletionOptions();

        // Only set temperature if explicitly provided (nullable support for model compatibility)
        if (options?.Temperature.HasValue == true)
        {
            chatOptions.Temperature = options.Temperature.Value;
        }

        // WORKAROUND: gpt-5-nano has a bug where setting max_completion_tokens
        // with response_format: json_object causes empty responses.
        // Only set MaxOutputTokenCount when NOT using JsonMode.
        if (options?.MaxTokens.HasValue == true && options.JsonMode != true)
        {
            chatOptions.MaxOutputTokenCount = options.MaxTokens.Value;
        }

        if (options?.JsonMode == true)
        {
            chatOptions.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat();
        }

        var response = await _chatClient.CompleteChatAsync(messages, chatOptions, cancellationToken);
        return response.Value?.Content?.FirstOrDefault()?.Text ?? string.Empty;
    }

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        string prompt,
        FI.CompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(options?.SystemPrompt))
        {
            messages.Add(new SystemChatMessage(options.SystemPrompt));
        }

        if (options?.Messages is { Count: > 0 })
        {
            foreach (var msg in options.Messages)
            {
                messages.Add(msg.Role.ToLowerInvariant() switch
                {
                    "system" => new SystemChatMessage(msg.Content),
                    "assistant" => new AssistantChatMessage(msg.Content),
                    _ => new UserChatMessage(msg.Content)
                });
            }
        }

        messages.Add(new UserChatMessage(prompt));

        var chatOptions = new ChatCompletionOptions();

        // Only set temperature if explicitly provided (nullable support for model compatibility)
        if (options?.Temperature.HasValue == true)
        {
            chatOptions.Temperature = options.Temperature.Value;
        }

        if (options?.MaxTokens.HasValue == true)
        {
            chatOptions.MaxOutputTokenCount = options.MaxTokens.Value;
        }

        await foreach (var update in _chatClient.CompleteChatStreamingAsync(messages, chatOptions, cancellationToken))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    yield return part.Text;
                }
            }
        }
    }
}
