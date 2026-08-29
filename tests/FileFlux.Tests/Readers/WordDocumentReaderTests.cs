using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;
using System.Text;

namespace FileFlux.Tests.Readers;

/// <summary>
/// WordDocumentReader unit tests — modern .docx (OOXML) extraction via Undoc (Rust FFI).
///
/// Fixture: Fixtures/sample-doc.docx (python-docx-generated, deterministic — see scratchpad
/// gen_docx_pptx_fixtures.py): H1 heading, an intro paragraph, three flowed list items, a
/// 2-column pipe table (구분|배점, 3 data rows), and a unique tail marker.
///
/// Replaces the previous tests that pointed at a hardcoded absolute path outside the repo
/// (always self-skipped) and the vacuous always-pass structure placeholder. This is now the
/// first test exercising the Undoc native .docx path for real (CI ubuntu-latest resolves
/// runtimes/linux-x64/native/libundoc.so). Assertions split by ownership; no exact
/// character-count assertion (Undoc formatting shifts on bump).
/// </summary>
public class WordDocumentReaderTests
{
    private static readonly string SampleDocFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-doc.docx");

    private readonly WordDocumentReader _reader = new();

    [Fact]
    public void ReaderType_ShouldReturnWordReader()
    {
        Assert.Equal("WordReader", _reader.ReaderType);
    }

    [Fact]
    public void SupportedExtensions_ShouldIncludeDocx()
    {
        Assert.Contains(".docx", _reader.SupportedExtensions);
    }

    [Theory]
    [InlineData("test.docx", true)]
    [InlineData("TEST.DOCX", true)]
    [InlineData("document.docx", true)]
    [InlineData("test.doc", false)]
    [InlineData("test.pdf", false)]
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
            _reader.ExtractAsync("non-existent-file.docx", null, CancellationToken.None));
        Assert.Contains("Word document not found", exception.Message);
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
            _reader.ExtractAsync((Stream)null!, "test.docx", null, CancellationToken.None));
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
    public async Task ExtractAsync_InvalidDocxPayload_ShouldThrowDocumentProcessingException()
    {
        using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes("this is not an OOXML package"));
        await Assert.ThrowsAsync<DocumentProcessingException>(() =>
            _reader.ExtractAsync(memoryStream, "broken.docx", null, CancellationToken.None));
    }

    // ----- Extract stage: FileFlux managed contract -----

    [Fact]
    public async Task ExtractAsync_ShouldFollowWordReaderContract()
    {
        var content = await _reader.ExtractAsync(SampleDocFixture, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("WordReader", content.ReaderType);
        Assert.Equal(".docx", content.File.Extension);
        Assert.Equal("word_document", content.Hints["file_type"]);
        Assert.Equal("undoc_native", content.Hints["conversion_method"]);
        Assert.True((int)content.Hints["character_count"] >= 0);
        Assert.True((int)content.Hints["word_count"] >= 0);
        Assert.True((int)content.Hints["paragraph_count"] >= 0);

        Assert.Equal(content.Text.Trim(), content.Text);
        Assert.NotEmpty(content.Text);
    }

    // ----- Extract stage: delegated Undoc serialization (content + structure) -----

    [Fact]
    public async Task ExtractAsync_ShouldPreserveHeadingParagraphsAndTable()
    {
        var content = await _reader.ExtractAsync(SampleDocFixture, cancellationToken: TestContext.Current.CancellationToken);

        // Heading and body paragraphs.
        Assert.Contains("유지보수 계약 개요", content.Text);
        Assert.Contains("본 문서는 2026년도 유지보수 수행사 선정 기준을 요약한다.", content.Text);
        Assert.Contains("기술역량 평가", content.Text);
        Assert.Contains("사후지원 체계 확인", content.Text);

        // Table rows preserved, including the LAST row and the tail marker after the table —
        // a truncated extraction (the near-empty symptom class) loses the tail.
        Assert.Contains("| 구분 | 배점 |", content.Text);
        Assert.Contains("| 지원 | 30 |", content.Text);      // last table row
        Assert.Contains("문서 끝 표식 ZZ-마감블록", content.Text); // unique tail marker

        // Structural hints inferred from the markdown.
        Assert.Equal(true, content.Hints["has_headers"]);
        Assert.Equal(true, content.Hints["has_tables"]);
    }

    // ----- File vs stream parity (two copy-paste extraction paths) -----

    [Fact]
    public async Task ExtractAsync_FromStream_ShouldMatchFileExtraction()
    {
        var fromFile = await _reader.ExtractAsync(SampleDocFixture, cancellationToken: TestContext.Current.CancellationToken);

        await using var stream = File.OpenRead(SampleDocFixture);
        var fromStream = await _reader.ExtractAsync(stream, "sample-doc.docx", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(fromFile.Text, fromStream.Text);
        Assert.Equal(fromFile.Hints["conversion_method"], fromStream.Hints["conversion_method"]);
    }
}
