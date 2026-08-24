using System.Text;
using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Unpdf;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Teeth for the silent decode-loss signal (Unpdf 0.12.0 <c>ExtractionQuality.SuppressedTextRuns</c>).
/// <para>
/// A run is one text string handed to the font decoder (a <c>Tj</c> operand). When the font's
/// character codes cannot be resolved, the decoder discards the run rather than emit mojibake —
/// before this signal existed that loss was invisible: the document still parsed and extraction
/// still reported success with the run's content simply missing.
/// </para>
/// <para>
/// This must not collapse into <c>extraction_failure_reason=no_text_layer</c>: that reason's
/// wording asserts "OCR required", which is wrong here — a page whose text operators were
/// present and discarded at decode time is not a scanned document (this repo already corrected
/// exactly this class of misattribution once, see <c>DescribePagesExhausted</c>).
/// </para>
/// <para>
/// The fixture is assembled byte by byte rather than committed, mirroring
/// <c>PdfPageIntegrityTests</c>: a Type0/CIDFontType2 composite font using
/// <c>/Encoding /Identity-H</c> with no <c>ToUnicode</c> map and no embedded cmap gives the
/// decoder no way to turn its CIDs into characters — proven against the real Unpdf.Net native
/// library by unpdf's own upstream test suite (<c>tests/suppression_reporting_test.rs</c>,
/// <c>unresolvable_composite_font_reports_suppressed_runs</c>).
/// </para>
/// </summary>
public class PdfSuppressedTextRunsTests : IDisposable
{
    private readonly PdfDocumentReader _reader = new();
    private readonly List<string> _tempFiles = [];

    [Fact]
    public async Task ExtractAsync_UnresolvableCompositeFont_ShouldReportSuppressedTextRunCount()
    {
        var path = WriteTempPdf(BuildUnresolvableCompositeFontPdf());

        var content = await _reader.ExtractAsync(path);

        Assert.True(content.Hints.TryGetValue("suppressed_text_runs", out var count));
        Assert.True((long)count! > 0);
    }

    [Fact]
    public async Task ExtractAsync_UnresolvableCompositeFont_ShouldUseTextRunsSuppressedReason_NotNoTextLayer()
    {
        // The whole document is one discarded Tj, so extraction yields no text at all —
        // the empty-document classifier must prefer this reason over the generic
        // "no_text_layer" fallback it would otherwise default to.
        var path = WriteTempPdf(BuildUnresolvableCompositeFontPdf());

        var content = await _reader.ExtractAsync(path);

        Assert.Equal(string.Empty, content.Text);
        Assert.Equal("text_runs_suppressed", content.Hints["extraction_failure_reason"]);
    }

