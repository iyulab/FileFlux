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
        var root = new RootCommand { new ExtractCommand(), new GetCommand(), new SetCommand() };
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

    [Fact]
    public async Task Get_and_set_fail_on_an_unknown_key()
    {
        Assert.Equal(1, await InvokeAsync("get", "NOT_A_KEY"));
        Assert.Equal(1, await InvokeAsync("set", "NOT_A_KEY", "value"));
    }
}
