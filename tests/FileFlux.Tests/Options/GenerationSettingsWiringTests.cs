using AwesomeAssertions;
using FileFlux.Core;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Parsers;
using Xunit;

namespace FileFlux.Tests.Options;

/// <summary>
/// <c>LlmRefineOptions.Temperature</c>/<c>MaxTokens</c> and <c>ParsingOptions.Temperature</c>/<c>MaxTokens</c> were declared,
/// defaulted and read by nothing: every LLM call used the service's literals. They now travel as
/// <see cref="GenerationSettings"/> through <see cref="IDocumentAnalysisService.GenerateAsync(string, GenerationSettings, CancellationToken)"/>.
/// </summary>
public sealed class GenerationSettingsWiringTests
{
    [Fact]
    public async Task LlmRefineOptions_Temperature_and_MaxTokens_reach_every_refinement_call()
    {
        var service = new RecordingService();
        var refiner = new LlmRefiner(service);
        var refined = new RefinedContent { Text = OcrLikeText() };

        await refiner.RefineAsync(refined, new LlmRefineOptions { RemoveNoise = true, CorrectOcrErrors = true, Temperature = 0.15, MaxTokens = 321 }, TestContext.Current.CancellationToken);

        service.Settings.Should().NotBeEmpty();
        service.Settings.Should().OnlyContain(s => s.Temperature == 0.15 && s.MaxTokens == 321);
    }

    [Fact]
    public async Task LlmRefineOptions_MaxTokens_zero_means_the_service_default()
    {
        var service = new RecordingService();
        var refiner = new LlmRefiner(service);

        await refiner.RefineAsync(new RefinedContent { Text = OcrLikeText() }, new LlmRefineOptions { RemoveNoise = true, MaxTokens = 0 }, TestContext.Current.CancellationToken);

        service.Settings.Should().NotBeEmpty().And.OnlyContain(s => s.MaxTokens == null);
    }

    [Fact]
    public async Task DocumentParsingOptions_settings_reach_the_structuring_call()
    {
        var service = new RecordingService();
        var parser = new BasicDocumentParser(service);
        var raw = new RawContent { Text = string.Join("\n\n", Enumerable.Range(1, 30).Select(i => $"# Section {i}\n\nParagraph {i} with enough words to matter.")) };

        await parser.ParseAsync(raw, new DocumentParsingOptions { UseLlmParsing = true, Temperature = 0.05, MaxTokens = 777 }, TestContext.Current.CancellationToken);
        var recorded = service.Settings.ToList();
        service.Settings.Clear();
        await parser.ParseAsync(raw, new DocumentParsingOptions { UseLlmParsing = true }, TestContext.Current.CancellationToken);

        recorded.Should().NotBeEmpty("the parser called the LLM").And.OnlyContain(s => s.Temperature == 0.05 && s.MaxTokens == 777);
        service.Settings.Should().NotBeEmpty().And.OnlyContain(s => s.Temperature == null && s.MaxTokens == null, "unset options mean the service default");
    }

    [Fact]
    public async Task The_default_overload_ignores_settings_for_an_implementation_that_does_not_override_it()
    {
        IDocumentAnalysisService legacy = new LegacyService();

        var answer = await legacy.GenerateAsync("p", new GenerationSettings(0.1, 5), TestContext.Current.CancellationToken);

        answer.Should().Be("legacy", "source compatibility: an outside implementation keeps working, and its documentation says the settings do not reach it");
    }

    private static string OcrLikeText() =>
        string.Join(" ", Enumerable.Repeat("Thc quick br0wn fox jumped 0ver the l azy d0g.", 40));

    private sealed class RecordingService : IDocumentAnalysisService
    {
        public List<GenerationSettings> Settings { get; } = [];

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            Settings.Add(GenerationSettings.Default);
            return Task.FromResult("{}");
        }

        public Task<string> GenerateAsync(string prompt, GenerationSettings settings, CancellationToken cancellationToken = default)
        {
            Settings.Add(settings);
            return Task.FromResult("{}");
        }

        public DocumentAnalysisServiceInfo ProviderInfo => new() { Name = "recording", Type = DocumentAnalysisProviderType.Custom };
        public Task<StructureAnalysisResult> AnalyzeStructureAsync(string prompt, DocumentType documentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ContentSummary> SummarizeContentAsync(string prompt, int maxLength = 200, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MetadataExtractionResult> ExtractMetadataAsync(string prompt, DocumentType documentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<QualityAssessment> AssessQualityAsync(string prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class LegacyService : IDocumentAnalysisService
    {
        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default) => Task.FromResult("legacy");
        public DocumentAnalysisServiceInfo ProviderInfo => new() { Name = "recording", Type = DocumentAnalysisProviderType.Custom };
        public Task<StructureAnalysisResult> AnalyzeStructureAsync(string prompt, DocumentType documentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ContentSummary> SummarizeContentAsync(string prompt, int maxLength = 200, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MetadataExtractionResult> ExtractMetadataAsync(string prompt, DocumentType documentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<QualityAssessment> AssessQualityAsync(string prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
