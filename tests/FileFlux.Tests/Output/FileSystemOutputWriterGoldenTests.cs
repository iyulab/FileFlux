using System.Text.Json.Nodes;
using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure.Output;

namespace FileFlux.Tests.Output;

/// <summary>
/// Pins what <see cref="FileSystemOutputWriter"/> writes to disk for every format — the file set, and each JSON file's
/// content compared as JSON (not text). Golden files live in <c>Fixtures/OutputWriter</c>; set
/// <c>FILEFLUX_RECORD_OUTPUT_GOLDEN=1</c> to rewrite them from the current writer.
/// </summary>
public sealed class FileSystemOutputWriterGoldenTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fileflux-output-golden-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData("md")]
    [InlineData("json")]
    [InlineData("jsonl")]
    public async Task WriteChunking_MatchesTheGolden(string format)
    {
        var dir = Path.Combine(_root, "chunk-" + format);

        await new FileSystemOutputWriter().WriteChunkingAsync(
            Chunking(), dir, new OutputOptions { Format = format }, TestContext.Current.CancellationToken);

        AssertMatchesGolden(dir, "chunk-" + format);
    }

    [Theory]
    [InlineData("md")]
    [InlineData("json")]
    public async Task WriteExtraction_MatchesTheGolden(string format)
    {
        var dir = Path.Combine(_root, "extract-" + format);

        await new FileSystemOutputWriter().WriteExtractionAsync(
            Extraction(), dir, new OutputOptions { Format = format }, TestContext.Current.CancellationToken);

        AssertMatchesGolden(dir, "extract-" + format);
    }

    private static void AssertMatchesGolden(string actualDir, string goldenName)
    {
        var goldenDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "OutputWriter", goldenName);
        var actualFiles = Directory.GetFiles(actualDir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();

        if (Environment.GetEnvironmentVariable("FILEFLUX_RECORD_OUTPUT_GOLDEN") == "1")
        {
            var sourceDir = Path.Combine(FindRepoRoot(), "tests", "FileFlux.Tests", "Fixtures", "OutputWriter", goldenName);
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }

            Directory.CreateDirectory(sourceDir);
            foreach (var file in actualFiles)
            {
                File.Copy(Path.Combine(actualDir, file!), Path.Combine(sourceDir, file!));
            }

            return;
        }

        var goldenFiles = Directory.GetFiles(goldenDir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(goldenFiles, actualFiles);

        foreach (var file in actualFiles)
        {
            var bytes = File.ReadAllBytes(Path.Combine(actualDir, file!));
            Assert.False(bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]), $"{file} starts with a byte order mark");
            var actual = File.ReadAllText(Path.Combine(actualDir, file!));
            var expected = File.ReadAllText(Path.Combine(goldenDir, file!));
            if (file!.EndsWith(".json", StringComparison.Ordinal))
            {
                AssertJsonEqual(expected, actual, file);
            }
            else if (file.EndsWith(".jsonl", StringComparison.Ordinal))
            {
                var expectedLines = Lines(expected);
                var actualLines = Lines(actual);
                Assert.Equal(expectedLines.Count, actualLines.Count);
                for (var i = 0; i < expectedLines.Count; i++)
                {
                    AssertJsonEqual(expectedLines[i], actualLines[i], $"{file} line {i + 1}");
                }
            }
            else
            {
                Assert.Equal(Normalize(expected), Normalize(actual));
            }
        }
    }

    private static void AssertJsonEqual(string expected, string actual, string what)
    {
        var e = JsonNode.Parse(expected);
        var a = JsonNode.Parse(actual);
        Assert.True(JsonNode.DeepEquals(e, a), $"{what} differs.\nexpected: {e?.ToJsonString()}\nactual:   {a?.ToJsonString()}");
    }

    private static List<string> Lines(string text) =>
        Normalize(text).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FileFlux.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("FileFlux.slnx not found above the test output.");
    }

    private static readonly DateTime Processed = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private static DocumentMetadata Metadata()
    {
        var metadata = new DocumentMetadata
        {
            FileName = "report.pdf",
            FileType = "PDF",
            FileSize = 12345,
            Title = "Quarterly \"Report\"",
            Author = "A. Writer",
            CreatedAt = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc),
            ProcessedAt = Processed,
            Language = "ko",
            LanguageConfidence = 0.875,
            PageCount = 3,
            WordCount = 42
        };
        metadata.CustomProperties["sheetName"] = "Q1";
        metadata.CustomProperties["sectionCount"] = 4;
        metadata.CustomProperties["undecodableStreams"] = new List<string> { "s1", "s2" };
        return metadata;
    }

    private static ExtractionResult Extraction() => new()
    {
        ParsedContent = new RefinedContent { Metadata = Metadata() },
        ProcessedText = "첫 문단입니다.\n\nSecond paragraph with three words.",
        Images =
        [
            new ProcessedImage { FileName = "img-1.png", Width = 640, Height = 480, FileSize = 2048, AIDescription = "A bar chart" },
            new ProcessedImage { FileName = "img-2.png", Width = 10, Height = 10, FileSize = 64, AIError = "too small" }
        ],
        SkippedImageCount = 1,
        AIProvider = "test-provider"
    };

    private static ChunkingResult Chunking() => new()
    {
        Extraction = Extraction(),
        Options = new ChunkingOptions { Strategy = "Paragraph", MaxChunkSize = 512, OverlapSize = 32 },
        Chunks =
        [
            new DocumentChunk
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                CreatedAt = Processed,
                Content = "첫 문단입니다.",
                ChunkIndex = 0,
                Tokens = 7,
                Quality = 0.9,
                Density = 0.4,
                Importance = 0.6,
                Strategy = "Paragraph",
                Metadata = Metadata(),
                Location = new SourceLocation { StartChar = 0, EndChar = 8, StartPage = 1, EndPage = 1, Section = "Intro", HeadingPath = ["Report", "Intro"] },
                Props = new Dictionary<string, object>
                {
                    [ChunkPropsKeys.EnrichedSummary] = "Opening paragraph",
                    [ChunkPropsKeys.EnrichedKeywords] = new List<string> { "report", "opening" },
                    [ChunkPropsKeys.DocumentTopic] = "Finance",
                    [ChunkPropsKeys.HierarchyPath] = "Report > Intro",
                    [ChunkPropsKeys.EnrichedContextualText] = "Report: opening paragraph",
                    ["custom.flag"] = true,
                    ["custom.count"] = 3,
                    ["custom.when"] = Processed,
                    ["custom.kind"] = DayOfWeek.Friday,
                    ["custom.tags"] = new[] { "a", "b" },
                    ["custom.nested"] = new Dictionary<string, object> { ["inner"] = 1.5, ["label"] = "x" },
                    ["custom.record"] = new SampleRecord("r", 2),
                }
            },
            new DocumentChunk
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                CreatedAt = Processed,
                Content = "Second paragraph with three words.",
                ChunkIndex = 1,
                Quality = 0,
                Density = 0,
                Importance = 0,
                Metadata = Metadata(),
                Location = new SourceLocation { StartChar = 10, EndChar = 44 },
                Props = new Dictionary<string, object>
                {
                    [ChunkPropsKeys.DocumentKeywords] = new List<object> { "second", "words" },
                    [ChunkPropsKeys.EnrichmentSkipped] = true,
                }
            }
        ]
    };

    public sealed record SampleRecord(string Name, int Value);
}
