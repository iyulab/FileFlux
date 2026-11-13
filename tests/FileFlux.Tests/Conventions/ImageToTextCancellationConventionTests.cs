using System.Text.RegularExpressions;
using Xunit;

namespace FileFlux.Tests.Conventions;

/// <summary>
/// Convention teeth: every library call of <c>IImageToTextService.ExtractTextAsync</c> reads a result that a consumer's
/// adapter may use to answer the caller's cancellation as an error message, so each is followed by
/// <c>ImageToTextCancellation.ThrowIfCancelledBehindError</c>. Four callers had been taking such an answer for «a picture
/// without text» and a cancelled call returned as a finished one (FileFlux 0.69.0); a new caller added without the check
/// brings it back, and nothing else would notice.
/// </summary>
public sealed partial class ImageToTextCancellationConventionTests
{
    private const string Guard = "ImageToTextCancellation.ThrowIfCancelledBehindError";

    // A call (a receiver before the dot), not the implementers' own overloads delegating to each other.
    [GeneratedRegex(@"[\w\)]\.ExtractTextAsync\(")]
    private static partial Regex CallSite();

    // The result is read within a few lines of the call (an argument list wraps over several).
    private const int Window = 14;

    [Fact]
    public void EveryCallThatReadsAnImageToTextResult_ChecksForACancellationBehindAnError()
    {
        var unguarded = new List<string>();
        var calls = 0;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(FindRepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                              && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var lines = File.ReadAllLines(file);
            calls += CountCalls(lines);
            unguarded.AddRange(Unguarded(lines).Select(l => $"{Path.GetRelativePath(FindRepoRoot(), file)}:{l}"));
        }

        Assert.True(calls >= 7, $"only {calls} call site(s) found - the scan lost its subject (the picture reader x2, the page reading, the document processor, the image descriptions, the image processor x2)");
        Assert.True(unguarded.Count == 0,
            "an ExtractTextAsync result is read without " + Guard + " within " + Window + " lines of the call (a consumer adapter that "
            + "answers every exception with ErrorMessage turns the caller's cancellation into such an answer):\n  " + string.Join("\n  ", unguarded));
    }

    [Fact]
    public void TheScan_FlagsACallWithoutTheCheck_AndAcceptsOneWithIt()
    {
        string[] bare =
        [
            "var result = await service.ExtractTextAsync(",
            "    bytes, options, ct).ConfigureAwait(false);",
            "if (!string.IsNullOrWhiteSpace(result.ExtractedText)) Use(result);",
        ];
        string[] guarded =
        [
            "var result = await service.ExtractTextAsync(bytes, options, ct).ConfigureAwait(false);",
            "ImageToTextCancellation.ThrowIfCancelledBehindError(result, ct);",
        ];
        string[] declaration = ["public Task<ImageToTextResult> ExtractTextAsync(byte[] imageData, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)"];
        string[] delegating = ["return await ExtractTextAsync(imageData, options, cancellationToken);"];

        Assert.Single(Unguarded(bare));
        Assert.Empty(Unguarded(guarded));
        Assert.Empty(Unguarded(declaration));
        Assert.Empty(Unguarded(delegating));
        Assert.Equal(1, CountCalls(bare));
    }

    private static int CountCalls(string[] lines) => lines.Count(l => CallSite().IsMatch(l));

    private static IEnumerable<int> Unguarded(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (!CallSite().IsMatch(lines[i]))
                continue;
            var end = Math.Min(lines.Length, i + Window);
            var guarded = false;
            for (var j = i; j < end && !guarded; j++)
                guarded = lines[j].Contains(Guard, StringComparison.Ordinal);
            if (!guarded)
                yield return i + 1;
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FileFlux.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("FileFlux.slnx not found above the test output.");
    }
}
