using System.Text;
using FileFlux.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// A text, Markdown or HTML file saved in a legacy code page keeps its characters. The readers decoded every file as UTF-8
/// with the replacement fallback, so a CP949 / EUC-KR file (older Korean exports and web pages) came out as a run of U+FFFD
/// with no error; the CSV reader alone had the CP949 fallback. UTF-16 saved without a byte order mark is recognised by its
/// NULs (before, it came out as NUL-interleaved text, because NUL is valid UTF-8).
/// </summary>
public sealed class LegacyEncodingTests : IDisposable
{
    private const string Hello = "안녕하세요";
    private const string Document = "문서처리";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fileflux-encoding-" + Guid.NewGuid().ToString("N"));

    static LegacyEncodingTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public LegacyEncodingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp cleanup is best effort */ }
    }

    private async Task<string> ExtractAsync(string fileName, byte[] bytes)
    {
        var path = Path.Combine(_dir, fileName);
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        var services = new ServiceCollection();
        services.AddFileFlux();
        await using var provider = services.BuildServiceProvider();
        await using var processor = provider.GetRequiredService<IDocumentProcessorFactory>().Create(path);

        await processor.ExtractAsync(cancellationToken: TestContext.Current.CancellationToken);

        return processor.Result.Raw!.Text;
    }

    private static byte[] Cp949(string text) => Encoding.GetEncoding(949).GetBytes(text);

    private const string MixedUtf16Text = "Quarterly report\n\n안녕하세요 문서처리입니다. 분기 매출은 12억 원이었습니다.\n\nSecond paragraph, 2026-11-13.";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlainText_Utf16WithoutBom_IsReadAsText(bool bigEndian)
    {
        var bytes = new UnicodeEncoding(bigEndian, byteOrderMark: false).GetBytes(MixedUtf16Text);

        var text = await ExtractAsync("utf16.txt", bytes);

        Assert.Contains("Quarterly report", text);
        Assert.Contains("안녕하세요 문서처리입니다.", text);
        Assert.DoesNotContain('\0', text);
        Assert.DoesNotContain('�', text);
    }

    [Fact]
    public async Task Markdown_Utf16WithoutBom_IsReadAsText()
    {
        var bytes = new UnicodeEncoding(bigEndian: false, byteOrderMark: false).GetBytes("# 제목\n\nPlain ASCII line, 12345.\n");

        var text = await ExtractAsync("utf16.md", bytes);

        Assert.Contains("제목", text);
        Assert.Contains("Plain ASCII line, 12345.", text);
        Assert.DoesNotContain('\0', text);
    }

    [Fact]
    public async Task PlainText_Utf16WithoutBom_WithAHangulSyllableWhoseLowByteIsZero_IsStillReadAsUtf16()
    {
        // U+AC00 is 00 AC / AC 00: a NUL on the other parity than the ASCII characters' NULs (twice here, against dozens of ASCII ones).
        var bytes = new UnicodeEncoding(bigEndian: false, byteOrderMark: false).GetBytes("Report no. 2026-11-13: 가 문서 abc 가나다 text line\n");

        var text = await ExtractAsync("utf16-ac00.txt", bytes);

        Assert.Contains("가나다", text);
        Assert.DoesNotContain('\0', text);
    }

    [Fact]
    public async Task PlainText_Utf16WithBom_StillWinsOverTheNulPattern()
    {
        var bytes = new UnicodeEncoding(bigEndian: false, byteOrderMark: true).GetPreamble()
            .Concat(new UnicodeEncoding(bigEndian: false, byteOrderMark: false).GetBytes("with a mark\n")).ToArray();

        var text = await ExtractAsync("utf16-bom.txt", bytes);

        Assert.Contains("with a mark", text);
        Assert.DoesNotContain('\0', text);
    }

    [Fact]
    public async Task PlainText_Utf8WithNoNulBytes_IsUnchangedByTheUtf16Check()
    {
        var text = await ExtractAsync("utf8.txt", Encoding.UTF8.GetBytes($"{Hello} {Document}\nplain ASCII"));

        Assert.Contains($"{Hello} {Document}", text);
    }

    [Fact]
    public async Task PlainText_WithOneStrayNulByte_IsNotTakenForUtf16()
    {
        // Fewer than two NULs say nothing about UTF-16: the file stays UTF-8.
        var bytes = Encoding.UTF8.GetBytes("one stray NUL here:\0 and the rest is ordinary UTF-8 text.\n");

        var text = await ExtractAsync("stray-nul.txt", bytes);

        Assert.Contains("and the rest is ordinary UTF-8 text.", text);
    }

    [Fact]
    public async Task PlainText_SavedAsCp949_KeepsItsKorean()
    {
        var text = await ExtractAsync("legacy.txt", Cp949($"{Hello} {Document}\n"));

        Assert.Contains($"{Hello} {Document}", text);
        Assert.DoesNotContain('�', text);
    }

    [Fact]
    public async Task Markdown_SavedAsCp949_KeepsItsKorean()
    {
        var text = await ExtractAsync("legacy.md", Cp949($"# {Hello}\n\n{Document}\n"));

        Assert.Contains(Hello, text);
        Assert.Contains(Document, text);
        Assert.DoesNotContain('�', text);
    }

    [Theory]
    [InlineData("<meta charset=\"euc-kr\">")]
    [InlineData("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=ks_c_5601-1987\">")]
    [InlineData("")]
    public async Task Html_SavedAsCp949_KeepsItsKorean(string meta)
    {
        var html = $"<html><head>{meta}</head><body><p>{Hello}</p><p>{Document}</p></body></html>";

        var text = await ExtractAsync("legacy.html", Cp949(html));

        Assert.Contains(Hello, text);
        Assert.Contains(Document, text);
        Assert.DoesNotContain('�', text);
    }

    [Fact]
    public async Task Html_DeclaringAWesternCharset_IsReadWithIt()
    {
        // 0xE9 is not valid UTF-8 and means e-acute in windows-1252; the declaration wins over the Korean fallback.
        var html = "<html><head><meta charset=\"windows-1252\"></head><body><p>café crème</p></body></html>";

        var text = await ExtractAsync("western.html", Encoding.GetEncoding(1252).GetBytes(html));

        Assert.Contains("café crème", text);
    }

    [Fact]
    public async Task PlainText_AsUtf16WithAByteOrderMark_StillReads()
    {
        var bytes = new UnicodeEncoding(bigEndian: false, byteOrderMark: true).GetPreamble()
            .Concat(Encoding.Unicode.GetBytes($"{Hello} {Document}\n")).ToArray();

        var text = await ExtractAsync("utf16.txt", bytes);

        Assert.Contains($"{Hello} {Document}", text);
    }

    [Fact]
    public async Task PlainText_AsUtf8_IsUnchanged()
    {
        var text = await ExtractAsync("utf8.txt", new UTF8Encoding(false).GetBytes($"{Hello} {Document}\n"));

        Assert.Contains($"{Hello} {Document}", text);
    }
}
