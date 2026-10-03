using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FluxCurator.Infrastructure.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// A browser's "Save as PDF" that kept a <c>.docx</c> name, a renamed download, a mail attachment
/// with a mangled extension: the content is a format FileFlux reads, but the name selects a reader
/// that cannot parse it. Reader selection consults the content when the content is decisive, and
/// says what happened; when it is not decisive, the name keeps choosing exactly as before.
///
/// <para>
/// Fixtures are real documents copied under the wrong name, as in
/// <see cref="MisdeclaredContainerRoutingTests"/>: a synthesised file would test the detector
/// against itself.
/// </para>
/// </summary>
public class MisdeclaredFormatRoutingTests : IDisposable
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static readonly string Pdf = Fixture("oai_gpt-oss_model_card.pdf");
    private static readonly string Docx = Fixture("sample-doc.docx");
    private static readonly string Xlsx = Fixture("list-simple.xlsx");
    private static readonly string Pptx = Fixture("sample-slides.pptx");
    private static readonly string Xls = Fixture("legacy-korean.xls");

    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"fileflux-misdeclared-format-{Guid.NewGuid():N}");

    public MisdeclaredFormatRoutingTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private string CopyAs(string source, string newName)
    {
        var target = Path.Combine(_tempDir, newName);
        File.Copy(source, target, overwrite: true);
        return target;
    }

    private static IDocumentProcessor Create(string filePath) => Factory().Create(filePath);

    private static DocumentProcessorFactory Factory() =>
        new DocumentProcessorFactory(new DocumentReaderFactory(), new ChunkerFactory(), loggerFactory: NullLoggerFactory.Instance);

    // === The detector ===

    [Fact]
    public void Detect_NamesTheFormatFromTheContent_NotTheName()
    {
        Assert.Equal(".pdf", FormatSignature.DetectFile(CopyAs(Pdf, "a.docx")));
        Assert.Equal(".docx", FormatSignature.DetectFile(CopyAs(Docx, "b.pdf")));
        Assert.Equal(".xlsx", FormatSignature.DetectFile(CopyAs(Xlsx, "c.pptx")));
        Assert.Equal(".pptx", FormatSignature.DetectFile(CopyAs(Pptx, "d.xlsx")));
    }

    [Fact]
    public void Detect_CompoundFile_ByItsStreams()
    {
        // A compound file holds several formats; the directory tells the ones with a reader apart.
        Assert.Equal(".xls", FormatSignature.DetectFile(Xls));
        Assert.Equal(".hwp", FormatSignature.DetectBytes(
            CompoundFileEncryptionTests.CompoundFile(["Root Entry", "FileHeader", "DocInfo", "BodyText"])));

        // Legacy Word has no reader to route to, and an encrypted OOXML document is left to the declared reader,
        // which reports it as encrypted rather than as some other format.
        Assert.Null(FormatSignature.DetectBytes(CompoundFileEncryptionTests.CompoundFile(["Root Entry", "WordDocument"])));
        Assert.Null(FormatSignature.DetectBytes(
            CompoundFileEncryptionTests.CompoundFile(["Root Entry", "EncryptionInfo", "EncryptedPackage", "Workbook"])));
    }

    [Fact]
    public void Detect_Hwpx_ByItsMimetypeEntry()
    {
        var path = Path.Combine(_tempDir, "report.docx");
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("mimetype").Open()))
                writer.Write("application/hwp+zip");
            using (var writer = new StreamWriter(zip.CreateEntry("Contents/section0.xml").Open()))
                writer.Write("<hs:sec/>");
        }

        Assert.Equal(".hwpx", FormatSignature.DetectFile(path));
    }

    [Fact]
    public void Detect_ContentItCannotTellApart_IsNull()
    {
        // Text and HTML carry no signature at all.
        var html = Path.Combine(_tempDir, "page.docx");
        File.WriteAllText(html, "<html><body>Sign in to download this file</body></html>");
        Assert.Null(FormatSignature.DetectFile(html));

        Assert.Null(FormatSignature.DetectFile(Path.Combine(_tempDir, "absent.docx")));
    }

    [Fact]
    public void Detect_DamagedPackage_IsNull_SoTheDeclaredReaderKeepsItsDiagnosis()
    {
        var bytes = File.ReadAllBytes(Docx);
        Assert.Null(FormatSignature.DetectBytes(bytes[..(bytes.Length / 2)]));
    }

    [Fact]
    public void Detect_ZipThatIsNotAnOfficePackage_IsNull()
    {
        var path = Path.Combine(_tempDir, "archive.docx");
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            // A folder named like a main part, but no [Content_Types].xml: not an OPC package.
            using var writer = new StreamWriter(zip.CreateEntry("word/notes.txt").Open());
            writer.Write("not a document");
        }

        Assert.Null(FormatSignature.DetectFile(path));
    }

    [Fact]
    public void Detect_Stream_RestoresThePosition()
    {
        using var stream = new MemoryStream(File.ReadAllBytes(Pdf));
        stream.Position = 0;

        Assert.Equal(".pdf", FormatSignature.DetectStream(stream));
        Assert.Equal(0, stream.Position);
    }

    // === Routing: the user-visible outcome ===

    [Fact]
    public async Task PdfSavedUnderADocxName_IsReadAsPdf_AndSaysTheNameWasWrong()
    {
        using var processor = Create(CopyAs(Pdf, "가이드.docx"));

        await processor.ExtractAsync(TestContext.Current.CancellationToken);

        var raw = processor.Result.Raw!;
        Assert.False(string.IsNullOrWhiteSpace(raw.Text));
        Assert.Equal(".pdf", raw.File.Extension);
        Assert.Equal("가이드.docx", raw.File.Name);
        Assert.Equal(".docx", raw.Hints["declared_extension"]);
        Assert.Contains(raw.Warnings, w => w.Contains("[extension_mismatch]") && w.Contains(".docx"));
    }

    [Fact]
    public async Task DocxSavedUnderAPdfName_IsReadThroughTheWordReader()
    {
        using var processor = Create(CopyAs(Docx, "report.pdf"));

        await processor.ExtractAsync(TestContext.Current.CancellationToken);

        Assert.Equal(".docx", processor.Result.Raw!.File.Extension);
        Assert.False(string.IsNullOrWhiteSpace(processor.Result.Raw.Text));
        Assert.Equal(".pdf", processor.Result.Raw.Hints["declared_extension"]);
    }

    [Theory]
    [InlineData("budget.docx")]
    [InlineData("budget.xlsx")]
    public async Task LegacyWorkbookUnderAnotherName_IsReadByTheLegacyReader_AndSaysSo(string name)
    {
        using var processor = Create(CopyAs(Xls, name));

        await processor.ExtractAsync(TestContext.Current.CancellationToken);

        Assert.Equal(".xls", processor.Result.Raw!.File.Extension);
        Assert.False(string.IsNullOrWhiteSpace(processor.Result.Raw.Text));
        Assert.Equal(Path.GetExtension(name), processor.Result.Raw.Hints["declared_extension"]);
    }

    [Fact]
    public async Task PdfWithAnExtensionNoReaderClaims_IsStillRead()
    {
        // Before content detection this was "No reader found" — the name selected nothing.
        using var processor = Create(CopyAs(Pdf, "download.bin"));

        await processor.ExtractAsync(TestContext.Current.CancellationToken);

        Assert.Equal(".pdf", processor.Result.Raw!.File.Extension);
        Assert.Equal(".bin", processor.Result.Raw.Hints["declared_extension"]);
    }

    [Fact]
    public async Task MisdeclaredBytes_AreRoutedByContentToo()
    {
        using var processor = Factory().Create(File.ReadAllBytes(Pdf), ".docx", "가이드.docx");

        await processor.ExtractAsync(TestContext.Current.CancellationToken);

        Assert.Equal(".pdf", processor.Result.Raw!.File.Extension);
        Assert.Contains(processor.Result.Raw.Warnings, w => w.Contains("[extension_mismatch]"));
    }

    [Fact]
    public async Task MisdeclaredSeekableStream_IsRoutedByContentToo()
    {
        await using var stream = new MemoryStream(File.ReadAllBytes(Pdf));
        using var processor = Factory().Create(stream, ".docx");

        await processor.ExtractAsync(TestContext.Current.CancellationToken);

        Assert.Equal(".pdf", processor.Result.Raw!.File.Extension);
    }

    [Fact]
    public async Task FluxDocumentProcessor_RoutesByContent()
    {
        var processor = new FluxDocumentProcessor(
            new DocumentReaderFactory(), new DocumentParserFactory(), new ChunkerFactory());

        var raw = await processor.ExtractAsync(CopyAs(Pdf, "가이드.docx"), TestContext.Current.CancellationToken);

        Assert.Equal(".pdf", raw.File.Extension);
        Assert.Equal(".docx", raw.Hints["declared_extension"]);
    }

    [Fact]
    public async Task ACallerHoldingTheReader_IsNotRefusedOverTheName()
    {
        // A consumer that selected the PDF reader itself (or the factory's content path) hands it a
        // name the content contradicts; the reader reads the content instead of refusing the name.
        var raw = await new PdfDocumentReader().ExtractAsync(
            CopyAs(Pdf, "가이드.docx"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(raw.Text));
    }

    // === Controls ===

    [Fact]
    public async Task CorrectlyNamedDocuments_CarryNoMismatchNote()
    {
        foreach (var source in new[] { Pdf, Docx })
        {
            using var processor = Create(CopyAs(source, Path.GetFileName(source)));

            await processor.ExtractAsync(TestContext.Current.CancellationToken);

            Assert.False(processor.Result.Raw!.Hints.ContainsKey("declared_extension"));
            Assert.DoesNotContain(processor.Result.Raw.Warnings, w => w.Contains("[extension_mismatch]"));
        }
    }

    [Fact]
    public async Task ContentNoReaderParses_StillFailsAsBefore_WithoutClaimingAFormat()
    {
        var path = Path.Combine(_tempDir, "error-page.docx");
        await File.WriteAllTextAsync(path, "<html><body>Sign in to download this file</body></html>", TestContext.Current.CancellationToken);

        using var processor = Create(path);
        var ex = await Assert.ThrowsAsync<DocumentProcessingException>(
            () => processor.ExtractAsync(TestContext.Current.CancellationToken));

        Assert.Contains("container_mismatch", ex.ToString());
        Assert.DoesNotContain("detected_extension", ex.ToString());
    }

    [Fact]
    public async Task WordReaderHandedAPdf_NamesWhatTheContentIs()
    {
        // The reader is no longer reached this way through the factory, but a caller holding the
        // Word reader directly gets the diagnosis AIMS asked for: what the file actually is.
        var ex = await Assert.ThrowsAsync<DocumentProcessingException>(() =>
            new WordDocumentReader().ExtractAsync(CopyAs(Pdf, "가이드.docx"), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("[extraction_failure_reason=container_mismatch]", ex.Message);
        Assert.Contains("[detected_extension=.pdf]", ex.Message);
        Assert.Contains("PDF", ex.Message);
    }
}
