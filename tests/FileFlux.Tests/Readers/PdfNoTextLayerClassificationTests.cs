using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Empty-document classification teeth for PdfDocumentReader (Unpdf 0.9.0
/// page introspection). Fixtures:
/// - Fixtures/image-only.pdf — one page drawing only an image XObject
///   (TextOpCount=0, ImageOpCount=1) → "no_text_layer" (scanned, OCR required)
/// - Fixtures/blank-page.pdf — one page with an empty content stream
///   (both counts 0) → "blank_page"
/// Both parse fine and yield empty text; before 0.13.0 this was a silently
/// empty Completed result, and 0.13.0 could not distinguish the two cases
/// (consumer field report 2026-07-22 / unpdf introspection follow-up AC4).
/// </summary>
public class PdfNoTextLayerClassificationTests
{
    private static readonly string ImageOnlyPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "image-only.pdf");

    private static readonly string BlankPagePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "blank-page.pdf");

    // One page whose content stream only paints a form XObject ("q /Fm1 Do Q"); the form draws the text.
    // The PDF parser does not read text inside forms and counts the form invocation as an image operation.
    private static readonly string FormXObjectPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "form-xobject-text.pdf");

    // One page that paints a form XObject whose content is a page-sized image (a scanner's wrapping).
    private static readonly string FormWrappedImagePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "form-xobject-image.pdf");

    private static readonly string TextPdfPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "oai_gpt-oss_model_card.pdf");

    private readonly PdfDocumentReader _reader = new();

    [Fact]
    public async Task ExtractAsync_ImageOnlyPdf_ShouldClassifyNoTextLayer()
    {
        var content = await _reader.ExtractAsync(ImageOnlyPath, cancellationToken: TestContext.Current.CancellationToken);

        // No exception: the document is valid — just has no readable text layer
        Assert.Equal(string.Empty, content.Text);
        Assert.Equal("no_text_layer", content.Hints["extraction_failure_reason"]);
        Assert.Contains(content.Warnings, w => w.Contains("image-only/scanned"));
    }

    [Fact]
    public async Task ExtractAsync_TextDrawnThroughAFormXObject_IsNotReportedAsAScan()
    {
        var content = await _reader.ExtractAsync(FormXObjectPath, cancellationToken: TestContext.Current.CancellationToken);

        // When the PDF parser reads form XObjects, this document yields its text and
        // this test changes to assert it. Until then the text is lost; what must not happen is the
        // "scanned, needs OCR" verdict, which a consumer shows its user as the cause.
        Assert.Equal("text_not_extracted", content.Hints["extraction_failure_reason"]);
        Assert.Contains(content.Warnings, w => w.Contains("cannot tell which"));
        Assert.DoesNotContain(content.Warnings, w => w.Contains("image-only/scanned"));
    }

    [Fact]
    public async Task ExtractAsync_AScanImageWrappedInAFormXObject_IsNotReportedAsNotAScan()
    {
        // A scanner can wrap its page image in a form XObject; the parser then extracts no image either. The
        // warning must not tell the reader the document is not a scan.
        var content = await _reader.ExtractAsync(FormWrappedImagePath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("text_not_extracted", content.Hints["extraction_failure_reason"]);
        Assert.DoesNotContain(content.Warnings, w => w.Contains("not a scanned document"));
        Assert.Contains(content.Warnings, w => w.Contains("OCR needed"));
    }

    [Fact]
    public async Task ExtractAsync_BlankPagePdf_ShouldClassifyBlankPage()
    {
        var content = await _reader.ExtractAsync(BlankPagePath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, content.Text);
        Assert.Equal("blank_page", content.Hints["extraction_failure_reason"]);
        Assert.Contains(content.Warnings, w => w.Contains("blank"));
    }

    [Fact]
    public async Task ExtractAsync_ImageOnlyPdf_ShouldStillReportPageCount()
    {
        var content = await _reader.ExtractAsync(ImageOnlyPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, content.Hints["page_count"]);
    }

    [Fact]
    public async Task ExtractAsync_TextPdf_ShouldNotCarryFailureReason()
    {
        // Reference behavior: a PDF with a real text layer must not be tagged.
        // Uses the committed real-world PDF; the previous probe looked in a
        // "Resources" directory that does not exist and early-returned, so this
        // reference case passed without ever running.
        var content = await _reader.ExtractAsync(TextPdfPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(string.Empty, content.Text);
        Assert.False(content.Hints.ContainsKey("extraction_failure_reason"));
    }
}
