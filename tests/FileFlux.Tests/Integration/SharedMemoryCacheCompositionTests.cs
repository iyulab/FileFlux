using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FileFlux.Tests.Integration;

/// <summary>
/// <c>AddFileFlux</c> must not choose the host's shared <see cref="IMemoryCache"/> policy.
/// Before 0.23.8 it called <c>AddMemoryCache(o =&gt; o.SizeLimit = 100)</c>. <c>MemoryCacheOptions</c>
/// configuration accumulates regardless of registration order, so every library in the same container
/// that called <c>cache.Set</c> without a <c>Size</c> then threw
/// "Cache entry must specify a value for Size when SizeLimit is set". Nothing FileFlux registers in DI
/// resolves <see cref="IMemoryCache"/>, so the registration only had that effect.
/// </summary>
public sealed class SharedMemoryCacheCompositionTests
{
    [Fact]
    public void HostCacheRegisteredFirst_ThenFileFlux_SetWithoutSizeDoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddFileFlux();

        AssertHostCacheUnlimited(services);
    }

    [Fact]
    public void FileFluxRegisteredFirst_ThenHostCache_SetWithoutSizeDoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFileFlux();
        services.AddMemoryCache();

        AssertHostCacheUnlimited(services);
    }

    [Fact]
    public void AddFileFlux_DoesNotConfigureMemoryCacheOptions()
    {
        var services = new ServiceCollection();
        services.AddFileFlux();

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IConfigureOptions<MemoryCacheOptions>));
    }

    private static void AssertHostCacheUnlimited(ServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IMemoryCache>();

        Assert.Null(provider.GetRequiredService<IOptions<MemoryCacheOptions>>().Value.SizeLimit);

        var exception = Record.Exception(() => cache.Set("community-summary", "text"));
        Assert.Null(exception);
        Assert.Equal("text", cache.Get<string>("community-summary"));
    }
}
