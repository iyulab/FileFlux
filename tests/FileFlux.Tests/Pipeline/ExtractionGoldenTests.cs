using FileFlux.Core;
using FileFlux.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// Pins the refined text of one document per reader path. The parser packages (Undoc, Unhwp, Unpdf) are native code that
/// FileFlux re-pins regularly, and a parser move can change the extracted text with no change in FileFlux — in 0.59.3 it
/// merged the body paragraphs of Word and HWP documents, and every other test stayed green. A difference here is either a
/// defect or an output change consumers need to hear about; record it only once it is described in the CHANGELOG.
/// Golden files live in <c>Fixtures/ExtractionGolden</c>; set <c>FILEFLUX_RECORD_EXTRACTION_GOLDEN=1</c> to rewrite them.
/// </summary>
public sealed class ExtractionGoldenTests : IDisposable
{
    private const string RecordVariable = "FILEFLUX_RECORD_EXTRACTION_GOLDEN";

    /// <summary>Generated, so HWPX is covered without committing a document made in Hangul.</summary>
    private const string GeneratedHwpx = "generated-paragraphs.hwpx";

    /// <summary>
    /// Generated: body text holding every character Markdown reads as syntax, then a table. A parser move that changes how
    /// prose is escaped (FileFlux 0.59.3 stopped escaping it) changes this document's text.
    /// </summary>
    private const string GeneratedMarkdownChars = "generated-markdown-characters.docx";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "FileFluxExtractionGolden", Guid.NewGuid().ToString("N"));

    public ExtractionGoldenTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    public static TheoryData<string> Documents =>
    [
        "demo.docx",
        "sample-doc.docx",
        "legacy-table.doc",
        "sample-slides.pptx",
        "legacy-slides.ppt",
        "list-simple.xlsx",
        "merged-2row-header.xlsx",
        "legacy-korean.xls",
        "two-column-narrow-gutter.pdf",
        "form-xobject-text.pdf",
        GeneratedHwpx,
        GeneratedMarkdownChars,
    ];

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task RefinedText_MatchesTheGolden(string document)
    {
        var path = document switch
        {
            GeneratedHwpx => WriteHwpx(),
            GeneratedMarkdownChars => WriteMarkdownCharsDocx(),
            _ => Path.Combine(AppContext.BaseDirectory, "Fixtures", document),
        };
        var services = new ServiceCollection();
        services.AddFileFlux();
        await using var provider = services.BuildServiceProvider();
        await using var processor = provider.GetRequiredService<IDocumentProcessorFactory>().Create(path);

        await processor.RefineAsync(cancellationToken: TestContext.Current.CancellationToken);
        var actual = Normalize(processor.Result.Refined!.Text);

        var golden = document + ".md";
        if (Environment.GetEnvironmentVariable(RecordVariable) == "1")
        {
            var sourceDir = Path.Combine(FindRepoRoot(), "tests", "FileFlux.Tests", "Fixtures", "ExtractionGolden");
            Directory.CreateDirectory(sourceDir);
            await File.WriteAllTextAsync(Path.Combine(sourceDir, golden), actual, TestContext.Current.CancellationToken);
            return;
        }

        var expected = Normalize(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "ExtractionGolden", golden), TestContext.Current.CancellationToken));
        Assert.True(expected == actual,
            $"The refined text of {document} changed. If the change is intended (a parser move, a refine rule), describe it " +
            $"in the CHANGELOG by format and re-record with {RecordVariable}=1.\n{FirstDifference(expected, actual)}");
    }

    private string WriteHwpx()
    {
        var path = Path.Combine(_dir, GeneratedHwpx);
        HwpxBuilder.Write(path,
        [
            "First body paragraph of a generated document.",
            "Second body paragraph of a generated document.",
            "세 번째 본문 문단입니다.",
        ]);
        return path;
    }

    private string WriteMarkdownCharsDocx()
    {
        var path = Path.Combine(_dir, GeneratedMarkdownChars);
        DocxBuilder.Write(path,
        [
            "* A sentence that starts with an asterisk.",
            "1. A sentence that starts with a number and a dot.",
            "# A sentence that starts with a hash.",
            "> A sentence that starts with a greater-than sign.",
            "snake_case_name, a*b*c, __init__, [brackets](parentheses) and <angle> brackets.",
            @"A path C:\dir\file.txt and a share \\server\share\doc.docx.",
            "Backticks `around words` and a price | with | pipes.",
        ],
        [["Name_with_underscores", "Value*"], ["a_b", "[1]"]]);
        return path;
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";

    private static string FirstDifference(string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        for (var i = 0; i < Math.Max(e.Length, a.Length); i++)
        {
            var el = i < e.Length ? e[i] : "<end>";
            var al = i < a.Length ? a[i] : "<end>";
            if (!string.Equals(el, al, StringComparison.Ordinal))
                return $"line {i + 1}:\n  golden: {el}\n  actual: {al}";
        }

        return "no line differs";
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FileFlux.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("FileFlux.slnx not found above the test output.");
    }
}
