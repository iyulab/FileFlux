using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// No-text-layer classification teeth for PdfDocumentReader.
/// Fixture: Fixtures/image-only.pdf — a valid single-page PDF whose page draws
/// only an image XObject (no text operators). Unpdf parses it fine and returns
/// empty text, which previously produced a silently-empty Completed result.
/// The reader must now surface the classification as structured metadata
/// (AIMS field report 2026-07-22: scanned 확약서류 PDFs were indistinguishable
/// from parser defects).
/// </summary>
public class PdfNoTextLayerClassificationTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "image-only.pdf");

    private readonly PdfDocumentReader _reader = new();

    [Fact]
    public async Task ExtractAsync_ImageOnlyPdf_ShouldClassifyNoTextLayer()
    {
        var content = await _reader.ExtractAsync(FixturePath);

        // No exception: the document is valid — just has no text layer
        Assert.Equal(string.Empty, content.Text);
        Assert.Equal("no_text_layer", content.Hints["extraction_failure_reason"]);
        Assert.Contains(content.Warnings, w => w.Contains("image-only/scanned"));
    }

    [Fact]
    public async Task ExtractAsync_ImageOnlyPdf_ShouldStillReportPageCount()
    {
        var content = await _reader.ExtractAsync(FixturePath);

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
