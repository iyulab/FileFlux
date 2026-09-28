using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using FileFlux.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests;

/// <summary>
/// The reader factory prefers the reader registered last for an extension. <c>AddNativeOfficeReader</c> told callers to
/// register it before <c>AddFileFlux()</c>, and <c>AddFileFluxWithNativeOffice</c> did exactly that — so the built-in
/// DOCX/XLSX/PPTX readers, registered after it, always won and the native reader never ran. A reader the caller adds now
/// wins over the built-ins whichever side of <c>AddFileFlux()</c> it is registered on.
/// </summary>
public class ReaderPrecedenceTests
{
    private static IDocumentReader? ReaderFor(Action<IServiceCollection> configure, string fileName)
    {
        var services = new ServiceCollection();
        configure(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IDocumentReaderFactory>().GetReader(fileName);
    }

    [Theory]
    [InlineData("report.docx")]
    [InlineData("sheet.xlsx")]
    [InlineData("deck.pptx")]
    public void NativeOfficeReader_RegisteredBeforeAddFileFlux_Wins(string file)
    {
        var reader = ReaderFor(s => { s.AddNativeOfficeReader(); s.AddFileFlux(); }, file);

        Assert.IsType<OfficeNativeDocumentReader>(reader);
    }

    [Fact]
    public void NativeOfficeReader_RegisteredAfterAddFileFlux_Wins()
    {
        Assert.IsType<OfficeNativeDocumentReader>(ReaderFor(s => { s.AddFileFlux(); s.AddNativeOfficeReader(); }, "report.docx"));
    }

    [Fact]
    public void AddFileFluxWithNativeOffice_UsesTheNativeReader()
    {
        Assert.IsType<OfficeNativeDocumentReader>(ReaderFor(s => s.AddFileFluxWithNativeOffice(), "report.docx"));
    }

    [Fact]
    public void WithoutTheNativeReader_TheBuiltInReaderServesDocx()
    {
        // Control: the facts above would pass on a factory that always returned the native reader.
        var reader = ReaderFor(s => s.AddFileFlux(), "report.docx");

        Assert.NotNull(reader);
        Assert.IsNotType<OfficeNativeDocumentReader>(reader);
    }

    [Fact]
    public void BuiltInReaderTypes_MatchWhatAddFileFluxRegisters()
    {
        var services = new ServiceCollection();
        services.AddFileFlux();
        using var provider = services.BuildServiceProvider();

        var registered = provider.GetServices<IDocumentReader>().Select(r => r.GetType()).ToHashSet();

        Assert.True(registered.SetEquals(ServiceCollectionExtensions.BuiltInReaderTypes),
            "AddFileFlux registers: " + string.Join(", ", registered.Select(t => t.Name).Order()));
    }
}
