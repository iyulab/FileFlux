using FileFlux.CLI.Commands;
using System.CommandLine;
using Xunit;

namespace FileFlux.Tests.CLI;

/// <summary>
/// A command that fails must exit non-zero so a script or CI step can tell failure from success.
/// </summary>
public class CommandExitCodeTests : IDisposable
{
    private readonly string _outputDir = Path.Combine(Path.GetTempPath(), $"FileFlux_ExitCode_{Guid.NewGuid():N}");

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_outputDir))
        {
            Directory.Delete(_outputDir, recursive: true);
        }
    }

    private static Task<int> InvokeAsync(params string[] args)
    {
        var root = new RootCommand
        {
            new ExtractCommand(), new ChunkCommand(), new ProcessCommand(), new RefineCommand(),
            new EnrichCommand(), new QACommand(), new EvaluateCommand(), new GetCommand(), new SetCommand()
        };
        return root.Parse(args).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Extract_succeeds_on_a_readable_document()
    {
        var input = Path.Combine(AppContext.BaseDirectory, "Fixtures", "oai_gpt-oss_model_card.pdf");

        var exitCode = await InvokeAsync("extract", input, "-o", _outputDir, "-q", "--no-extract-images");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Extract_fails_when_reading_throws()
    {
        var input = Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-encrypted.xls");

        var exitCode = await InvokeAsync("extract", input, "-o", _outputDir, "-q");

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Extract_fails_when_the_input_does_not_exist()
    {
        var exitCode = await InvokeAsync("extract", Path.Combine(_outputDir, "missing.pdf"), "-o", _outputDir);

        Assert.Equal(1, exitCode);
    }

    [Theory]
    [InlineData("extract", "jsonl")]
    [InlineData("extract", "markdown")]
    [InlineData("chunk", "markdown")]
    [InlineData("process", "txt", "--no-ai")]
    [InlineData("refine", "jsonl")]
    public async Task A_format_the_command_does_not_write_fails_before_any_output(string command, string format, string? extra = null)
    {
        var inputDir = _outputDir + "-input";
        Directory.CreateDirectory(inputDir);
        var input = Path.Combine(inputDir, "guide.md");
        await File.WriteAllTextAsync(input, "# Guide\n\nA short paragraph about installing the tool.\n", TestContext.Current.CancellationToken);

        string[] args = [command, input, "-o", _outputDir, "-q", "--no-extract-images", "-f", format];
        int exitCode;
        try
        {
            exitCode = await InvokeAsync(extra is null ? args : [.. args, extra]);
        }
        finally
        {
            Directory.Delete(inputDir, recursive: true);
        }

        Assert.Equal(1, exitCode);
        Assert.False(Directory.Exists(_outputDir), $"{command} -f {format} wrote output before rejecting the format");
    }

    [Theory]
    [InlineData("extract", "md, json")]
    [InlineData("chunk", "md, json, jsonl")]
    [InlineData("process", "md, json, jsonl")]
    [InlineData("refine", "md, json")]
    [InlineData("enrich", "json, jsonl")]
    [InlineData("qa", "json, jsonl")]
    [InlineData("evaluate", "json, jsonl")]
    public void An_unknown_format_is_reported_with_the_allowed_values(string command, string allowed)
    {
        var root = new RootCommand
        {
            new ExtractCommand(), new ChunkCommand(), new ProcessCommand(), new RefineCommand(),
            new EnrichCommand(), new QACommand(), new EvaluateCommand()
        };

        var result = root.Parse([command, "input.pdf", "-f", "xml"]);

        var error = Assert.Single(result.Errors);
        Assert.Contains("'xml'", error.Message, StringComparison.Ordinal);
        Assert.Contains(allowed, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("chunk", "JSON")]
    [InlineData("extract", "Md")]
    public void A_known_format_is_accepted_in_any_case(string command, string format)
    {
        var root = new RootCommand { new ExtractCommand(), new ChunkCommand() };

        Assert.Empty(root.Parse([command, "input.pdf", "-f", format]).Errors);
    }

    [Fact]
    public async Task Get_and_set_fail_on_an_unknown_key()
    {
        Assert.Equal(1, await InvokeAsync("get", "NOT_A_KEY"));
        Assert.Equal(1, await InvokeAsync("set", "NOT_A_KEY", "value"));
    }
}
