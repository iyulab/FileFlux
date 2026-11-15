using System.Text.RegularExpressions;
using FileFlux.Core;
using Microsoft.Extensions.DependencyInjection;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// The repository's own Markdown (the README, the docs, the changelog): real documents with many fenced code blocks, in
/// lists, with braces in headings and numbered points. Each goes through the default path (extract, refine, chunk) and is
/// checked against the file itself, by a scan that shares nothing with the library's:
/// <list type="bullet">
/// <item>every fenced block comes out of the reader as written (up to the uniform re-indentation of a list item's content);</item>
/// <item>every fenced block comes out of the refining exactly as the reader wrote it;</item>
/// <item>no chunk path element names something that is not a heading of the file (a code comment, a list item, a clause).</item>
/// </list>
/// Constructed fixtures missed three defects of the default Markdown path that this check found on a corpus of 204 files.
/// </summary>
public sealed partial class RepositoryMarkdownCorpusTests
{
    public static TheoryData<string> Documents()
    {
        var data = new TheoryData<string>();
        var root = FindRepoRoot();
        foreach (var file in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !f.Contains($"{Path.DirectorySeparatorChar}TestResults{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && (!f.Contains($"{Path.DirectorySeparatorChar}Fixtures{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                     || f.Contains($"{Path.DirectorySeparatorChar}MarkdownCorpus{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
                     .Order(StringComparer.Ordinal))
        {
            if (Blocks(File.ReadAllText(file)).Count > 0)
                data.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task A_document_keeps_its_code_and_its_headings_through_the_default_path(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        var services = new ServiceCollection();
        services.AddFileFlux();
        await using var provider = services.BuildServiceProvider();
        await using var processor = provider.GetRequiredService<IDocumentProcessorFactory>().Create(path);

        await processor.ExtractAsync(cancellationToken: TestContext.Current.CancellationToken);
        await processor.RefineAsync(cancellationToken: TestContext.Current.CancellationToken);
        await processor.ChunkAsync(cancellationToken: TestContext.Current.CancellationToken);

        var file = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        var raw = Blocks(processor.Result.Raw!.Text);
        var refined = Blocks(processor.Result.Refined!.Text);
        var chunks = (processor.Result.Chunks ?? []).Select(c => Normalize(c.Content)).ToList();
        var problems = new List<string>();

        var original = Blocks(file);
        if (original.Count != raw.Count)
            problems.Add($"the reader wrote {raw.Count} blocks for {original.Count}");
        else
            for (var i = 0; i < original.Count; i++)
                if (Body(original[i]) != Body(raw[i]))
                    problems.Add($"reader changed block {i + 1}:{Environment.NewLine}{original[i]}{Environment.NewLine}--- became:{Environment.NewLine}{raw[i]}");

        if (raw.Count != refined.Count)
            problems.Add($"the refining kept {refined.Count} of the reader's {raw.Count} blocks");
        else
            for (var i = 0; i < raw.Count; i++)
                if (raw[i] != refined[i])
                    problems.Add($"refining changed block {i + 1}:{Environment.NewLine}{raw[i]}{Environment.NewLine}--- became:{Environment.NewLine}{refined[i]}");

        // A block may be larger than a chunk and be cut across chunks; it is never absent.
        var stream = string.Join('\n', chunks);
        foreach (var block in refined)
        {
            var longest = Body(block).Split('\n').OrderByDescending(l => l.Length).First();
            if (longest.Trim().Length > 0 && !stream.Contains(longest, StringComparison.Ordinal))
                problems.Add($"a block is missing from the chunks, its longest line: {longest.Trim()}");
        }

        var headings = Headings(file);
        foreach (var chunk in processor.Result.Chunks ?? [])
            foreach (var element in chunk.Location.HeadingPath)
                if (!headings.Contains(Fold(element)))
                    problems.Add($"chunk {chunk.ChunkIndex} has a heading path element that is no heading of the file: {element}");

        Assert.True(problems.Count == 0, $"{relativePath}:{Environment.NewLine}{string.Join(Environment.NewLine, problems.Distinct().Take(10))}");
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    /// <summary>The fenced blocks of a Markdown text, fence lines included (an independent scan: CommonMark-style, closed blocks only).</summary>
    private static List<string> Blocks(string text)
    {
        var result = new List<string>();
        string[]? open = null;
        var marker = '\0';
        var length = 0;
        var current = new List<string>();
        foreach (var line in Normalize(text).Split('\n'))
        {
            if (open is null)
            {
                var match = OpeningFence().Match(line);
                if (match.Success && !(match.Groups[2].Value[0] == '`' && match.Groups[3].Value.Contains('`', StringComparison.Ordinal)))
                {
                    open = [line];
                    marker = match.Groups[2].Value[0];
                    length = match.Groups[2].Value.Length;
                    current = [line];
                }
            }
            else
            {
                current.Add(line);
                var match = ClosingFence().Match(line);
                if (match.Success && match.Groups[1].Value[0] == marker && match.Groups[1].Value.Length >= length)
                {
                    result.Add(string.Join('\n', current));
                    open = null;
                }
            }
        }

        return result;
    }

    /// <summary>A block's body without its fence lines and without the indentation shared by its lines (a list item's content is re-indented as a whole).</summary>
    private static string Body(string block)
    {
        var lines = block.Split('\n');
        var body = lines.Skip(1).Take(Math.Max(0, lines.Length - 2)).ToList();
        var common = body.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart(' ').Length).DefaultIfEmpty(0).Min();
        return string.Join('\n', body.Select(l => l.Length >= common ? l[common..] : l.TrimStart(' ')));
    }

    private static HashSet<string> Headings(string text)
    {
        var headings = new HashSet<string>();
        var lines = Normalize(text).Split('\n');
        var inFence = false;
        var marker = '\0';
        var length = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!inFence)
            {
                var open = OpeningFence().Match(line);
                if (open.Success)
                {
                    inFence = true;
                    marker = open.Groups[2].Value[0];
                    length = open.Groups[2].Value.Length;
                    continue;
                }
            }
            else
            {
                var close = ClosingFence().Match(line);
                if (close.Success && close.Groups[1].Value[0] == marker && close.Groups[1].Value.Length >= length)
                    inFence = false;
                continue;
            }

            var atx = AtxHeading().Match(line);
            if (atx.Success)
                headings.Add(Fold(atx.Groups[1].Value));
            else if (i + 1 < lines.Length && line.Trim().Length > 0 && SetextUnderline().IsMatch(lines[i + 1]))
                headings.Add(Fold(line));
        }

        return headings;
    }

    private static string Fold(string text) => Whitespace().Replace(Markup().Replace(text, " "), " ").Trim().ToLowerInvariant();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileFlux.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("FileFlux.slnx was not found above the test directory.");
    }

    [GeneratedRegex(@"^(\s*)(`{3,}|~{3,})(.*)$")]
    private static partial Regex OpeningFence();

    [GeneratedRegex(@"^ {0,3}(`{3,}|~{3,})\s*$")]
    private static partial Regex ClosingFence();

    [GeneratedRegex(@"^ {0,3}#{1,6}\s+(.+?)\s*#*\s*$")]
    private static partial Regex AtxHeading();

    [GeneratedRegex(@"^ {0,3}(=+|-+)\s*$")]
    private static partial Regex SetextUnderline();

    [GeneratedRegex(@"[`*_\[\]()<>#]")]
    private static partial Regex Markup();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
