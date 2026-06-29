using FileFlux;
using FileFlux.Core;
using FileFlux.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Factories;

/// <summary>
/// Regression tests for MU-6: the parser factory and Markdown converter must not capture a
/// Scoped <see cref="IDocumentAnalysisService"/> as a Singleton (captive dependency). With the
/// default Scoped lifetime the resolved graph must validate under <c>ValidateScopes:true</c>,
/// so consumers (e.g. AIMS) need not disable global scope validation.
/// </summary>
public class FileFluxLifetimeScopeValidationTests
{
    [Fact]
    public void AddFileFlux_DefaultScoped_WithScopedAnalysisService_BuildsUnderValidateScopes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Consumer registers the analysis service as Scoped (as FluxIndex's adapter does).
        services.AddScoped<IDocumentAnalysisService, MockTextCompletionService>();

        services.AddFileFlux(); // default: ServiceLifetime.Scoped

        // ValidateScopes:true is the .NET dev-time default; a captive dependency throws here at build.
        using var provider = services.BuildServiceProvider(validateScopes: true);

        // Resolving the previously-captive services inside a scope must succeed without throwing.
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDocumentParserFactory>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMarkdownConverter>());
    }

    [Fact]
    public void AddFileFlux_Singleton_WithSingletonAnalysisService_BuildsUnderValidateScopes()
    {
        // Regression guard: the Singleton consumption path (background/hosted services) still works.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDocumentAnalysisService, MockTextCompletionService>();

        services.AddFileFlux(ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider(validateScopes: true);

        Assert.NotNull(provider.GetRequiredService<IDocumentParserFactory>());
        Assert.NotNull(provider.GetRequiredService<IMarkdownConverter>());
    }
}
