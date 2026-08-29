using System.Text;
using System.Text.RegularExpressions;
using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Teeth for the silent page-loss signal (Unpdf 0.11.0 <c>PagesIncomplete</c>).
/// <para>
/// A PDF whose page tree only partly resolves still parses and still extracts — just
/// not all of it. Before this signal existed the shortfall was reported as a complete
/// document, so an indexer stored a fraction of the text as if it were the whole and a
/// missing page was indistinguishable from a page that never existed.
/// </para>
/// <para>
/// The fixture is assembled byte by byte in-test rather than committed: the damage
/// recipe <i>is</i> the fixture, and a hand-built page tree makes the failure exact —
/// one kid object of the root <c>Pages</c> node is written as an unterminated
/// dictionary, so that node cannot be resolved while the rest of the file stays valid.
/// </para>
/// </summary>
public class PdfPageIntegrityTests : IDisposable
{
    private readonly PdfDocumentReader _reader = new();
    private readonly List<string> _tempFiles = [];

    [Fact]
    public async Task ExtractAsync_DamagedPageTree_ShouldReportTheExtractionAsIncomplete()
    {
        var path = WriteTempPdf(BuildTwoPagePdf(damageSecondPage: true));

        var content = await _reader.ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(true, content.Hints["pages_incomplete"]);
        Assert.Equal(ProcessingStatus.Partial, content.Status);
        Assert.Contains(content.Warnings, w => w.Contains("missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExtractAsync_DamagedPageTree_ShouldStillReturnTheRecoveredText()
    {
        // The point of the signal is to qualify a partial result, not to discard it.
        var path = WriteTempPdf(BuildTwoPagePdf(damageSecondPage: true));

        var content = await _reader.ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("PAGE ONE", content.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("PAGE TWO", content.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_DamagedPageTree_ShouldPublishTheDeclaredPageCountAgainstTheExtractedOne()
    {
        // The shortfall is evidence the consumer can weigh; the reader states both numbers
        // and asserts neither as a loss figure.
        var path = WriteTempPdf(BuildTwoPagePdf(damageSecondPage: true));

        var content = await _reader.ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2L, content.Hints["declared_page_count"]);
        Assert.Equal(1, content.Hints["page_count"]);
    }

    [Fact]
    public async Task ExtractAsync_DamagedPageTree_WarningMustNotClaimHowManyPagesWereLost()
    {
        // One unresolved node drops its whole subtree, so the number of lost pages is not
        // knowable from the signal. Naming a figure would be a fabrication the consumer
        // would then report to users.
        var path = WriteTempPdf(BuildTwoPagePdf(damageSecondPage: true));

        var content = await _reader.ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        var warning = Assert.Single(content.Warnings, w => w.Contains("missing", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotMatch(new Regex(@"\d+\s+page", RegexOptions.IgnoreCase), warning);
    }

    [Fact]
    public async Task ExtractAsync_IntactMultiPagePdf_ShouldNotFlagIncompleteness()
    {
        // Same builder, no damage: the signal must not fire on healthy documents.
        var path = WriteTempPdf(BuildTwoPagePdf(damageSecondPage: false));

        var content = await _reader.ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(content.Hints.ContainsKey("pages_incomplete"));
        Assert.False(content.Hints.ContainsKey("declared_page_count"));
        Assert.Equal(ProcessingStatus.Completed, content.Status);
        Assert.Contains("PAGE TWO", content.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_DamagedPageTree_ShouldCarryTheSameSignal()
    {
        // Stage 0 is where the page count is stated, so it is the surface where a short
        // page set most easily passes for a whole document.
        var path = WriteTempPdf(BuildTwoPagePdf(damageSecondPage: true));

        var result = await _reader.ReadAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(true, result.DocumentProps["pages_incomplete"]);
        Assert.Equal(2L, result.DocumentProps["declared_page_count"]);
        Assert.Equal(ProcessingStatus.Partial, result.Status);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ReadAsync_IntactPdf_ShouldNotFlagIncompleteness()
    {
        var path = WriteTempPdf(BuildTwoPagePdf(damageSecondPage: false));

        var result = await _reader.ReadAsync(path, TestContext.Current.CancellationToken);

        Assert.False(result.DocumentProps.ContainsKey("pages_incomplete"));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExtractAsync_RealWorldPdf_ShouldNotFlagIncompleteness()
    {
        // A synthetic control cannot rule out false positives on real generator output.
        var content = await _reader.ExtractAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "oai_gpt-oss_model_card.pdf"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(content.Hints.ContainsKey("pages_incomplete"));
        Assert.Equal(ProcessingStatus.Completed, content.Status);
    }

    /// <summary>
    /// Two-page PDF with a real page tree. When <paramref name="damageSecondPage"/> is set,
    /// the second kid is written as an unterminated dictionary: the node cannot be resolved,
    /// its page never reaches the output, and the document still parses and extracts.
    /// </summary>
    private static byte[] BuildTwoPagePdf(bool damageSecondPage)
    {
        static string Page(int contentsObject) =>
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 300 200]/Resources<</Font<</F1 6 0 R>>>>" +
            $"/Contents {contentsObject} 0 R>>";

        var objects = new[]
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R 4 0 R]/Count 2>>",
            Page(5),
            damageSecondPage
                ? "<</Type/Page/Parent 2 0 R/MediaBox[0 0 300 %%%]"   // unterminated: unresolvable node
                : Page(7),
            ContentStream("BT /F1 14 Tf 20 100 Td (PAGE ONE) Tj ET\n"),
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
            ContentStream("BT /F1 14 Tf 20 100 Td (PAGE TWO) Tj ET\n"),
        };

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
        var path = Path.Combine(Path.GetTempPath(), $"fileflux_integrity_{Guid.NewGuid():N}.pdf");
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
