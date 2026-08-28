using FileFlux.Core;
using FileFlux.Providers.LMSupply.Extensions;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Providers.LMSupply;

/// <summary>
/// Registration-shape assertions for FileFlux.Providers.LMSupply's DI extension methods — mirrors
/// FluxIndex.Providers.LMSupply.Tests's ServiceCollectionExtensionsTests. Deliberately does not
/// resolve any service (that would trigger a real model load); these only check the descriptor
/// list, the same way the sibling package's tests do.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLMSupplyDocumentAnalysis_RegistersIDocumentAnalysisService()
    {
        var services = new ServiceCollection();

        services.AddLMSupplyDocumentAnalysis("test-model");

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IDocumentAnalysisService) &&
            sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddLMSupplyEmbedding_RegistersIEmbeddingService()
    {
        var services = new ServiceCollection();

        services.AddLMSupplyEmbedding("test-model");

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IEmbeddingService) &&
            sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddLMSupplyOcr_RegistersIImageToTextService()
    {
        var services = new ServiceCollection();

        services.AddLMSupplyOcr();

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IImageToTextService) &&
            sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddLMSupplyCaptioner_RegistersIImageToTextService()
    {
        var services = new ServiceCollection();

        services.AddLMSupplyCaptioner();

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IImageToTextService) &&
            sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddLMSupplyDocumentAnalysis_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddLMSupplyDocumentAnalysis();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddLMSupplyEmbedding_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddLMSupplyEmbedding();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddLMSupplyOcr_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddLMSupplyOcr();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddLMSupplyCaptioner_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddLMSupplyCaptioner();

        result.Should().BeSameAs(services);
    }
}
