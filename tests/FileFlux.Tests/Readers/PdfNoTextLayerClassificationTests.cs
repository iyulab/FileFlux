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
/// (AIMS field report 2026-07-22 / unpdf introspection issue follow-up AC4).
/// </summary>
public class PdfNoTextLayerClassificationTests
{
    private static readonly string ImageOnlyPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "image-only.pdf");

    private static readonly string BlankPagePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "blank-page.pdf");

    private readonly PdfDocumentReader _reader = new();

    [Fact]
    public async Task ExtractAsync_ImageOnlyPdf_ShouldClassifyNoTextLayer()
    {
        var content = await _reader.ExtractAsync(ImageOnlyPath);

        // No exception: the document is valid — just has no readable text layer
        Assert.Equal(string.Empty, content.Text);
        Assert.Equal("no_text_layer", content.Hints["extraction_failure_reason"]);
        Assert.Contains(content.Warnings, w => w.Contains("image-only/scanned"));
    }

    [Fact]
    public async Task ExtractAsync_BlankPagePdf_ShouldClassifyBlankPage()
    {
        var content = await _reader.ExtractAsync(BlankPagePath);

        Assert.Equal(string.Empty, content.Text);
        Assert.Equal("blank_page", content.Hints["extraction_failure_reason"]);
        Assert.Contains(content.Warnings, w => w.Contains("blank"));
    }

    [Fact]
    public async Task ExtractAsync_ImageOnlyPdf_ShouldStillReportPageCount()
    {
        var content = await _reader.ExtractAsync(ImageOnlyPath);

        Assert.Equal(1, content.Hints["page_count"]);
    }

    [Fact]
    public async Task ExtractAsync_TextPdf_ShouldNotCarryFailureReason()
    {
        // Reference behavior: a PDF with a real text layer must not be tagged
        var textPdfPath = FindAnyTextPdf();
        if (textPdfPath is null)
            return; // no text-bearing sample available in this environment

        var content = await _reader.ExtractAsync(textPdfPath);

        Assert.False(content.Hints.ContainsKey("extraction_failure_reason"));
    }

    private static string? FindAnyTextPdf()
    {
        // Reuse any repository sample PDF with actual text if present
        var candidates = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Resources"))
            ? Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Resources"), "*.pdf")
            : [];
        return candidates.FirstOrDefault();
    }
}
