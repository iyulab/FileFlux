using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FileFlux.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests;

/// <summary>
/// Compiles every <c>```csharp</c> block in README.md against the current assemblies, and resolves every service the
/// README asks the container for. The Quick Start once asked for an <c>IDocumentProcessor</c> the container never
/// registers, called a <c>ProcessAsync(path)</c> the interface does not have and read a <c>chunk.Index</c> that does not
/// exist — and <see cref="DocsSnippetRosterTests"/> passed it, because it checks only that a method of that name exists
/// on some type. A compiler checks the receiver, the arguments, the members read and the namespaces.
/// The guide sections in <see cref="CompiledSections"/> are compiled the same way.
/// </summary>
/// <remarks>
/// A block is compiled as a top-level program: its <c>using</c> lines are hoisted, the common usings below are added, and
/// the stand-ins below are declared when the block uses the name without declaring it — values a reader already has
/// from the surrounding text (a built provider, an extracted reader), not part of what the block shows.
/// </remarks>
public class ReadmeSnippetCompileTests
{
    // A block that is deliberately not a program (a signature sketch, pseudocode) is listed here by the heading it sits
    // under, with the reason. Shrink this, never grow it silently.
    private static readonly Dictionary<string, string> Fragments = new(StringComparer.Ordinal);

    // Sections of other documents whose C# blocks are compiled too: (file relative to the repository root, heading). A
    // section runs from its heading to the next heading of the same or a higher level. Grow this as guides are repaired.
    private static readonly (string File, string Heading)[] CompiledSections =
    [
        ("docs/TUTORIAL.md", "Basic Usage"),
        ("docs/TUTORIAL.md", "Metadata Enrichment"),
        ("docs/ARCHITECTURE.md", "Extensibility Pattern"),
    ];

    private const string CommonUsings = """
        using System;
        using System.Collections.Generic;
        using System.IO;
        using System.Linq;
        using System.Threading.Tasks;
        using FileFlux;
        using FileFlux.Core;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Logging;
        """;

    private static readonly (string Name, string Declaration)[] StandIns =
    [
        ("services", "IServiceCollection services = null!;"),
        ("provider", "IServiceProvider provider = null!;"),
        ("reader", "IDocumentReader reader = null!;"),
        ("processor", "IDocumentProcessor processor = null!;"),
        ("logger", "ILogger logger = null!;"),
        ("storedText", "string storedText = \"\";"),
        ("storedSpans", "IReadOnlyList<SourceSpan> storedSpans = [];"),
        ("myAnalysisService", "IDocumentAnalysisService myAnalysisService = null!;"),
        ("myVisionService", "IImageToTextService myVisionService = null!;"),
        ("enricher", "IMetadataEnricher enricher = null!;"),
        ("chunk", "DocumentChunk chunk = null!;"),
        ("documentText", "string documentText = \"\";"),
    ];

    private static readonly string[] AssembliesToLoad =
    [
        "FileFlux", "FileFlux.Core", "FileFlux.Providers.LMSupply",
        "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.Caching.Abstractions", "Microsoft.Extensions.Caching.Memory", "Microsoft.Extensions.Options",
    ];

    public static TheoryData<string> Blocks()
    {
        var data = new TheoryData<string>();
        foreach (var block in ReadBlocks())
            data.Add(block.Key);
        return data;
    }

    [Theory]
    [MemberData(nameof(Blocks))]
    public void ReadmeBlock_Compiles(string key)
    {
        var block = ReadBlocks().Single(b => b.Key == key);
        if (Fragments.ContainsKey(block.Heading))
            return;

        var errors = Compile(block.Code);

        Assert.True(errors.IsEmpty,
            $"README block {key} does not compile against the current API:\n" +
            string.Join("\n", errors.Select(e => e.ToString())) + "\n--- source ---\n" + Program(block.Code));
    }

    [Fact]
    public void EveryReadmeBlock_IsFoundAndFragmentsNameRealHeadings()
    {
        var blocks = ReadBlocks();
        var readme = ReadBlocks(ReadmePath(), keyPrefix: "", section: null);
        Assert.True(readme.Count >= 8, $"expected the README's C# blocks, found {readme.Count}");
        Assert.All(Fragments.Keys, heading => Assert.Contains(blocks, b => b.Heading == heading));
    }

    /// <summary>A listed guide section that yields no block (renamed heading, moved file) would compile nothing and pass.</summary>
    [Fact]
    public void EveryCompiledSection_HasBlocks()
    {
        foreach (var (file, heading) in CompiledSections)
        {
            var blocks = ReadBlocks(Path.Combine(RepositoryRoot(), file), file + " ", heading);
            Assert.True(blocks.Count > 0, $"{file} has no C# block under the heading '{heading}'");
        }
    }

