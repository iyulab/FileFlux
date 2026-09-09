using FluxImprover;
using FluxImprover.ContextualRetrieval;
using FluxImprover.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace FileFlux.Tests.Integration;

/// <summary>
/// <c>AddFileFlux</c> and FluxImprover's own <c>AddFluxImprover</c> must compose in either order.
/// Before 0.22.12 FileFlux registered <c>FluxImproverServices</c> with <c>Add</c> and a factory that
/// returned null when FileFlux had no <c>IDocumentAnalysisService</c>; registered after
/// <c>AddFluxImprover</c>, that null-returning descriptor shadowed FluxImprover's and every FluxImprover
/// facade in the container failed with "No service for type FluxImproverServices" — found when the
/// ecosystem E2E harness wired FluxFeed's contextual enrichment (2026-09-09).
/// </summary>
public sealed class FluxImproverCompositionOrderTests
{
    private static ITextGenerationService FakeCompletion()
    {
        var completion = Substitute.For<ITextGenerationService>();
        completion.CompleteAsync(Arg.Any<string>(), Arg.Any<CompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns("ok");
        return completion;
    }

    [Fact]
    public void FluxImproverRegisteredFirst_ThenFileFlux_ResolvesFluxImproverServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFluxImprover(_ => FakeCompletion());
        services.AddFileFlux();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<FluxImproverServices>());
        Assert.NotNull(scope.ServiceProvider.GetService<IContextualEnrichmentService>());
    }

    [Fact]
    public void FileFluxRegisteredFirst_ThenFluxImprover_ResolvesFluxImproverServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFileFlux();
        services.AddFluxImprover(_ => FakeCompletion());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // FluxImprover's TryAdd yields to FileFlux's descriptor here, so FileFlux's factory has to be able
        // to build from FluxImprover's ITextGenerationService — that is the fallback under test.
        Assert.NotNull(scope.ServiceProvider.GetService<FluxImproverServices>());
        Assert.NotNull(scope.ServiceProvider.GetService<IContextualEnrichmentService>());
    }

    [Fact]
    public void FileFluxAlone_WithoutAnyCompletionService_StillHasNoImprover()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFileFlux();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Null(scope.ServiceProvider.GetService<FluxImproverServices>());
        Assert.False(scope.ServiceProvider.GetRequiredService<Core.IDocumentEnricher>().HasLlmSupport);
    }
}
