using System.Text.Json;
using AwesomeAssertions;
using FileFlux.Core;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FileFlux.Tests.Services;

/// <summary>
/// A refinement pass rewrites the whole text. With the service default of 1000 output tokens, every document past
/// roughly 4 KB came back cut off, and the cut-off text — being shorter — passed the "noise removed" / "duplicates
/// merged" gates and replaced the document. These pin that a truncated response is never adopted, that the output
/// budget is sized from the text, and that the skip is reported.
/// </summary>
public sealed class LlmRefinerTruncationTests : IDisposable
{
    private readonly OpenAICompatibleDocumentAnalysisServiceTests.MockHttpMessageHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly OpenAICompatibleDocumentAnalysisService _service;

    public LlmRefinerTruncationTests()
    {
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://api.example.com/v1/") };
        _service = new OpenAICompatibleDocumentAnalysisService(_httpClient, "test-model", NullLogger<OpenAICompatibleDocumentAnalysisService>.Instance);
    }

    public void Dispose()
    {
        _service.Dispose();
        _httpClient.Dispose();
        _handler.Dispose();
    }

    private static readonly LlmRefineOptions NoiseOnly = new()
    {
        RestoreSentences = false, CorrectOcrErrors = false, RestructureSections = false, MergeDuplicates = false, RemoveNoise = true,
    };

    [Fact]
    public async Task A_response_cut_off_at_the_token_limit_does_not_replace_the_document()
    {
        var document = LongDocument();
        Respond(document[..(document.Length / 3)], finishReason: "length");

        var result = await new LlmRefiner(_service).RefineAsync(new RefinedContent { Text = document }, NoiseOnly, TestContext.Current.CancellationToken);

        result.Text.Should().Be(document.Trim(), "the cut-off rewrite is shorter, but that is lost content, not removed noise");
        result.Info.Improvements.Should().BeEmpty();
        result.Info.Warnings.Should().ContainSingle().Which.Should().StartWith("RemoveNoise:").And.Contain("truncated");
    }

    [Fact]
    public async Task A_complete_shorter_response_is_still_adopted()
    {
        var document = LongDocument();
        var cleaned = document[..(document.Length / 2)];
        Respond(cleaned, finishReason: "stop");

        var result = await new LlmRefiner(_service).RefineAsync(new RefinedContent { Text = document }, NoiseOnly, TestContext.Current.CancellationToken);

        result.Text.Should().Be(cleaned.Trim());
        result.Info.Improvements.Should().ContainSingle();
        result.Info.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unset_MaxTokens_is_sized_from_the_text_not_left_to_the_service_default()
    {
        var document = LongDocument();
        Respond(document, finishReason: "stop");

        await new LlmRefiner(_service).RefineAsync(new RefinedContent { Text = document }, NoiseOnly, TestContext.Current.CancellationToken);

        RequestedMaxTokens().Should().Be(LlmRefiner.OutputBudget(document)).And.BeGreaterThan(1000);
    }

    [Fact]
    public async Task An_explicit_MaxTokens_is_sent_as_given()
    {
        var document = LongDocument();
        Respond(document, finishReason: "stop");
        var options = new LlmRefineOptions
        {
            RestoreSentences = false, CorrectOcrErrors = false, RestructureSections = false, MergeDuplicates = false, RemoveNoise = true, MaxTokens = 4321,
        };

        await new LlmRefiner(_service).RefineAsync(new RefinedContent { Text = document }, options, TestContext.Current.CancellationToken);

        RequestedMaxTokens().Should().Be(4321);
    }

    [Fact]
    public async Task GenerateAsync_throws_on_a_length_finish_but_the_availability_probe_does_not()
    {
        Respond("Hel", finishReason: "length");

        var act = () => _service.GenerateAsync("p", new GenerationSettings(MaxTokens: 50), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<GenerationTruncatedException>()).Which.MaxTokens.Should().Be(50);
        (await _service.IsAvailableAsync(TestContext.Current.CancellationToken)).Should().BeTrue("the probe asks for 10 tokens and only needs an answer");
    }

    public static TheoryData<string, LlmRefineOptions> SinglePasses => new()
    {
        { "RestoreSentences", new LlmRefineOptions { RemoveNoise = false, CorrectOcrErrors = false, RestructureSections = false, MergeDuplicates = false, RestoreSentences = true } },
        { "RemoveNoise", new LlmRefineOptions { RestoreSentences = false, CorrectOcrErrors = false, RestructureSections = false, MergeDuplicates = false, RemoveNoise = true } },
        { "CorrectOcrErrors", new LlmRefineOptions { RestoreSentences = false, RemoveNoise = false, RestructureSections = false, MergeDuplicates = false, CorrectOcrErrors = true } },
        { "RestructureSections", new LlmRefineOptions { RestoreSentences = false, RemoveNoise = false, CorrectOcrErrors = false, MergeDuplicates = false, RestructureSections = true } },
        { "MergeDuplicates", new LlmRefineOptions { RestoreSentences = false, RemoveNoise = false, CorrectOcrErrors = false, RestructureSections = false, MergeDuplicates = true } },
    };

    [Theory]
    [MemberData(nameof(SinglePasses))]
    public async Task No_pass_adopts_a_truncated_response(string pass, LlmRefineOptions options)
    {
        var document = RefinableText();
        var service = new ScriptedService(_ => throw new GenerationTruncatedException(100));

        var result = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = document }, options, TestContext.Current.CancellationToken);

