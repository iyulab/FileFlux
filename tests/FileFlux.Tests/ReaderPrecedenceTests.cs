using FileFlux.Core;
using FileFlux.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests;

/// <summary>
/// The reader factory prefers the reader registered last for an extension. A reader the caller adds wins over the
/// built-ins whichever side of <c>AddFileFlux()</c> it is registered on — it used to lose when registered before, because
/// the built-ins, registered after it, came last.
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
    public void AddedReader_RegisteredBeforeAddFileFlux_Wins(string file)
    {
        var reader = ReaderFor(s => { s.AddDocumentReader<OfficeStandInReader>(); s.AddFileFlux(); }, file);

        Assert.IsType<OfficeStandInReader>(reader);
    }

    [Fact]
    public void AddedReader_RegisteredAfterAddFileFlux_Wins()
    {
        Assert.IsType<OfficeStandInReader>(ReaderFor(s => { s.AddFileFlux(); s.AddDocumentReader<OfficeStandInReader>(); }, "report.docx"));
    }

    [Fact]
    public void WithoutAnAddedReader_TheBuiltInReaderServesDocx()
    {
        // Control: the facts above would pass on a factory that always returned the added reader.
        var reader = ReaderFor(s => s.AddFileFlux(), "report.docx");

        Assert.NotNull(reader);
        Assert.IsNotType<OfficeStandInReader>(reader);
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

    private sealed class OfficeStandInReader : IDocumentReader
    {
        public IEnumerable<string> SupportedExtensions => [".docx", ".xlsx", ".pptx"];

        public string ReaderType => "OfficeStandIn";

        public bool CanRead(string fileName) => SupportedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

        public Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ReadResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RawContent> ExtractAsync(string filePath, ExtractOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
