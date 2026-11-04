using System.Text.Json.Nodes;
using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Output;
using Microsoft.Extensions.DependencyInjection;

namespace FileFlux.Tests.Output;

/// <summary>
/// An extraction of a non-empty file that yielded no text used to read as a success: the CLI printed "Extracted successfully" with
/// "Total characters 0" in a table, and <c>RawContent.Warnings</c> was never shown. The notes now travel with the result
/// (<see cref="ExtractionResult.Warnings"/>), reach <c>info.json</c> as <c>warnings</c>, and a clean extraction is unchanged.
/// </summary>
public sealed class ExtractionWarningsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fileflux-warnings-" + Guid.NewGuid().ToString("N"));

    public ExtractionWarningsTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Collect_AddsTheZeroTextNote_WhenANonEmptyFileYieldedNoText()
    {
        var warnings = ExtractionWarnings.Collect(new RawContent { Warnings = ["reader note"] }, fileSize: 1616, extractedText: "");

        warnings.Should().HaveCount(2);
        warnings[0].Should().Be("reader note");
        warnings[1].Should().Contain("No text was extracted").And.Contain("1616 bytes");
    }

    [Fact]
    public void Collect_NotesNothing_ForAnEmptyFile_AndForAFileThatYieldedText()
    {
        ExtractionWarnings.Collect(new RawContent(), fileSize: 0, extractedText: "").Should().BeEmpty();
        ExtractionWarnings.Collect(new RawContent(), fileSize: 72, extractedText: "Name  Qty").Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractToDirectory_ReportsAFileThatHasContentButNoText_InTheResultAndInfoJson()
    {
        var input = Path.Combine(_root, "blank.txt");
        await File.WriteAllTextAsync(input, "   \n\t \n   ", Ct);

        var result = await Processor().ExtractToDirectoryAsync(
            input, new OutputOptions { OutputDirectory = Path.Combine(_root, "out"), Format = "md" }, cancellationToken: Ct);

        result.ProcessedText.Should().BeNullOrWhiteSpace();
        result.Warnings.Should().ContainSingle(w => w.Contains("No text was extracted"));

        var info = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "out", "info.json"), Ct))!;
        info["warnings"]!.AsArray().Select(n => n!.GetValue<string>()).Should().ContainSingle(w => w.Contains("No text was extracted"));
    }

    [Fact]
    public async Task ExtractToDirectory_KeepsACleanExtractionUnchanged()
    {
        var input = Path.Combine(_root, "clean.md");
        await File.WriteAllTextAsync(input, "# Title\n\nFirst paragraph about wind turbines.\n", Ct);

        var result = await Processor().ExtractToDirectoryAsync(
            input, new OutputOptions { OutputDirectory = Path.Combine(_root, "clean-out"), Format = "md" }, cancellationToken: Ct);

        result.ProcessedText.Should().Contain("wind turbines");
        result.Warnings.Should().BeEmpty();

        var info = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "clean-out", "info.json"), Ct))!.AsObject();
        info.ContainsKey("warnings").Should().BeFalse("a clean extraction's info.json carries no warnings key");
    }

    [Fact]
    public async Task WriteChunkingInfo_CarriesTheExtractionWarnings()
    {
        var extraction = new ExtractionResult
        {
            ParsedContent = new RefinedContent { Metadata = new DocumentMetadata { FileName = "sheet.xlsx" } },
            ProcessedText = "",
            Warnings = ["No text was extracted (0 characters) from a non-empty file (1616 bytes)."]
        };
        var chunking = new ChunkingResult { Chunks = [], Extraction = extraction, Options = new ChunkingOptions() };
        var dir = Path.Combine(_root, "chunked");

        await new FileSystemOutputWriter().WriteChunkingAsync(chunking, dir, new OutputOptions { Format = "md" }, Ct);

        var info = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(dir, "info.json"), Ct))!;
        info["warnings"]!.AsArray().Should().HaveCount(1);
    }

    private static FluxDocumentProcessor Processor()
    {
        var services = new ServiceCollection();
        services.AddFileFlux();
        return services.BuildServiceProvider().GetRequiredService<FluxDocumentProcessor>();
    }
}
