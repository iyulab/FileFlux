using Iyu.Conventions.Testing;
using Xunit;

namespace FileFlux.Tests;

/// <summary>
/// The public surface follows the two API rules of the ecosystem: every public async method takes a
/// <see cref="CancellationToken"/>, and failure is reported by an exception rather than by a returned object carrying a
/// success flag and an error. The scans are <c>Iyu.Conventions.Testing</c>'s, over the same assemblies as the
/// operational-language scan.
/// </summary>
/// <remarks>
/// The rosters are the methods that break a rule today. Shrink them; never grow them silently. A change to a listed
/// method's parameters changes its entry, which is a roster change on purpose.
/// </remarks>
public class PublicApiConventionTests
{
    private static readonly string[] KnownUncancellable = [];

    // Kept (2026-10-05): RawContent / RefinedContent / parsed content are the extraction products, and a document is often
    // extracted in part — the Errors / Warnings / Status they carry are per-page diagnostics beside the content, not the
    // call's failure channel (a reader throws when it cannot read the file at all).
    private static readonly string[] KnownResultReturns =
    [
        "FileFlux.Core.IDocumentReader.ExtractAsync(Stream, String, ExtractOptions, CancellationToken)",
        "FileFlux.Core.IDocumentReader.ExtractAsync(String, ExtractOptions, CancellationToken)",
        "FileFlux.Core.IDocumentReader.ReadAsync(Stream, String, CancellationToken)",
        "FileFlux.Core.IDocumentReader.ReadAsync(String, CancellationToken)",
        "FileFlux.Core.IDocumentRefiner.RefineAsync(RawContent, RefineOptions, CancellationToken)",
        "FileFlux.Core.ILlmRefiner.RefineAsync(RefinedContent, LlmRefineOptions, CancellationToken)",
        "FileFlux.Core.Infrastructure.Readers.ImageExtractionPolicy.Apply(RawContent, ExtractOptions)",
        "FileFlux.Core.Infrastructure.Readers.TextDocumentReader.ExtractStreamAsync(Stream, String, Action<ProcessingProgress>, CancellationToken)",
        "FileFlux.Core.Infrastructure.Readers.TextDocumentReader.ExtractStreamAsync(String, Action<ProcessingProgress>, CancellationToken)",
        "FileFlux.IDocumentParser.ParseAsync(RawContent, DocumentParsingOptions, CancellationToken)",
        "FileFlux.Infrastructure.FluxDocumentProcessor.ExtractAsync(String, CancellationToken)",
        "FileFlux.Infrastructure.FluxDocumentProcessor.ParseAsync(RawContent, ParsingOptions, CancellationToken)",
        "FileFlux.Infrastructure.FluxDocumentProcessor.RefineAsync(RefinedContent, RefiningOptions, CancellationToken)",
    ];

    [Fact]
    public void PublicAsyncMethods_TakeACancellationToken() =>
        AsyncCancellation.Scan(OptionsReachabilityRosterTests.Libraries).ShouldMatchRoster(KnownUncancellable);

    [Fact]
    public void PublicMethods_DoNotReturnResultObjects() =>
        ResultReturns.Scan(OptionsReachabilityRosterTests.Libraries).ShouldMatchRoster(KnownResultReturns);

    // Positive control: an empty roster would also pass if the scan saw no public method at all.
    [Fact]
    public void Scan_SeesThePublicSurface() =>
        Assert.True(ResultReturns.Scan(OptionsReachabilityRosterTests.Libraries).MembersRead > 0, "the scan read too few public methods");
}