    [Fact]
    public async Task ExtractAsync_UnresolvableCompositeFont_WarningMustNotClaimOcrIsNeeded()
    {
        // The warning may still mention OCR/scanning defensively (to say this is NOT
        // that case) — what it must never do is assert OCR is required, the exact claim
        // that misattributed this class of loss before (2026-07-31 field report).
        var path = WriteTempPdf(BuildUnresolvableCompositeFontPdf());

        var content = await _reader.ExtractAsync(path);

        Assert.DoesNotContain(content.Warnings, w => w.Contains("requires OCR", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExtractAsync_UnresolvableCompositeFont_ShouldMarkStatusPartial()
    {
        var path = WriteTempPdf(BuildUnresolvableCompositeFontPdf());

        var content = await _reader.ExtractAsync(path);

        Assert.Equal(ProcessingStatus.Partial, content.Status);
    }

    [Fact]
    public async Task ExtractAsync_ReadableDocument_ShouldNotReportSuppressedTextRuns()
    {
        // Control: an ordinary Type1 font that decodes cleanly must not false-positive.
        var path = WriteTempPdf(BuildReadableFontPdf());

        var content = await _reader.ExtractAsync(path);

        Assert.False(content.Hints.ContainsKey("suppressed_text_runs"));
        Assert.False(content.Hints.ContainsKey("extraction_failure_reason"));
        Assert.Contains("Readable text", content.Text, StringComparison.Ordinal);
        Assert.Equal(ProcessingStatus.Completed, content.Status);
    }

    [Fact]
    public async Task ReadAsync_UnresolvableCompositeFont_ShouldCarryTheSameSignal()
    {
        // Stage 0 states page/document metadata before Stage 1 ever runs, so the signal
        // has to reach there too — mirrors PagesIncomplete's stage-0 wiring.
        var path = WriteTempPdf(BuildUnresolvableCompositeFontPdf());

        var result = await _reader.ReadAsync(path);

        Assert.True(result.DocumentProps.TryGetValue("suppressed_text_runs", out var count));
        Assert.True((long)count! > 0);
        Assert.Equal(ProcessingStatus.Partial, result.Status);
    }

    [Fact]
    public async Task ReadAsync_ReadableDocument_ShouldNotFlagSuppression()
    {
        var path = WriteTempPdf(BuildReadableFontPdf());

        var result = await _reader.ReadAsync(path);

        Assert.False(result.DocumentProps.ContainsKey("suppressed_text_runs"));
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// Teeth for the Unpdf 0.14.0 binding upgrade itself: before this version, <c>PageStats</c>
    /// had no field for the native core's per-page breakdown, so the only signal reachable from
    /// C# was <c>ExtractionQuality.SuppressedTextRuns</c> — the whole-document total. This
    /// asserts the binding now surfaces the page attributed to the loss, not just that
    /// classification (already covered above) still lands correctly.
    /// </summary>
    [Fact]
    public void GetPageStats_UnresolvableCompositeFont_ShouldReportPerPageSuppressedTextRunCount()
    {
        var path = WriteTempPdf(BuildUnresolvableCompositeFontPdf());
        using var doc = UnpdfDocument.ParseFile(path);

        var stats = doc.GetPageStats(1);

        Assert.True(stats.SuppressedTextRuns > 0);
    }

    [Fact]
    public void GetPageStats_ReadableDocument_ShouldNotReportSuppressedTextRuns()
    {
        var path = WriteTempPdf(BuildReadableFontPdf());
        using var doc = UnpdfDocument.ParseFile(path);

        var stats = doc.GetPageStats(1);

        Assert.Equal(0, stats.SuppressedTextRuns);
    }

    /// <summary>
    /// One page whose only content is a Tj against an Identity-H composite font with no
    /// ToUnicode map and no embedded cmap — the decoder cannot resolve the CIDs and drops
    /// the run. Mirrors unpdf's own <c>unresolvable_composite_pdf()</c> fixture.
    /// </summary>
    private static byte[] BuildUnresolvableCompositeFontPdf()
    {
        var content = "BT /F1 12 Tf 72 720 Td (\\001\\102\\001\\103) Tj ET\n";
        var objects = new[]
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>",
            ContentStream(content),
            "<</Type/Font/Subtype/Type0/BaseFont/NoMap/Encoding/Identity-H/DescendantFonts[6 0 R]>>",
            "<</Type/Font/Subtype/CIDFontType2/BaseFont/NoMap/CIDSystemInfo" +
                "<</Registry(Adobe)/Ordering(Identity)/Supplement 0>>>>",
        };

        return Assemble(objects);
    }

    /// <summary>An ordinary Type1/Helvetica font whose text decodes cleanly — nothing to suppress.</summary>
    private static byte[] BuildReadableFontPdf()
    {
        var content = "BT /F1 12 Tf 72 720 Td (Readable text) Tj ET\n";
        var objects = new[]
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>",
            ContentStream(content),
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
        };

        return Assemble(objects);
    }

    private static byte[] Assemble(string[] objects)
    {
        using var buffer = new MemoryStream();
        Write(buffer, "%PDF-1.4\n");
        buffer.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]);   // binary marker

        var offsets = new List<long>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(buffer.Position);
            Write(buffer, $"{i + 1} 0 obj{objects[i]}endobj\n");
        }

        var xref = buffer.Position;
        Write(buffer, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            Write(buffer, $"{offset:D10} 00000 n \n");
        Write(buffer, $"trailer<</Size {objects.Length + 1}/Root 1 0 R>>\nstartxref\n{xref}\n%%EOF\n");

        return buffer.ToArray();
    }

    private static string ContentStream(string content)
        => $"<</Length {content.Length}>>stream\n{content}endstream";

    private static void Write(Stream stream, string text) => stream.Write(Encoding.ASCII.GetBytes(text));

    private string WriteTempPdf(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fileflux_suppressed_runs_{Guid.NewGuid():N}.pdf");
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
