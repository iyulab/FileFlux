using FileFlux;
using FileFlux.Domain;
using CoreDocumentStructure = FileFlux.CoreDocumentStructure;
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
public class TestOpenAiTextCompletionService : ITextCompletionService
{
    private readonly ChatClient _chatClient;

    public TestOpenAiTextCompletionService(string apiKey, string model = "gpt-4o-mini")
    {
        if (string.IsNullOrEmpty(apiKey))
            throw new ArgumentException("API key is required", nameof(apiKey));

        var openAiClient = new OpenAIClient(apiKey);
        _chatClient = openAiClient.GetChatClient(model);
    }

    public TextCompletionServiceInfo ProviderInfo => new()
    {
        Name = "OpenAI (Test)",
        Type = TextCompletionProviderType.OpenAI,
        SupportedModels = new[] { "gpt-4o-mini", "gpt-4o", "gpt-3.5-turbo" },
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

    // Simplified implementations for testing - just return basic results
    public async Task<StructureAnalysisResult> AnalyzeStructureAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        var response = await GenerateAsync(prompt, cancellationToken);
        return new StructureAnalysisResult
        {
            DocumentType = documentType,
            Confidence = 0.8,
            RawResponse = response,
            TokensUsed = response.Length / 4 // Rough estimate
        };
    }

    public async Task<ContentSummary> SummarizeContentAsync(
        string prompt,
        int maxLength = 200,
        CancellationToken cancellationToken = default)
    {
        var response = await GenerateAsync($"Summarize in {maxLength} characters: {prompt}", cancellationToken);
        return new ContentSummary
        {
            Summary = response.Length > maxLength ? response.Substring(0, maxLength) : response,
            Keywords = Array.Empty<string>(),
            Confidence = 0.8,
            OriginalLength = prompt.Length,
            TokensUsed = response.Length / 4
        };
    }

    public async Task<MetadataExtractionResult> ExtractMetadataAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        var response = await GenerateAsync(prompt, cancellationToken);
        return new MetadataExtractionResult
        {
            Keywords = new[] { "test", "keyword" },
            Language = "en",
            Categories = new[] { "test" },
            Confidence = 0.8,
            TokensUsed = response.Length / 4
        };
    }

    public async Task<QualityAssessment> AssessQualityAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var response = await GenerateAsync(prompt, cancellationToken);
        return new QualityAssessment
        {
            ConfidenceScore = 0.8,
            CompletenessScore = 0.8,
            ConsistencyScore = 0.8,
            Explanation = response,
            TokensUsed = response.Length / 4
        };
    }
}