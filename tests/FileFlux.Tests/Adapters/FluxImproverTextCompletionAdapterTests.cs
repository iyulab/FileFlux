using AwesomeAssertions;
using FileFlux.Infrastructure.Adapters;
using FluxImprover.Services;
using NSubstitute;
using Xunit;

namespace FileFlux.Tests.Adapters;

/// <summary>
/// FluxImprover's per-call options reach the FileFlux analysis service: the token limit and temperature as
/// <see cref="GenerationSettings"/>, the system prompt ahead of the prompt. The adapter used to call the one-argument
/// overload, so every FluxImprover call ran under the service's defaults.
/// </summary>
public sealed class FluxImproverTextCompletionAdapterTests
{
    [Fact]
    public async Task CompleteAsync_CarriesMaxTokensTemperatureAndSystemPrompt()
    {
        var inner = Substitute.For<IDocumentAnalysisService>();
        inner.GenerateAsync(Arg.Any<string>(), Arg.Any<GenerationSettings>(), Arg.Any<CancellationToken>()).Returns("ok");
        var adapter = new FluxImproverTextCompletionAdapter(inner);

        var text = await adapter.CompleteAsync(
            "Summarize: x",
            new CompletionOptions { MaxTokens = 512, Temperature = 0.2f, SystemPrompt = "You summarize." },
            TestContext.Current.CancellationToken);

        text.Should().Be("ok");
        await inner.Received(1).GenerateAsync(
            "You summarize.\n\nSummarize: x",
            Arg.Is<GenerationSettings>(s => s.MaxTokens == 512 && s.Temperature.HasValue && Math.Abs(s.Temperature.Value - 0.2) < 1e-6),
            Arg.Any<CancellationToken>());
        await inner.DidNotReceive().GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteAsync_WithoutOptions_UsesTheServiceDefaults()
    {
        var inner = Substitute.For<IDocumentAnalysisService>();
        inner.GenerateAsync(Arg.Any<string>(), Arg.Any<GenerationSettings>(), Arg.Any<CancellationToken>()).Returns("ok");
        var adapter = new FluxImproverTextCompletionAdapter(inner);

        await adapter.CompleteAsync("plain", cancellationToken: TestContext.Current.CancellationToken);

        await inner.Received(1).GenerateAsync("plain", GenerationSettings.Default, Arg.Any<CancellationToken>());
    }
}
