using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;
using System.Text;

namespace FileFlux.Tests.Readers;

/// <summary>
/// PowerPointDocumentReader unit tests — modern .pptx (OOXML) extraction via Undoc (Rust FFI).
///
/// Fixture: Fixtures/sample-slides.pptx (python-pptx-generated, deterministic — see scratchpad
/// gen_docx_pptx_fixtures.py): 3 slides (title + body each), with a unique token on the last
/// slide for the slide-loss guard.
///
/// Replaces the previous tests that pointed at a hardcoded absolute path outside the repo
/// (always self-skipped) and four vacuous always-pass placeholders. First test exercising the
/// Undoc native .pptx path for real (CI ubuntu-latest resolves runtimes/linux-x64/native). No exact
/// character-count assertion (Undoc formatting shifts on bump).
/// </summary>
public class PowerPointDocumentReaderTests
{
    private static readonly string SampleSlidesFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-slides.pptx");

    private readonly PowerPointDocumentReader _reader = new();

    [Fact]
    public void ReaderType_ShouldReturnPowerPointReader()
    {
        Assert.Equal("PowerPointReader", _reader.ReaderType);
    }

    [Fact]
    public void SupportedExtensions_ShouldIncludePptx()
    {
        Assert.Contains(".pptx", _reader.SupportedExtensions);
    }

    [Theory]
    [InlineData("presentation.pptx", true)]
    [InlineData("TEST.PPTX", true)]
    [InlineData("slides.pptx", true)]
    [InlineData("presentation.ppt", false)]
    [InlineData("test.pdf", false)]
    [InlineData("test.docx", false)]
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
            _reader.ExtractAsync("non-existent-file.pptx", null, CancellationToken.None));
        Assert.Contains("PowerPoint document not found", exception.Message);
    }

    [Fact]
    public async Task ExtractAsync_WithUnsupportedExtension_ShouldThrowArgumentException()
    {
        var tempFile = Path.GetTempFileName();
        var wrongExtFile = Path.ChangeExtension(tempFile, ".pdf");
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
            _reader.ExtractAsync((Stream)null!, "test.pptx", null, CancellationToken.None));
        Assert.Equal("stream", exception.ParamName);
    }

    [Fact]
    public async Task ExtractAsync_StreamWithUnsupportedExtension_ShouldThrowArgumentException()
    {
        using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes("test content"));
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _reader.ExtractAsync(memoryStream, "test.pdf", null, CancellationToken.None));
        Assert.Contains("File format not supported", exception.Message);
    }

    [Fact]
    public async Task ExtractAsync_InvalidPptxPayload_ShouldThrowDocumentProcessingException()
    {
        using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes("this is not an OOXML package"));
        await Assert.ThrowsAsync<DocumentProcessingException>(() =>
            _reader.ExtractAsync(memoryStream, "broken.pptx", null, CancellationToken.None));
    }

    // ----- Read stage: slides → pages -----

    [Fact]
    public async Task ReadAsync_ShouldReportEachSlideAsPage()
    {
        var result = await _reader.ReadAsync(SampleSlidesFixture, TestContext.Current.CancellationToken);

        Assert.Equal("PowerPointReader", result.ReaderType);
        Assert.Equal(3, result.Pages.Count);
        Assert.Equal(3, result.DocumentProps["slide_count"]);
        Assert.All(result.Pages, p => Assert.Equal("powerpoint_slide", p.Props["file_type"]));
    }

    // ----- Extract stage: FileFlux managed contract -----

    [Fact]
    public async Task ExtractAsync_ShouldFollowPowerPointReaderContract()
    {
        var content = await _reader.ExtractAsync(SampleSlidesFixture, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("PowerPointReader", content.ReaderType);
        Assert.Equal(".pptx", content.File.Extension);
        Assert.Equal("powerpoint_presentation", content.Hints["file_type"]);
        Assert.Equal("undoc_native", content.Hints["conversion_method"]);
        Assert.Equal(3, content.Hints["slide_count"]);
        Assert.True((int)content.Hints["character_count"] >= 0);

        Assert.Equal(content.Text.Trim(), content.Text);
        Assert.NotEmpty(content.Text);
    }

    // ----- Extract stage: delegated Undoc serialization (slide-loss guard) -----

    [Fact]
    public async Task ExtractAsync_ShouldPreserveEverySlide()
    {
        var content = await _reader.ExtractAsync(SampleSlidesFixture, cancellationToken: TestContext.Current.CancellationToken);

        // Every slide marker is present — the reader emits "## Slide N" per section.
        Assert.Contains("## Slide 1", content.Text);
        Assert.Contains("## Slide 2", content.Text);
        Assert.Contains("## Slide 3", content.Text);

        // First slide content, and the LAST slide's unique token — a truncated extraction
        // that dropped trailing slides fails the last assertion.
        Assert.Contains("수행사 선정 발표", content.Text);            // slide 1 title
        Assert.Contains("선정업체 에이비씨소프트", content.Text);     // slide 3 body
        Assert.Contains("SLIDE3-마감표식", content.Text);            // last slide unique token
    }

    // ----- File vs stream parity (two copy-paste extraction paths) -----

    [Fact]
    public async Task ExtractAsync_FromStream_ShouldMatchFileExtraction()
    {
        var fromFile = await _reader.ExtractAsync(SampleSlidesFixture, cancellationToken: TestContext.Current.CancellationToken);

        await using var stream = File.OpenRead(SampleSlidesFixture);
        var fromStream = await _reader.ExtractAsync(stream, "sample-slides.pptx", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(fromFile.Text, fromStream.Text);
        Assert.Equal(fromFile.Hints["slide_count"], fromStream.Hints["slide_count"]);
    }
}
