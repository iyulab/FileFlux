using FileFlux;
using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Undoc;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Propagation teeth for the parse-failure diagnostic (<c>extraction_error_kind</c>) on the three
/// Undoc-backed readers (Excel/Word/PowerPoint).
/// <para>
/// Mirrors <see cref="PdfErrorKindPropagationTests"/> for the Unpdf-backed PDF reader. Undoc and
/// Unpdf assign error-kind numbers independently, so the two enums are not comparable, but the
/// message-channel contract (<c>extraction_error_kind=&lt;value&gt;</c> as a tail token) is
/// shared — see <see cref="UndocErrorKindFormatting"/>.
/// </para>
/// <para>
/// Inputs are synthesized rather than committed, and both recipes were verified against
/// Undoc 0.8.0 to land on the documented kinds: a ZIP local-file-header prefix with a garbage
/// body reports <see cref="UndocErrorKind.ZipArchive"/>, and content that is neither a ZIP nor a
/// compound file reports <see cref="UndocErrorKind.UnknownFormat"/>.
/// </para>
/// </summary>
public class UndocErrorKindPropagationTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    [Fact]
    public async Task Excel_ExtractAsync_ZipHeaderWithGarbageBody_ShouldReportZipArchiveKind()
    {
        var path = WriteTemp([0x50, 0x4B, 0x03, 0x04, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08], ".xlsx");

        var ex = await Assert.ThrowsAsync<DocumentProcessingException>(
            () => new ExcelDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains($"extraction_error_kind={UndocErrorKind.ZipArchive}", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Excel_ExtractAsync_NotAnOfficeContainerAtAll_ShouldReportUnknownFormatKind()
    {
        var path = WriteTemp("this is definitely not an xlsx file at all\n"u8.ToArray(), ".xlsx");

        var ex = await Assert.ThrowsAsync<DocumentProcessingException>(
            () => new ExcelDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains($"extraction_error_kind={UndocErrorKind.UnknownFormat}", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain($"extraction_error_kind={UndocErrorKind.ZipArchive}", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Excel_ReadAsync_NotAnOfficeContainerAtAll_ShouldAlsoReportTheKind()
    {
        // Stage 0 opens the document too, so it must carry the same diagnostic.
        var path = WriteTemp("this is definitely not an xlsx file at all\n"u8.ToArray(), ".xlsx");

        var ex = await Assert.ThrowsAsync<DocumentProcessingException>(
            () => new ExcelDocumentReader().ReadAsync(path, TestContext.Current.CancellationToken));

        Assert.Contains($"extraction_error_kind={UndocErrorKind.UnknownFormat}", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Excel_ExtractAsync_FromStream_ShouldReportTheKindToo()
    {
        using var stream = new MemoryStream([0x50, 0x4B, 0x03, 0x04, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]);

        var ex = await Assert.ThrowsAsync<DocumentProcessingException>(
            () => new ExcelDocumentReader().ExtractAsync(stream, "broken.xlsx", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains($"extraction_error_kind={UndocErrorKind.ZipArchive}", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Excel_ExtractAsync_ValidWorkbook_ShouldNotCarryAnErrorKind()
    {
        var content = await new ExcelDocumentReader().ExtractAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "flat-header.xlsx"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("extraction_error_kind", content.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(content.Warnings, w => w.Contains("extraction_error_kind", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Word_ExtractAsync_ZipHeaderWithGarbageBody_ShouldReportZipArchiveKind()
    {
        var path = WriteTemp([0x50, 0x4B, 0x03, 0x04, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08], ".docx");

        var ex = await Assert.ThrowsAsync<DocumentProcessingException>(
            () => new WordDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains($"extraction_error_kind={UndocErrorKind.ZipArchive}", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Word_ExtractAsync_NotAnOfficeContainerAtAll_ShouldReportUnknownFormatKind()
    {
        var path = WriteTemp("nope"u8.ToArray(), ".docx");

        var ex = await Assert.ThrowsAsync<DocumentProcessingException>(
            () => new WordDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains($"extraction_error_kind={UndocErrorKind.UnknownFormat}", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PowerPoint_ExtractAsync_ZipHeaderWithGarbageBody_ShouldReportZipArchiveKind()
    {
        var path = WriteTemp([0x50, 0x4B, 0x03, 0x04, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08], ".pptx");

        var ex = await Assert.ThrowsAsync<DocumentProcessingException>(
            () => new PowerPointDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains($"extraction_error_kind={UndocErrorKind.ZipArchive}", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PowerPoint_ExtractAsync_NotAnOfficeContainerAtAll_ShouldReportUnknownFormatKind()
    {
        var path = WriteTemp("nope"u8.ToArray(), ".pptx");

        var ex = await Assert.ThrowsAsync<DocumentProcessingException>(
            () => new PowerPointDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains($"extraction_error_kind={UndocErrorKind.UnknownFormat}", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Undoc's ABI assigns each new failure reason a new number and never reuses an old one, so a
    /// newer native build can hand us a value this enum does not name. It must pass through as its
    /// number instead of throwing or collapsing to null — hence <c>ToString()</c> rather than
    /// <c>Enum.GetName</c> in the formatter.
    /// </summary>
    [Fact]
    public void FormatErrorKind_UnknownValueFromANewerBuild_ShouldPassThroughAsItsNumber()
    {
        var fromTheFuture = (UndocErrorKind)9999;

        Assert.Equal("9999", UndocErrorKindFormatting.FormatErrorKind(fromTheFuture));
    }

    private string WriteTemp(byte[] bytes, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fileflux_undoc_kind_{Guid.NewGuid():N}{extension}");
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
