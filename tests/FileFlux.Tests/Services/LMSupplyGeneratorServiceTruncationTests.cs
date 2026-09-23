using AwesomeAssertions;
using FileFlux.Core;
using FileFlux.Providers.LMSupply.Services;
using LMSupply.Generator.Abstractions;
using LMSupply.Generator.Models;
using NSubstitute;
using Xunit;

namespace FileFlux.Tests.Services;

/// <summary>
/// The LMSupply-backed service applies the same rule as the OpenAI-compatible one: an answer the model stopped at
/// MaxTokens throws <see cref="GenerationTruncatedException"/>, so a refiner never adopts a cut-off rewrite as whole.
/// </summary>
public sealed class LMSupplyGeneratorServiceTruncationTests
{
    private static LMSupplyGeneratorService Service(IGeneratorModel model) => new(model);

    private static IGeneratorModel Model(string text, string? finishReason)
    {
        var model = Substitute.For<IGeneratorModel>();
        model.ModelId.Returns("test-model");
        model.GenerateCompleteResultAsync(Arg.Any<string>(), Arg.Any<GenerationOptions>(), Arg.Any<CancellationToken>())
            .Returns(new GenerationResult(text, TokenUsage.Empty, finishReason));
        return model;
    }

    [Fact]
    public async Task GenerateAsync_AnAnswerCutOffAtMaxTokens_Throws()
    {
        await using var service = Service(Model("half a rewri", "length"));

        var act = () => service.GenerateAsync("rewrite this", new GenerationSettings { MaxTokens = 32 }, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<GenerationTruncatedException>()).Which.Message.Should().Contain("32");
    }

    [Theory]
    [InlineData("stop")]
    [InlineData(null)]
    public async Task GenerateAsync_AFinishedAnswer_OrOneWithoutAReason_IsReturned(string? finishReason)
    {
        await using var service = Service(Model("the whole rewrite", finishReason));

        var text = await service.GenerateAsync("rewrite this", new GenerationSettings { MaxTokens = 32 }, TestContext.Current.CancellationToken);

        text.Should().Be("the whole rewrite");
    }
}
