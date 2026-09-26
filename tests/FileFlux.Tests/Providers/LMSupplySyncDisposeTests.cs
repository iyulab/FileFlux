using FileFlux.Core;
using Microsoft.Extensions.DependencyInjection;

namespace FileFlux.Tests.Providers;

/// <summary>
/// A container disposed with <c>Dispose()</c> throws on a singleton that is only <see cref="IAsyncDisposable"/>. The
/// LMSupply services implement both, so a consumer that builds its provider with <c>using</c> does not crash on exit.
/// </summary>
public class LMSupplySyncDisposeTests
{
    [Fact]
    public void Container_DisposedSynchronously_DoesNotThrow()
    {
        var services = new ServiceCollection();
        FileFlux.Providers.LMSupply.Extensions.ServiceCollectionExtensions.AddLMSupplyTranscriber(services);
        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IAudioToTextService>(); // resolved, model not loaded (it loads on first use)

        var dispose = Record.Exception(provider.Dispose);

        Assert.Null(dispose);
    }
}
