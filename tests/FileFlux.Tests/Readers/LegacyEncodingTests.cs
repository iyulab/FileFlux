using System.Text;
using FileFlux.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// A text, Markdown or HTML file saved in a legacy code page keeps its characters. The readers decoded every file as UTF-8
/// with the replacement fallback, so a CP949 / EUC-KR file (older Korean exports and web pages) came out as a run of U+FFFD
/// with no error; the CSV reader alone had the CP949 fallback.
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
