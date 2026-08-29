using System.Text;
using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Regression teeth for text carrying control characters.
/// <para>
/// A PDF string literal may legitimately contain control bytes — <c>\000</c> is a valid
/// octal escape — and documents in the field do. Extraction used to abort the page over
/// them, so a document that parsed perfectly reported every page as failed. The fix is
/// output normalization in the native library (Unpdf 0.11.0); this asserts the outcome
/// that matters here, which is that such a document extracts at all.
/// </para>
/// <para>
/// Deliberately not an attribution test: whether the NUL is dropped by the native library
/// or by the reader's own cross-reader sanitizer is not observable from here, and does not
/// need to be. The reported symptom was the <see cref="DocumentProcessingException"/>,
/// which was thrown upstream of any sanitizing this assembly does — pinning Unpdf back to
/// 0.10.0 makes this test fail with <c>extraction_error_kind=InvalidOutput</c>.
/// </para>
/// </summary>
public class PdfControlCharacterExtractionTests : IDisposable
{
    private readonly PdfDocumentReader _reader = new();
    private readonly List<string> _tempFiles = [];

    [Fact]
    public async Task ExtractAsync_TextLiteralContainingNul_ShouldExtractInsteadOfFailing()
    {
        var path = WriteTempPdf(BuildSinglePagePdf(@"HELLO\000WORLD"));

        var content = await _reader.ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("HELLO", content.Text, StringComparison.Ordinal);
        Assert.Contains("WORLD", content.Text, StringComparison.Ordinal);
        Assert.DoesNotContain('\0', content.Text);
        Assert.Equal(ProcessingStatus.Completed, content.Status);
        Assert.False(content.Hints.ContainsKey("extraction_error_kind"));
    }

    [Fact]
    public async Task ExtractAsync_TextLiteralWithoutNul_ShouldBeUnaffected()
    {
        // Control: the same document without the escape must read the same way.
        var path = WriteTempPdf(BuildSinglePagePdf("HELLO WORLD"));

        var content = await _reader.ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("HELLO WORLD", content.Text, StringComparison.Ordinal);
        Assert.Equal(ProcessingStatus.Completed, content.Status);
    }

    /// <summary>
    /// Single-page PDF whose one text-showing operator draws <paramref name="literal"/>,
    /// written verbatim into the content stream so PDF escapes stay escapes.
    /// </summary>
    private static byte[] BuildSinglePagePdf(string literal)
    {
        var content = $"BT /F1 14 Tf 20 100 Td ({literal}) Tj ET\n";

        var objects = new[]
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 300 200]/Resources<</Font<</F1 4 0 R>>>>/Contents 5 0 R>>",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
            $"<</Length {content.Length}>>stream\n{content}endstream",
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

    private static void Write(Stream stream, string text) => stream.Write(Encoding.ASCII.GetBytes(text));

    private string WriteTempPdf(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fileflux_ctrl_{Guid.NewGuid():N}.pdf");
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
