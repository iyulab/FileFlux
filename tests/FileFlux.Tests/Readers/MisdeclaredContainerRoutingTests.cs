using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// A file's extension is a claim about its container, and in the field the claim is often wrong:
/// a legacy compound-file workbook gets saved or copied under an <c>.xlsx</c> name, reaches the
/// OOXML reader, and fails with "could not find EOCD". That message is accurate about a ZIP package
/// and reads to a user as "your file is corrupt" — while the file is valid and a reader for it has
/// existed since 0.14.0. The only thing wrong was which reader got picked.
///
/// <para>
/// Renaming happens in both directions, so both are pinned here. Fixtures are the existing real
/// workbooks copied under the wrong name: synthesising a container would test the detector against
/// itself rather than against a file some tool actually produced.
/// </para>
/// </summary>
public class MisdeclaredContainerRoutingTests : IDisposable
{
    private static readonly string LegacyFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-korean.xls");

    private static readonly string OoxmlFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "list-simple.xlsx");

    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"fileflux-misdeclared-{Guid.NewGuid():N}");

    public MisdeclaredContainerRoutingTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private string CopyAs(string sourceFixture, string newName)
    {
        var target = Path.Combine(_tempDir, newName);
        File.Copy(sourceFixture, target, overwrite: true);
        return target;
    }

    // === The detector itself ===

    [Fact]
    public void Detect_ReadsTheContainerFromTheBytes_NotTheName()
    {
        Assert.Equal(OfficeContainer.CompoundFile, ContainerSignature.DetectFile(LegacyFixture));
        Assert.Equal(OfficeContainer.Zip, ContainerSignature.DetectFile(OoxmlFixture));
    }

    [Fact]
    public void Detect_ContentThatIsNeitherContainer_IsUnknown()
    {
        var path = Path.Combine(_tempDir, "not-a-workbook.xlsx");
        File.WriteAllText(path, "<html><body>Sign in to download this file</body></html>");

        Assert.Equal(OfficeContainer.Unknown, ContainerSignature.DetectFile(path));
    }

    [Fact]
    public void Detect_MissingFile_IsUnknownRatherThanThrowing()
    {
        // A probe failure must never turn a readable document into an error of its own; the caller
        // carries on to whichever reader the extension would have chosen.
        Assert.Equal(
            OfficeContainer.Unknown,
            ContainerSignature.DetectFile(Path.Combine(_tempDir, "absent.xlsx")));
    }

    // === Routing: the user-visible outcome ===

    [Fact]
    public async Task XlsxNamedCompoundFile_ExtractsInsteadOfFailingOnTheZipHeader()
    {
        var path = CopyAs(LegacyFixture, "quotation.xlsx");

        var content = await new ExcelDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(content.Text));
        Assert.Contains("견적서", content.Text);
        Assert.Equal("LegacyExcelReader", content.ReaderType);
        // The container that was parsed, not the name it arrived under - a consumer routing on this
        // would otherwise inherit the same mislabelling.
        Assert.Equal(".xls", content.File.Extension);
    }

    [Fact]
    public async Task XlsNamedOoxmlPackage_ExtractsThroughTheOoxmlReader()
    {
        var path = CopyAs(OoxmlFixture, "list.xls");

        var content = await new LegacyExcelDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(content.Text));
        Assert.Equal("ExcelReader", content.ReaderType);
    }

    [Fact]
    public async Task MisdeclaredContainer_RoutesFromAStreamToo()
    {
        // The stream and byte entry points are what StatefulDocumentProcessor uses, and they are
        // handed only an extension - so routing that worked on the file path alone would leave them
        // behind, which is the "fixed everywhere except one path" shape.
        await using var stream = File.OpenRead(LegacyFixture);

        var content = await new ExcelDocumentReader().ExtractAsync(stream, "quotation.xlsx", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("견적서", content.Text);
        Assert.Equal("LegacyExcelReader", content.ReaderType);
    }

    // === Correctly named files are untouched ===

    [Fact]
    public async Task CorrectlyNamedWorkbooks_AreUnaffected()
    {
        var ooxml = await new ExcelDocumentReader().ExtractAsync(OoxmlFixture, cancellationToken: TestContext.Current.CancellationToken);
        var legacy = await new LegacyExcelDocumentReader().ExtractAsync(LegacyFixture, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ExcelReader", ooxml.ReaderType);
        Assert.Equal("LegacyExcelReader", legacy.ReaderType);
        Assert.False(string.IsNullOrWhiteSpace(ooxml.Text));
        Assert.False(string.IsNullOrWhiteSpace(legacy.Text));
    }

    // === What routing cannot save ===

    [Fact]
    public async Task ContentThatIsNoWorkbookAtAll_SaysSo_RatherThanBlamingTheZipHeader()
    {
        var path = Path.Combine(_tempDir, "error-page.xlsx");
        await File.WriteAllTextAsync(path, "<html><body>Sign in to download this file</body></html>", TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<DocumentProcessingException>(
            () => new ExcelDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("container_mismatch", ex.Message);
    }

    /// <summary>
    /// Word and PowerPoint get the diagnosis but not the routing: there is no legacy .doc/.ppt
    /// reader to hand off to, so the honest outcome is to say what the file is rather than to
    /// borrow the OOXML parser's complaint about a ZIP archive.
    /// </summary>
    [Theory]
    [InlineData("renamed.docx")]
    [InlineData("renamed.pptx")]
    public async Task LegacyCompoundFileUnderAnOoxmlName_IsReportedAsAMismatch_NotAsCorruption(string name)
    {
        var path = CopyAs(LegacyFixture, name);

        var ex = await Assert.ThrowsAsync<DocumentProcessingException>(() => name.EndsWith(".docx")
            ? new WordDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken) : new PowerPointDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("container_mismatch", ex.Message);
    }

    [Theory]
    [InlineData("valid.docx")]
    [InlineData("valid.pptx")]
    public async Task RealOoxmlDocuments_AreStillReadWithoutAMismatchNote(string name)
    {
        // Guards the annotation against firing on documents that are simply fine - a note that
        // appears everywhere carries no information.
        var fixture = name.EndsWith(".docx") ? "sample-doc.docx" : "sample-slides.pptx";
        var path = CopyAs(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), name);

        var content = name.EndsWith(".docx")
            ? await new WordDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken) : await new PowerPointDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(content.Text));
    }

    [Fact]
    public async Task DamagedPackage_KeepsTheParsersOwnDiagnosis()
    {
        // Truncating after the ZIP magic keeps it classifiable as a package, so this is a damaged
        // workbook rather than a mislabelled one and must not be reported as a container mismatch.
        var bytes = await File.ReadAllBytesAsync(OoxmlFixture, TestContext.Current.CancellationToken);
        var path = Path.Combine(_tempDir, "truncated.xlsx");
        await File.WriteAllBytesAsync(path, bytes[..(bytes.Length / 2)], TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<DocumentProcessingException>(
            () => new ExcelDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken));

        Assert.DoesNotContain("container_mismatch", ex.Message);
    }
}
