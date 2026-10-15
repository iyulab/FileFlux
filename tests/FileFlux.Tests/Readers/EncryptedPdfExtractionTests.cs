using System.Globalization;
using System.Text;
using AwesomeAssertions;
using FileFlux.Core.Infrastructure.Readers;

namespace FileFlux.Tests.Readers;

/// <summary>
/// A password-protected PDF fails the way a password-protected Office file does: an
/// <see cref="FileFlux.Core.EncryptedDocumentException"/> carrying <c>extraction_failure_reason=encrypted_document</c>, so a
/// consumer maps one cause to one message whatever the format. Before, the PDF reader reported only the Unpdf diagnostic
/// kind (<c>extraction_error_kind=Encrypted</c>) inside a generic processing failure.
/// </summary>
public class EncryptedPdfExtractionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fileflux-encpdf-" + Guid.NewGuid().ToString("N"));

    public EncryptedPdfExtractionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort temp cleanup */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AnEncryptedPdf_FailsAsEncrypted_WithTheSameFailureReasonAsOfficeFiles()
    {
        var path = Path.Combine(_dir, "locked.pdf");
        await File.WriteAllBytesAsync(path, EncryptedPdf(), TestContext.Current.CancellationToken);

        var act = async () => await new PdfDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        var ex = (await act.Should().ThrowAsync<FileFlux.Core.EncryptedDocumentException>()).Which;
        ex.Message.Should().Contain("[extraction_failure_reason=encrypted_document]");
        ex.IsPermanent.Should().BeTrue();
    }

    [Fact]
    public async Task AnEncryptedPdfStream_FailsAsEncrypted()
    {
        await using var stream = new MemoryStream(EncryptedPdf());

        var act = async () => await new PdfDocumentReader().ExtractAsync(stream, "locked.pdf", cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<FileFlux.Core.EncryptedDocumentException>())
            .Which.Message.Should().Contain("[extraction_failure_reason=encrypted_document]");
    }

    /// <summary>
    /// A one-page PDF whose trailer names a standard security handler (AES-256, revision 6) with key material no
    /// password matches — what a password-protected file looks like to a reader without the password.
    /// </summary>
    private static byte[] EncryptedPdf()
    {
        static string Hex(byte value, int count) => "<" + string.Concat(Enumerable.Repeat(value.ToString("X2", CultureInfo.InvariantCulture), count)) + ">";

        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >>",
            "<< /Length 0 >>\nstream\n\nendstream",
            $"<< /Filter /Standard /V 5 /R 6 /Length 256 /P -4 /O {Hex(0x11, 48)} /U {Hex(0x22, 48)} " +
            $"/OE {Hex(0x33, 32)} /UE {Hex(0x44, 32)} /Perms {Hex(0x55, 16)} " +
            "/CF << /StdCF << /AuthEvent /DocOpen /CFM /AESV3 /Length 32 >> >> /StmF /StdCF /StrF /StdCF >>",
        };

        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R /Encrypt 5 0 R ");
        pdf.Append("/ID [<0123456789ABCDEF0123456789ABCDEF> <0123456789ABCDEF0123456789ABCDEF>] >>\n");
        pdf.Append(CultureInfo.InvariantCulture, $"startxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
