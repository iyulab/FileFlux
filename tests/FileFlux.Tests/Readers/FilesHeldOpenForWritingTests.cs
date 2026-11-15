using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// A document is most often asked about while the program that edits it still has it open. On Windows an
/// editor holds its file open for writing (shared with other readers and writers), and a reader that opens the
/// same path asking to deny writers is refused with «being used by another process». These facts hold the
/// file the way an editor does and read it through every file-path reader.
/// </summary>
public class FilesHeldOpenForWritingTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"fileflux-held-open-{Guid.NewGuid():N}");

    public FilesHeldOpenForWritingTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    public static TheoryData<string> Formats { get; } = new()
    {
        "txt", "md", "csv", "html", "docx", "xlsx", "pptx", "pdf", "doc", "xls", "ppt",
    };

    private string Prepare(string format)
    {
        var path = Path.Combine(_tempDir, "held-open." + format);
        switch (format)
        {
            case "txt":
                File.WriteAllText(path, "Quarterly plan. The budget is approved.");
                break;
            case "md":
                File.WriteAllText(path, "# Quarterly plan\n\nThe budget is approved.\n");
                break;
            case "csv":
                File.WriteAllText(path, "item,amount\nbudget,100\n");
                break;
            case "html":
                File.WriteAllText(path, "<html><body><h1>Quarterly plan</h1><p>The budget is approved.</p></body></html>");
                break;
            default:
                var fixture = format switch
                {
                    "docx" => "sample-doc.docx",
                    "xlsx" => "list-simple.xlsx",
                    "pptx" => "sample-slides.pptx",
                    "pdf" => "form-xobject-text.pdf",
                    "doc" => "legacy-table.doc",
                    "xls" => "legacy-biff5.xls",
                    "ppt" => "legacy-slides.ppt",
                    _ => throw new ArgumentOutOfRangeException(nameof(format)),
                };
                File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), path);
                break;
        }

        return path;
    }

    private static IDocumentReader ReaderFor(string format) => format switch
    {
        "txt" => new TextDocumentReader(),
        "md" => new MarkdownDocumentReader(),
        "csv" => new CsvDocumentReader(),
        "html" => new HtmlDocumentReader(),
        "docx" or "doc" => new WordDocumentReader(),
        "xlsx" or "xls" => new ExcelDocumentReader(),
        "pptx" or "ppt" => new PowerPointDocumentReader(),
        "pdf" => new PdfDocumentReader(),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task EveryReader_ReadsAFile_AnEditorHoldsOpenForWriting(string format)
    {
        var path = Prepare(format);
        var expected = (await ReaderFor(format).ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken)).Text;
        Assert.False(string.IsNullOrWhiteSpace(expected), "the control read of the closed file has text");

        using var editor = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        var content = await ReaderFor(format).ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(expected, content.Text);
    }
}
