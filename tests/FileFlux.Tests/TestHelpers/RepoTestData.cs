namespace FileFlux.Tests.TestHelpers;

/// <summary>
/// The real-world documents committed under the repository's <c>tests/</c> folder
/// (<c>test-docx</c>, <c>test-pdf</c>, <c>test-pptx</c>, <c>test-md</c>, <c>test-xlsx</c>), located from the test
/// output directory so the tests run from any checkout.
/// </summary>
internal static class RepoTestData
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "test-pdf")) && Directory.Exists(Path.Combine(dir.FullName, "test-docx")))
                return dir.FullName;
        }

        throw new DirectoryNotFoundException(
            $"No folder holding test-pdf/ and test-docx/ above '{AppContext.BaseDirectory}'. The committed test documents live in the repository's tests/ folder.");
    }
}
