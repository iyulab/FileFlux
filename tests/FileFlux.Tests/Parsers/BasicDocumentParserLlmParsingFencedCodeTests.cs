using AwesomeAssertions;
using FileFlux.Core;
using FileFlux.Infrastructure.Parsers;
using Xunit;

namespace FileFlux.Tests.Parsers;

/// <summary>
/// The LLM parse (<see cref="DocumentParsingOptions.UseLlmParsing"/>) asks the model for a topic, keywords and a summary only; the
/// structure and the text come from the rule-based parse. A fenced code block therefore has to come out of it exactly as it
/// does out of the rule-based parse - the text is rebuilt from the sections after the model answered, and rebuilding is where the
/// block's blank lines and indentation could be lost.
/// </summary>
public sealed class BasicDocumentParserLlmParsingFencedCodeTests
{
    private const string Fence = "```";

    private static readonly string Block = Fence + "python\ndef f():\n    x = 1\n\n\n    return x\n\n    # a comment\n" + Fence;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_fenced_block_comes_out_of_the_LLM_parse_as_it_comes_out_of_the_rule_based_parse(bool hasHeaders)
    {
        var markdown = hasHeaders
            ? "# Setup\n\nIntro paragraph.\n\n" + Block + "\n\nA closing paragraph.\n\n## More\n\nSecond section."
            : "Intro paragraph.\n\n" + Block + "\n\nA closing paragraph.";
        var raw = new RawContent
        {
            Text = markdown,
            File = new SourceFileInfo { Name = "guide.md", Extension = ".md", Size = markdown.Length },
            Hints = new Dictionary<string, object> { ["has_headers"] = hasHeaders },
        };
        var model = new FixedAnswerService("TOPIC: setup\nKEYWORDS: a, b\nSUMMARY: a short summary");

        var rules = await new BasicDocumentParser().ParseAsync(raw, new DocumentParsingOptions { UseLlmParsing = false }, TestContext.Current.CancellationToken);
        var llm = await new BasicDocumentParser(model).ParseAsync(raw, new DocumentParsingOptions { UseLlmParsing = true }, TestContext.Current.CancellationToken);

        model.Calls.Should().Be(1, "the LLM parse was really used");
        llm.Topic.Should().Be("setup");
        rules.Text.Should().Contain(Block);
        llm.Text.Should().Contain(Block, "the block's blank lines and indentation survive the rebuild");
        llm.Text.Should().Be(rules.Text);
    }

    private sealed class FixedAnswerService(string answer) : IDocumentAnalysisService
    {
        public int Calls { get; private set; }

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default) =>
            GenerateAsync(prompt, GenerationSettings.Default, cancellationToken);

        public Task<string> GenerateAsync(string prompt, GenerationSettings settings, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(answer);
        }

        public DocumentAnalysisServiceInfo ProviderInfo => new() { Name = "fixed", Type = DocumentAnalysisProviderType.Custom };
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