        service.Calls.Should().Be(1, $"the {pass} pass runs on this text");
        result.Text.Should().Be(document.Trim());
        result.Info.Warnings.Should().ContainSingle().Which.Should().StartWith($"{pass}:").And.Contain("truncated");
    }

    [Fact]
    public async Task A_pass_that_does_not_fit_the_declared_context_is_not_sent()
    {
        var document = LongDocument();
        var service = new ScriptedService(p => p, contextLength: 2000);

        var result = await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = document }, NoiseOnly, TestContext.Current.CancellationToken);

        service.Calls.Should().Be(0);
        result.Text.Should().Be(document.Trim());
        result.Info.Warnings.Should().ContainSingle().Which.Should().StartWith("RemoveNoise:").And.Contain("model context (2000 tokens)");
    }

    [Fact]
    public async Task An_undeclared_context_is_not_checked()
    {
        var service = new ScriptedService(_ => "short", contextLength: 0);

        await new LlmRefiner(service).RefineAsync(new RefinedContent { Text = LongDocument() }, NoiseOnly, TestContext.Current.CancellationToken);

        service.Calls.Should().Be(1);
    }

    private void Respond(string content, string finishReason) =>
        _handler.SetResponse(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { role = "assistant", content }, finish_reason = finishReason } },
        }));

    private int RequestedMaxTokens()
    {
        using var body = JsonDocument.Parse(_handler.LastRequestBody!);
        return body.RootElement.GetProperty("max_tokens").GetInt32();
    }

    /// <summary>About 10 KB — past what 1000 output tokens can hold.</summary>
    private static string LongDocument() =>
        string.Join("\n\n", Enumerable.Range(1, 60).Select(i =>
            $"## Section {i}\n\nParagraph {i} explains one part of the procedure in enough words to be worth keeping, and then some more."));

    private static string RefinableText() =>
        "# Title\n\n## Section\n\n" + string.Join("\n", Enumerable.Repeat("the quick br0wn fox jumped 0ver the l azy d0g and kept", 40)) + "\n\n" +
        string.Join(" ", Enumerable.Repeat("Thc quick br0wn fox jumped 0ver the l azy d0g.", 40));

    private sealed class ScriptedService(Func<string, string> answer, int contextLength = 0) : IDocumentAnalysisService
    {
        public int Calls { get; private set; }

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default) =>
            GenerateAsync(prompt, GenerationSettings.Default, cancellationToken);

        public Task<string> GenerateAsync(string prompt, GenerationSettings settings, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(answer(prompt));
        }

        public DocumentAnalysisServiceInfo ProviderInfo => new() { Name = "scripted", Type = DocumentAnalysisProviderType.Custom, MaxContextLength = contextLength };
        public Task<StructureAnalysisResult> AnalyzeStructureAsync(string prompt, DocumentType documentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ContentSummary> SummarizeContentAsync(string prompt, int maxLength = 200, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MetadataExtractionResult> ExtractMetadataAsync(string prompt, DocumentType documentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<QualityAssessment> AssessQualityAsync(string prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