    /// <summary>Positive control for the section reader: it keeps sub-headings, ignores fenced lines and stops at the next heading of the same level.</summary>
    [Fact]
    public void SectionReader_TakesOnlyTheSection()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fileflux-section-{Guid.NewGuid():N}.md");
        File.WriteAllText(path, string.Join("\n",
            "## Before", "```csharp", "var before = 1;", "```",
            "### Target", "```csharp", "var inside = 1;", "```",
            "#### Sub", "```bash", "# not a heading", "```", "```csharp", "var nested = 1;", "```",
            "### After", "```csharp", "var after = 1;", "```"));
        try
        {
            var blocks = ReadBlocks(path, "", "Target");
            Assert.Equal(["var inside = 1;", "var nested = 1;"], blocks.Select(b => b.Code.Trim()).ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Positive control: the compiler rejects what the old Quick Start did.</summary>
    [Fact]
    public void Compile_RejectsTheOldQuickStart()
    {
        var errors = Compile("""
            var processor = provider.GetRequiredService<IDocumentProcessor>();
            var chunks = await processor.ProcessAsync("document.pdf");
            foreach (var chunk in chunks) Console.WriteLine(chunk.Index);
            """);

        Assert.NotEmpty(errors);
    }

    /// <summary>
    /// A block that compiles can still ask the container for a service <c>AddFileFlux()</c> never registers — the old
    /// Quick Start failed at runtime that way. Every <c>GetRequiredService&lt;T&gt;()</c> the README names must resolve.
    /// </summary>
    [Fact]
    public void EveryServiceTheReadmeResolves_IsRegisteredByAddFileFlux()
    {
        var names = Regex.Matches(File.ReadAllText(ReadmePath()), @"GetRequiredService<([A-Za-z_][\w\.]*)>\(\)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(names);

        var services = new ServiceCollection();
        services.AddFileFlux();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        foreach (var name in names)
        {
            var type = FindType(name);
            Assert.True(type is not null, $"README resolves {name}, which no loaded FileFlux assembly declares");
            Assert.True(scope.ServiceProvider.GetService(type!) is not null,
                $"README resolves {name} from AddFileFlux(), which does not register it");
        }
    }

    /// <summary>
    /// The Quick Start flow, run: a consumer that copies it gets chunks, with no AI service registered. A Markdown file
    /// stands in for the README's PDF so the fact does not depend on the native PDF reader.
    /// </summary>
    [Fact]
    public async Task QuickStartFlow_ProducesChunks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fileflux-readme-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(path,
            "# Title\n\nFirst paragraph about solar panels and how they convert light.\n\n## Section\n\nSecond paragraph about batteries.\n",
            TestContext.Current.CancellationToken);
        try
        {
            var services = new ServiceCollection();
            services.AddFileFlux();
            using var provider = services.BuildServiceProvider();

            var factory = provider.GetRequiredService<IDocumentProcessorFactory>();
            await using var processor = factory.Create(path);
            await processor.ProcessAsync(cancellationToken: TestContext.Current.CancellationToken);

            var chunks = processor.Result.ToList();
            Assert.NotEmpty(chunks);
            Assert.Contains(chunks, c => c.Content.Contains("solar panels", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Type? FindType(string name)
    {
        foreach (var assemblyName in AssembliesToLoad)
            Assembly.Load(assemblyName);
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("FileFlux", StringComparison.Ordinal) == true)
            .SelectMany(a => a.GetExportedTypes())
            .FirstOrDefault(t => t.FullName == name || t.Name == name);
    }

    private sealed record Block(string Key, string Heading, string Code);

    private static List<Block> ReadBlocks() =>
        ReadBlocks(ReadmePath(), keyPrefix: "", section: null)
            .Concat(CompiledSections.SelectMany(s => ReadBlocks(Path.Combine(RepositoryRoot(), s.File), s.File + " ", s.Heading)))
            .ToList();

    /// <summary>
    /// The C# blocks of <paramref name="path"/>; with <paramref name="section"/>, only those under that heading and its
    /// sub-headings. Lines inside fences are never read as headings.
    /// </summary>
    private static List<Block> ReadBlocks(string path, string keyPrefix, string? section)
    {
        var lines = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var blocks = new List<Block>();
        var heading = "(top)";
        var inSection = section is null;
        int? sectionLevel = null;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith('#'))
            {
                var level = lines[i].TakeWhile(c => c == '#').Count();
                heading = lines[i].TrimStart('#').Trim();
                if (section is not null && heading == section)
                {
                    inSection = true;
                    sectionLevel = level;
                }
                else if (sectionLevel is { } open && level <= open)
                {
                    inSection = false;
                    sectionLevel = null;
                }
            }

            var fence = lines[i].Trim();
            if (!fence.StartsWith("```", StringComparison.Ordinal))
                continue;

            var start = i + 1;
            var code = new StringBuilder();
            for (i++; i < lines.Length && lines[i].Trim() != "```"; i++)
                code.AppendLine(lines[i]);
            if (fence == "```csharp" && inSection)
                blocks.Add(new Block($"{keyPrefix}line {start}: {heading}", heading, code.ToString()));
        }

        return blocks;
    }

    private static string Program(string code)
    {
        var lines = code.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        bool IsUsingDirective(string l) =>
            l.StartsWith("using ", StringComparison.Ordinal) && l.TrimEnd().EndsWith(';') && !l.StartsWith("using var ", StringComparison.Ordinal);

        var body = string.Join("\n", lines.Where(l => !IsUsingDirective(l)));
        var standIns = StandIns
            .Where(s => Regex.IsMatch(body, $@"\b{s.Name}\b")
                        && !Regex.IsMatch(body, $@"\b(var|[A-Z][\w<>?,\s]*)\s+{s.Name}\s*(?:[=;]|in\b)"))
            .Select(s => s.Declaration);

        return string.Join("\n", lines.Where(IsUsingDirective)) + "\n" + CommonUsings + "\n"
               + string.Join("\n", standIns) + "\n" + body;
    }

    private static ImmutableArray<Diagnostic> Compile(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(Program(code), new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "ReadmeSnippet", [tree], References(),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
        return compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
    }

    private static List<MetadataReference> References()
    {
        foreach (var name in AssembliesToLoad)
            Assembly.Load(name);

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
            paths.UnionWith(trusted.Split(Path.PathSeparator).Where(p => p.Length > 0));
        paths.UnionWith(AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.Length > 0)
            .Select(a => a.Location));
        return paths.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
    }

    private static string ReadmePath() => Path.Combine(RepositoryRoot(), "README.md");

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FileFlux.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("FileFlux.slnx not found above the test output directory");
    }
}
