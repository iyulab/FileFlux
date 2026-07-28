using FileFlux.Core;
using Xunit;

namespace FileFlux.Tests.Utils;

/// <summary>
/// <see cref="FileNameHelper"/> must answer the same on every host OS.
///
/// Regression guard: the original tests asserted Windows semantics and passed locally while failing on
/// the Linux CI runner the moment one was added — <c>Path.GetFileName</c> does not treat '\' as a
/// separator off Windows, and <c>Path.GetInvalidFileNameChars</c> returns only '/' and NUL on Linux.
/// Document paths cross platforms routinely, so a host-relative answer is a defect, not a nuance.
/// </summary>
public class FileNameHelperPortabilityTests
{
    [Theory]
    [InlineData(@"C:\tests\report.txt", "report.txt")]
    [InlineData("/var/data/report.txt", "report.txt")]
    [InlineData(@"\\server\share\report.txt", "report.txt")]
    [InlineData(@"C:\tests\한글파일명.txt", "한글파일명.txt")]
    [InlineData("report.txt", "report.txt")]
    public void GetSafeFileName_StripsBothSeparators_OnEveryHost(string path, string expected)
    {
        Assert.Equal(expected, FileNameHelper.GetSafeFileName(path));
    }

    [Theory]
    [InlineData("invalid<>file|name.txt")]
    [InlineData("a:b.txt")]
    [InlineData("a\"b.txt")]
    [InlineData("a?b.txt")]
    [InlineData("a*b.txt")]
    [InlineData("a\\b.txt")]
    [InlineData("a/b.txt")]
    [InlineData("a\tb.txt")]
    public void IsValidFileName_RejectsPortablyInvalidNames_OnEveryHost(string fileName)
    {
        Assert.False(FileNameHelper.IsValidFileName(fileName),
            "a name that breaks on Windows must not validate just because the tests happen to run on Linux");
    }

    [Theory]
    [InlineData("valid_file_name.txt")]
    [InlineData("한글파일명.txt")]
    [InlineData("report (final) v2.md")]
    public void IsValidFileName_AcceptsPortablyValidNames(string fileName)
    {
        Assert.True(FileNameHelper.IsValidFileName(fileName));
    }
}
