using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Unpdf;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Propagation teeth for the parse-failure diagnostic (<c>extraction_error_kind</c>).
/// <para>
/// The exception path carries no <see cref="RawContent"/>, and consumers persist only
/// <see cref="Exception.Message"/>, so the kind must survive inside the message of the
/// <see cref="DocumentProcessingException"/> the reader throws. Two distinct kinds are
/// asserted so a hardcoded label cannot pass.
/// </para>
/// <para>
/// Inputs are synthesized rather than committed: the corruption recipe is the fixture,
/// and both recipes were verified against Unpdf 0.10.0 to land on the documented kinds.
/// Documents that open cleanly but fail page by page — the shape reported from the field
/// — are <b>not</b> reachable by any synthesizable corruption (every crude damage fails at
/// parse time), so that path's kind capture is covered by manual consumer verification,
/// not by these tests.
/// </para>
/// </summary>
public class PdfErrorKindPropagationTests : IDisposable
{
    private readonly PdfDocumentReader _reader = new();
    private readonly List<string> _tempFiles = [];

    [Fact]
    public async Task ExtractAsync_PdfHeaderWithGarbageBody_ShouldReportPdfParseKind()
    {
        // Recognized as a PDF (header present) but structurally unparseable.
        var path = WriteTempPdf("%PDF-1.7\ngarbage\n"u8.ToArray());

        var ex = await Assert.ThrowsAsync<DocumentProcessingException>(
            () => _reader.ExtractAsync(path));

        Assert.Contains($"extraction_error_kind={UnpdfErrorKind.PdfParse}", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_NotAPdfAtAll_ShouldReportUnknownFormatKind()
    {
        // No PDF header: a different failure reason, and it must not be mislabeled.
        var path = WriteTempPdf("this is definitely not a pdf file at all\n"u8.ToArray());

        var ex = await Assert.ThrowsAsync<DocumentProcessingException>(
            () => _reader.ExtractAsync(path));

        Assert.Contains($"extraction_error_kind={UnpdfErrorKind.UnknownFormat}", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain($"extraction_error_kind={UnpdfErrorKind.PdfParse}", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_NotAPdfAtAll_ShouldAlsoReportTheKind()
    {
        // Stage 0 opens the document too, so it must carry the same diagnostic.
        var path = WriteTempPdf("this is definitely not a pdf file at all\n"u8.ToArray());

        var ex = await Assert.ThrowsAsync<DocumentProcessingException>(
            () => _reader.ReadAsync(path));

        Assert.Contains($"extraction_error_kind={UnpdfErrorKind.UnknownFormat}", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_FromStream_ShouldReportTheKindToo()
    {
        using var stream = new MemoryStream("%PDF-1.7\ngarbage\n"u8.ToArray());

        var ex = await Assert.ThrowsAsync<DocumentProcessingException>(
            () => _reader.ExtractAsync(stream, "broken.pdf"));

        Assert.Contains($"extraction_error_kind={UnpdfErrorKind.PdfParse}", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_ValidPdf_ShouldNotCarryAnErrorKind()
    {
        // Reference behavior: a healthy document must not be tagged with a failure kind.
        var content = await _reader.ExtractAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "oai_gpt-oss_model_card.pdf"));

        Assert.False(content.Hints.ContainsKey("extraction_error_kind"));
        Assert.DoesNotContain(content.Warnings, w => w.Contains("extraction_error_kind", StringComparison.Ordinal));
    }

    /// <summary>
    /// Unpdf's ABI assigns each new failure reason a new number and never reuses an old
    /// one, so a newer native build can hand us a value this enum does not name. It must
    /// pass through as its number instead of throwing or collapsing to null — hence
    /// <c>ToString()</c> rather than <c>Enum.GetName</c> in the formatter.
    /// </summary>
    [Fact]
    public void FormatErrorKind_UnknownValueFromANewerBuild_ShouldPassThroughAsItsNumber()
    {
        var fromTheFuture = (UnpdfErrorKind)9999;

        Assert.Equal("9999", PdfDocumentReader.FormatErrorKind(fromTheFuture));
        Assert.Equal("9999", PdfDocumentReader.SummarizeErrorKinds([fromTheFuture]));
    }

    [Fact]
    public void SummarizeErrorKinds_MixedCauses_ShouldKeepEveryDistinctKindVisible()
    {
        var summary = PdfDocumentReader.SummarizeErrorKinds(
            [UnpdfErrorKind.PdfParse, UnpdfErrorKind.MissingObject, UnpdfErrorKind.PdfParse]);

        Assert.Equal($"{UnpdfErrorKind.PdfParse}+{UnpdfErrorKind.MissingObject}", summary);
    }

    [Fact]
    public void SummarizeErrorKinds_NoKindsObserved_ShouldFallBackToOther()
    {
        // Defensive: "None" means success in Unpdf's vocabulary and must never label a failure.
        Assert.Equal($"{UnpdfErrorKind.Other}", PdfDocumentReader.SummarizeErrorKinds([]));
    }

    private string WriteTempPdf(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fileflux_kind_{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, bytes);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { File.Delete(path); } catch { /* best effort cleanup */ }
        }
        GC.SuppressFinalize(this);
    }
}
