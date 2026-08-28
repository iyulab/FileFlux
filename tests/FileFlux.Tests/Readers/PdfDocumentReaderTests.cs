using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;
using System.Text;

namespace FileFlux.Tests.Readers;

/// <summary>
/// PdfDocumentReader unit tests — .pdf extraction via Unpdf (Rust FFI).
///
/// Fixture: Fixtures/oai_gpt-oss_model_card.pdf — the committed real-world PDF from
/// tests/test-pdf, reused via a csproj Link (no duplicated binary). A multi-page text PDF used
/// as a positive-extraction fixture. Assertions are deliberately STRUCTURAL (page count,
/// character-count floor, no failure hint) rather than tied to the document's third-party
/// editorial text, so the test guards the reader — not this specific vendor document.
///
/// Replaces the previous tests that pointed at a hardcoded absolute path outside the repo
/// (always self-skipped, and which probed non-existent hint keys `PageCount`/`TotalCharacters`
/// that the reader never emits) and four vacuous always-pass placeholders. The no-text-layer /
/// blank-page classification path is covered separately by
/// <see cref="PdfNoTextLayerClassificationTests"/>; this class covers the positive multi-page
/// text-extraction path. No exact character-count assertion (Unpdf formatting shifts on bump).
/// </summary>
public class PdfDocumentReaderTests
{
    private static readonly string ModelCardFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "oai_gpt-oss_model_card.pdf");

    private readonly PdfDocumentReader _reader = new();

    [Fact]
    public void ReaderType_ShouldReturnPdfReader()
    {
        Assert.Equal("PdfReader", _reader.ReaderType);
    }

    [Fact]
    public void SupportedExtensions_ShouldIncludePdf()
    {
        Assert.Contains(".pdf", _reader.SupportedExtensions);
    }

    [Theory]
    [InlineData("document.pdf", true)]
    [InlineData("TEST.PDF", true)]
    [InlineData("report.pdf", true)]
    [InlineData("document.docx", false)]
    [InlineData("test.txt", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CanRead_ShouldReturnCorrectResult(string? fileName, bool expected)
    {
        Assert.Equal(expected, _reader.CanRead(fileName!));
    }

    // ----- Argument / guard behavior -----

    [Fact]
    public async Task ExtractAsync_WithNullFilePath_ShouldThrowArgumentException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _reader.ExtractAsync((string)null!, null, CancellationToken.None));
        Assert.Contains("File path cannot be null or empty", exception.Message);
    }

    [Fact]
    public async Task ExtractAsync_WithEmptyFilePath_ShouldThrowArgumentException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _reader.ExtractAsync("", null, CancellationToken.None));
        Assert.Contains("File path cannot be null or empty", exception.Message);
    }

    [Fact]
    public async Task ExtractAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
    {
        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _reader.ExtractAsync("non-existent-file.pdf", null, CancellationToken.None));
        Assert.Contains("PDF file not found", exception.Message);
    }

    [Fact]
    public async Task ExtractAsync_WithUnsupportedExtension_ShouldThrowArgumentException()
    {
        var tempFile = Path.GetTempFileName();
        var wrongExtFile = Path.ChangeExtension(tempFile, ".docx");
        File.Move(tempFile, wrongExtFile);
        try
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                _reader.ExtractAsync(wrongExtFile, null, CancellationToken.None));
            Assert.Contains("File format not supported", exception.Message);
        }
        finally
        {
            if (File.Exists(wrongExtFile)) File.Delete(wrongExtFile);
        }
    }

    [Fact]
    public async Task ExtractAsync_WithNullStream_ShouldThrowArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _reader.ExtractAsync((Stream)null!, "test.pdf", null, CancellationToken.None));
        Assert.Equal("stream", exception.ParamName);
    }

    [Fact]
    public async Task ExtractAsync_StreamWithUnsupportedExtension_ShouldThrowArgumentException()
    {
        using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes("test content"));
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _reader.ExtractAsync(memoryStream, "test.docx", null, CancellationToken.None));
        Assert.Contains("File format not supported", exception.Message);
    }

    // ----- Read stage: pages -----

    [Fact]
    public async Task ReadAsync_ShouldReportMultiplePages()
    {
        var result = await _reader.ReadAsync(ModelCardFixture);

        Assert.Equal("PdfReader", result.ReaderType);
        Assert.True(result.Pages.Count > 1, "model card is a multi-page document");
        Assert.Equal(result.Pages.Count, result.DocumentProps["page_count"]);
    }

    // ----- Extract stage: FileFlux managed contract -----

    [Fact]
    public async Task ExtractAsync_ShouldFollowPdfReaderContract()
    {
        var content = await _reader.ExtractAsync(ModelCardFixture);

        Assert.Equal("PdfReader", content.ReaderType);
        Assert.Equal(".pdf", content.File.Extension);
        Assert.Equal("pdf_document", content.Hints["file_type"]);
        Assert.Equal("unpdf_native", content.Hints["conversion_method"]);
        Assert.True((int)content.Hints["page_count"] > 1);      // multi-page
        Assert.True((int)content.Hints["character_count"] > 0);
        Assert.True((int)content.Hints["word_count"] > 0);

        Assert.Equal(content.Text.Trim(), content.Text);
        Assert.NotEmpty(content.Text);
    }

    // ----- Extract stage: embedded image extraction, unblocked by Unpdf 0.15.0's
    // ParseOptions.ExtractResources opt-in -----

    [Fact]
    public async Task ExtractAsync_ModelCardFixture_ExtractsEmbeddedImages()
    {
        // The fixture is a real-world PDF exported with embedded screenshots/diagrams — same
        // shape as the docx/pptx/hwp parity gap this closes. 14 is the fixture's actual resource
        // count (confirmed against Unpdf 0.15.0 directly); a regression that breaks extraction
        // entirely would report 0, not a partial count, so this is not a brittle exact-count test
        // in the sense the class's other tests deliberately avoid — it is the whole point here.
        var content = await _reader.ExtractAsync(ModelCardFixture);

        Assert.Equal(14, content.Images.Count);
        Assert.True((int)content.Hints["image_count"] == 14);
        Assert.True((bool)content.Hints["has_images"]);

        foreach (var image in content.Images)
        {
            Assert.NotEmpty(image.Id);
            Assert.False(string.IsNullOrEmpty(image.MimeType));
            Assert.NotNull(image.Data);
            Assert.True(image.Data.Length > 0);
            Assert.Equal(image.Data.Length, image.OriginalSize);
            Assert.NotNull(image.SourceUrl);
            Assert.StartsWith("embedded:", image.SourceUrl, StringComparison.Ordinal);
        }
    }

    // ----- Extract stage: delegated Unpdf serialization (structural truncation guard) -----

    [Fact]
    public async Task ExtractAsync_ShouldExtractSubstantialTextWithoutTruncation()
    {
        var content = await _reader.ExtractAsync(ModelCardFixture);

        // Structural truncation guard, independent of the document's editorial text: a
        // multi-page text PDF extracted in full yields a large character count. A truncated
        // extraction of a many-page document (or the near-empty symptom class) lands far below
        // this floor, which is itself well under the fixture's actual size.
        Assert.True((int)content.Hints["page_count"] > 1);
        Assert.True((int)content.Hints["character_count"] > 10_000,
            $"expected substantial extracted text, got {content.Hints["character_count"]} chars");

        // A genuine text PDF must not be classified as no-text/blank.
        Assert.False(content.Hints.ContainsKey("extraction_failure_reason"));
    }

    // ----- File vs stream parity (two copy-paste extraction paths) -----

    [Fact]
    public async Task ExtractAsync_FromStream_ShouldMatchFileExtraction()
    {
        var fromFile = await _reader.ExtractAsync(ModelCardFixture);

        await using var stream = File.OpenRead(ModelCardFixture);
        var fromStream = await _reader.ExtractAsync(stream, "oai_gpt-oss_model_card.pdf");

        Assert.Equal(fromFile.Text, fromStream.Text);
        Assert.Equal(fromFile.Hints["page_count"], fromStream.Hints["page_count"]);
        Assert.Equal(fromFile.Hints["conversion_method"], fromStream.Hints["conversion_method"]);
        Assert.Equal(fromFile.Images.Count, fromStream.Images.Count);
    }
}
