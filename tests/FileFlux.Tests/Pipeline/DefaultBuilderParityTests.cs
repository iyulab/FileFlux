using FileFlux.Core;
using FileFlux.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// The default builder and dependency injection refine the same file the same way. The builder used to skip the
/// Markdown converter and normaliser that <c>AddFileFlux</c> registers, so a harness built on <c>CreateDefault()</c>
/// measured a different pipeline from the one production ran.
/// </summary>
public sealed class DefaultBuilderParityTests
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "vertical-merge.xlsx");

    [Fact]
    public async Task CreateDefault_AndAddFileFlux_RefineToTheSameText()
    {
        await using var fromBuilder = DocumentProcessorFactoryBuilder.CreateDefault().Build().Create(Fixture);
        await fromBuilder.RefineAsync(cancellationToken: TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddFileFlux();
        await using var provider = services.BuildServiceProvider();
        await using var fromServices = provider.GetRequiredService<IDocumentProcessorFactory>().Create(Fixture);
        await fromServices.RefineAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(fromBuilder.Result.Refined?.Text));
        Assert.Equal(fromServices.Result.Refined!.Text, fromBuilder.Result.Refined!.Text);
    }
}
