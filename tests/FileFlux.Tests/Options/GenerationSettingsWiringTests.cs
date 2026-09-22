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
    public async Task LlmRefineOptions_MaxTokens_zero_is_sized_from_the_text()
    {
        // Each pass rewrites the whole text, so "unset" means a budget the rewrite fits in (0.26.0) — not the service
        // default, which was 1000 tokens and cut off every document past roughly 4 KB.
        var service = new RecordingService();
        var refiner = new LlmRefiner(service);
        var options = new LlmRefineOptions { RestoreSentences = false, CorrectOcrErrors = false, RestructureSections = false, MergeDuplicates = false, RemoveNoise = true, MaxTokens = 0 };

        await refiner.RefineAsync(new RefinedContent { Text = OcrLikeText() }, options, TestContext.Current.CancellationToken);

        service.Settings.Should().ContainSingle().Which.MaxTokens.Should().Be(LlmRefiner.OutputBudget(OcrLikeText()));
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

    /// <summary>
    /// One pass at a time: the recording double answers "{}", which would collapse the text after the first pass and
    /// close the later passes' own gates (OCR-looking tokens, headings, length) — so each pass gets a fresh text.
    /// </summary>
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
    public async Task LlmRefineOptions_DocumentType_TargetLanguage_CustomInstructions_reach_the_pass_prompt(string pass, LlmRefineOptions options)
    {
        var service = new RecordingService();
        var refiner = new LlmRefiner(service);
        options.DocumentType = DocumentTypeHint.Legal;
        options.TargetLanguage = "ko";
        options.CustomInstructions = "Keep the article numbers.";

        await refiner.RefineAsync(new RefinedContent { Text = RefinableText() }, options, TestContext.Current.CancellationToken);

        var prompt = service.Prompts.Should().ContainSingle($"the {pass} pass calls the LLM once on this text").Subject;
        prompt.Should().Contain("The document is a legal document")
            .And.Contain("Write the result in the language 'ko'")
            .And.Contain("Additional instructions: Keep the article numbers.");
    }

    [Theory]
    [MemberData(nameof(SinglePasses))]
    public async Task LlmRefineOptions_unset_context_adds_no_rule_to_the_pass_prompt(string pass, LlmRefineOptions options)
    {
        var service = new RecordingService();
        var refiner = new LlmRefiner(service);

        await refiner.RefineAsync(new RefinedContent { Text = RefinableText() }, options, TestContext.Current.CancellationToken);

        var prompt = service.Prompts.Should().ContainSingle($"the {pass} pass calls the LLM once on this text").Subject;
        prompt.Should().NotContain("The document is").And.NotContain("Write the result in the language").And.NotContain("Additional instructions:");
        LlmRefiner.ContextRules(new LlmRefineOptions()).Should().BeEmpty();
    }

    [Fact]
    public void ContextRules_orders_document_type_then_language_then_instructions()
    {
        var rules = LlmRefiner.ContextRules(new LlmRefineOptions { DocumentType = DocumentTypeHint.Pdf, TargetLanguage = " en ", CustomInstructions = "  Short.  " });

        rules.Split('\n').Should().Equal(
            "- The document is text extracted from a PDF (line breaks may fall mid-sentence)",
            "- Write the result in the language 'en' (keep passages that are already in it; do not translate technical terms)",
            "- Additional instructions: Short.");
    }

    /// <summary>Long enough for every pass's own gate: lowercase-to-lowercase line breaks, OCR-looking tokens, headings, and > 1000 chars.</summary>
    private static string RefinableText() =>
        "# Title\n\n## Section\n\n" + string.Join("\n", Enumerable.Repeat("the quick br0wn fox jumped 0ver the l azy d0g and kept", 40)) + "\n\n" + OcrLikeText();

    private static string OcrLikeText() =>
        string.Join(" ", Enumerable.Repeat("Thc quick br0wn fox jumped 0ver the l azy d0g.", 40));

    private sealed class RecordingService : IDocumentAnalysisService
    {
        public List<GenerationSettings> Settings { get; } = [];
        public List<string> Prompts { get; } = [];

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            Settings.Add(GenerationSettings.Default);
            Prompts.Add(prompt);
            return Task.FromResult("{}");
        }

        public Task<string> GenerateAsync(string prompt, GenerationSettings settings, CancellationToken cancellationToken = default)
        {
            Settings.Add(settings);
            Prompts.Add(prompt);
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
