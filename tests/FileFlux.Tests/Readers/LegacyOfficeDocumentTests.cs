using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FluxCurator.Infrastructure.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Word 97-2003 (<c>.doc</c>) and PowerPoint 97-2003 (<c>.ppt</c>) documents are read by the Word and
/// PowerPoint readers, through the same parser as their OOXML successors. Before, the parser could not read
/// them and a <c>.doc</c> was refused by name.
///
/// <para>
/// Fixtures are real files produced by Office (Apache POI's test data, see
/// <c>Fixtures/NOTICE-apache-poi.txt</c>): a synthesised compound file would test the detector against itself.
/// </para>
/// </summary>
public class LegacyOfficeDocumentTests : IDisposable
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static readonly string Doc = Fixture("legacy-table.doc");
    private static readonly string Ppt = Fixture("legacy-slides.ppt");

    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"fileflux-legacy-office-{Guid.NewGuid():N}");

    public LegacyOfficeDocumentTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private string CopyAs(string fixture, string name)
    {
        var path = Path.Combine(_tempDir, name);
        File.Copy(fixture, path, overwrite: true);
        return path;
    }

    // === Reading ===

    [Fact]
    public async Task ALegacyWordDocument_IsRead_WithItsTable()
    {
        var content = await new WordDocumentReader().ExtractAsync(Doc, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("created using Word 97", content.Text);
        Assert.Contains("This text is below the table.", content.Text);
        var table = Assert.Single(content.Tables);
        Assert.Equal(2, table.RowCount);
        Assert.Equal(3, table.ColumnCount);
        Assert.Equal("Cell 2,3", table.Cells[1][2]);
        Assert.Equal(".doc", content.File.Extension);
        Assert.Equal("WordReader", content.ReaderType);
    }

    [Fact]
    public async Task ALegacyPresentation_IsRead_WithItsSlidesAndNotes()
    {
        var content = await new PowerPointDocumentReader().ExtractAsync(Ppt, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("This is a test title", content.Text);
        Assert.Contains("This is the title on page 2", content.Text);
        Assert.Contains("These are the notes for page 1", content.Text);
        Assert.Equal(".ppt", content.File.Extension);
    }

    [Fact]
    public async Task TheStreamEntryPoint_ReadsALegacyDocumentToo()
    {
        await using var stream = File.OpenRead(Doc);

        var content = await new WordDocumentReader().ExtractAsync(stream, "memo.doc", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("created using Word 97", content.Text);
        Assert.Equal(".doc", content.File.Extension);
    }

    [Theory]
    [InlineData("memo.doc", "WordReader")]
    [InlineData("deck.ppt", "PowerPointReader")]
    public void TheReaderFactory_PicksTheReaderByTheLegacyExtension(string name, string readerType)
    {
        var reader = new DocumentReaderFactory().GetReader(name);

        Assert.NotNull(reader);
        Assert.Equal(readerType, reader.ReaderType);
    }

    [Fact]
    public void TheDetector_NamesALegacyDocumentByItsStreams()
    {
        Assert.Equal(".doc", FormatSignature.DetectFile(Doc));
        Assert.Equal(".ppt", FormatSignature.DetectFile(Ppt));
    }

    // === Misnamed files ===

    [Fact]
    public async Task ALegacyDocumentUnderAnOoxmlName_IsRead_AndTheMismatchIsNoted()
    {
        // The pre-2007 file saved under the new name is the common case; the processor reads it and says so.
        var path = CopyAs(Doc, "memo.docx");
        using var processor = new DocumentProcessorFactory(new DocumentReaderFactory(), new ChunkerFactory(), loggerFactory: NullLoggerFactory.Instance)
            .Create(path);

        await processor.ExtractAsync(cancellationToken: TestContext.Current.CancellationToken);

        var content = processor.Result.Raw!;
        Assert.Contains("created using Word 97", content.Text);
        Assert.Equal(".doc", content.File.Extension);
        Assert.Contains(content.Warnings, w => w.Contains("[extension_mismatch]"));
    }

    [Theory]
    [InlineData("list-simple.xlsx", "report.docx")]
    [InlineData("sample-doc.docx", "deck.pptx")]
    public async Task AnotherOoxmlFormat_HandedToTheWrongReader_IsNamed_NotReadAsThatReadersFormat(string fixture, string name)
    {
        // The parser reads any Office container it is handed, so without the guard a workbook would come back
        // from the Word reader labelled as a Word document.
        var path = CopyAs(Fixture(fixture), name);

        var ex = await Assert.ThrowsAsync<DocumentProcessingException>(() => name.EndsWith(".docx")
            ? new WordDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken)
            : new PowerPointDocumentReader().ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("container_mismatch", ex.Message);
        Assert.Contains("detected_extension=", ex.Message);
    }

    // === Slides of the pictures ===

    [Fact]
    public async Task PresentationImages_CarryTheSlideThatShowsThem_FromAFileAndFromBytes()
    {
        // slide-images.pptx (made with python-pptx): slide 1 shows one picture, slide 2 none, slide 3 a group of two.
        var path = Fixture("slide-images.pptx");
        var reader = new PowerPointDocumentReader();

        var fromFile = await reader.ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        await using var stream = File.OpenRead(path);
        var fromBytes = await reader.ExtractAsync(stream, "deck.pptx", cancellationToken: TestContext.Current.CancellationToken);

        foreach (var content in new[] { fromFile, fromBytes })
        {
            Assert.Equal(3, content.Images.Count);
            Assert.Equal([1, 3, 3], content.Images.Select(i => i.PageNumber ?? 0).Order().ToArray());
            Assert.DoesNotContain(content.Images, i => i.PageNumber == 2);
        }
    }

    // === What cannot be read ===

    [Theory]
    [InlineData("legacy-encrypted.doc")]
    [InlineData("legacy-encrypted.ppt")]
    [InlineData("legacy-encrypted.xls")]
    public async Task APasswordProtectedLegacyFile_FailsAsEncrypted(string fixture)
    {
        // The 97-2003 formats protect a file inside its own streams, not with the EncryptedPackage stream the
        // container probe looks for, so it is the parser that finds out - and it must fail the same way.
        var path = Fixture(fixture);
        IDocumentReader reader = fixture.EndsWith(".doc") ? new WordDocumentReader()
            : fixture.EndsWith(".ppt") ? new PowerPointDocumentReader()
            : new LegacyExcelDocumentReader();

        var ex = await Assert.ThrowsAsync<EncryptedDocumentException>(
            () => reader.ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(ex.IsPermanent);
        Assert.NotNull(ex.FileName);
        Assert.Contains("[extraction_failure_reason=encrypted_document]", ex.Message);
    }

    [Fact]
    public async Task AWord95Document_FailsWithTheParsersReason()
    {
        var ex = await Assert.ThrowsAsync<DocumentProcessingException>(
            () => new WordDocumentReader().ExtractAsync(Fixture("legacy-word6.doc"), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("extraction_error_kind=UnsupportedFormat", ex.Message);
        Assert.Contains("Word 6.0/95", ex.Message);
    }
}
