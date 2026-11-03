using FileFlux.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// Ordinary sentences that begin like a list marker keep every word through extraction and refinement. The Markdown
/// converter read "letters or digits followed by a dot" as a marker, so <c>Dr. Smith arrived</c> came out as
/// <c>- Smith arrived</c>, <c>v1.2 was released</c> as <c>- 2 was released</c> and <c>3.14 is pi</c> as <c>3. 14 is pi</c>
/// for plain-text and Markdown input.
/// </summary>
public sealed class ProseLineStartTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fileflux-prose-start-" + Guid.NewGuid().ToString("N"));

    public ProseLineStartTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp cleanup is best effort */ }
    }

    [Theory]
    [InlineData("txt")]
    [InlineData("md")]
    public async Task RefinedText_KeepsSentencesThatStartLikeAListMarker(string extension)
    {
        string[] sentences =
        [
            "Dr. Smith arrived on Monday.",
            "3.14 is the value of pi.",
            "v1.2 was released in May.",
            "U.S. policy changed this year.",
            "e.g. a list of examples follows.",
            "2024.csv is the file name.",
            "No. 5 is next in the queue.",
        ];
        var path = Path.Combine(_dir, "prose." + extension);
        await File.WriteAllTextAsync(path, string.Join("\n\n", sentences) + "\n", TestContext.Current.CancellationToken);
        var services = new ServiceCollection();
        services.AddFileFlux();
        await using var provider = services.BuildServiceProvider();
        await using var processor = provider.GetRequiredService<IDocumentProcessorFactory>().Create(path);

        await processor.RefineAsync(cancellationToken: TestContext.Current.CancellationToken);
        var lines = processor.Result.Refined!.Text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        foreach (var sentence in sentences)
        {
            Assert.Contains(sentence, lines);
        }
    }
}
