using System.Reflection;
using System.Text.RegularExpressions;
using FileFlux.Core;
using Iyu.Conventions.Testing.Snippets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests;

/// <summary>
/// Compiles every C# block in README.md and the guides under docs/ against the current assemblies, and resolves every
/// service the README asks the container for. The Quick Start once asked for an <c>IDocumentProcessor</c> the container
/// never registers, called a <c>ProcessAsync(path)</c> the interface does not have and read a <c>chunk.Index</c> that does
/// not exist — and <see cref="DocsSnippetRosterTests"/> passed it, because it checks only that a method of that name exists
/// on some type. A compiler checks the receiver, the arguments, the members read and the namespaces.
/// </summary>
/// <remarks>
/// Each block is compiled as a top-level program by <see cref="SnippetCompiler"/>: its own usings, the usings below, and
/// the stand-ins below when the block uses the name without declaring it — values a reader already has from the
/// surrounding text (a built provider, an extracted reader), not part of what the block shows. Every block of every
/// document is compiled; a section that is deliberately not a program is in <see cref="Fragments"/> with the reason.
/// </remarks>
public class ReadmeSnippetCompileTests
{
    // A block that is deliberately not a program (a signature sketch, pseudocode), by the heading it sits under, with the
    // reason. Shrink this, never grow it silently.
    private static readonly SnippetRoster Fragments = new(new Dictionary<string, string>(StringComparer.Ordinal));

    // The documents whose blocks are compiled, relative to the repository root.
    private static readonly string[] Documents = ["README.md", "docs/TUTORIAL.md", "docs/ARCHITECTURE.md"];

    private static readonly string[] Usings =
    [
        "System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Threading", "System.Threading.Tasks",
        "FileFlux", "FileFlux.Core", "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Logging",
    ];

    private static readonly Dictionary<string, string> StandIns = new(StringComparer.Ordinal)
    {
        ["services"] = "IServiceCollection services = null!;",
        ["provider"] = "IServiceProvider provider = null!;",
        ["reader"] = "IDocumentReader reader = null!;",
        ["processor"] = "IDocumentProcessor processor = null!;",
        ["factory"] = "IDocumentProcessorFactory factory = null!;",
        ["logger"] = "ILogger logger = null!;",
        ["storedText"] = "string storedText = \"\";",
        ["storedSpans"] = "IReadOnlyList<SourceSpan> storedSpans = [];",
        ["myAnalysisService"] = "IDocumentAnalysisService myAnalysisService = null!;",
        ["myVisionService"] = "IImageToTextService myVisionService = null!;",
        ["myEmbeddingService"] = "IEmbeddingService myEmbeddingService = null!;",
        ["enricher"] = "IMetadataEnricher enricher = null!;",
        ["chunk"] = "DocumentChunk chunk = null!;",
        ["documentText"] = "string documentText = \"\";",
    };

    private static readonly string[] AssembliesToLoad =
    [
        "FileFlux", "FileFlux.Core", "FileFlux.Providers.LMSupply",
        "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.Caching.Abstractions", "Microsoft.Extensions.Caching.Memory", "Microsoft.Extensions.Options",
    ];

    private static readonly SnippetCompiler Compiler = new(SnippetCompiler.ReferencesFor([.. AssembliesToLoad.Select(Assembly.Load)]))
    {
        Usings = Usings,
        StandIns = StandIns,
    };

    private static readonly IReadOnlyList<DocSnippet> Snippets =
        [.. Documents.SelectMany(d => DocSnippets.Read(Path.Combine(RepositoryRoot(), d), d))];

    public static TheoryData<string> Blocks() => new(Snippets.Select(s => s.Key));

    [Theory]
    [MemberData(nameof(Blocks))]
    public void ReadmeBlock_Compiles(string key)
    {
        var snippet = Snippets.Single(s => s.Key == key);
        if (!Fragments.IsFragment(snippet))
            Compiler.ShouldCompile(snippet);
    }

    [Fact]
    public void EveryReadmeBlock_IsFoundAndFragmentsNameRealHeadings()
    {
        Assert.True(Snippets.Count(s => s.File == "README.md") >= 8, "expected the README's C# blocks");
        Fragments.ShouldMatch([.. Snippets], atLeast: 40);
    }

    /// <summary>Positive control: the compiler rejects what the old Quick Start did.</summary>
    [Fact]
    public void Compile_RejectsTheOldQuickStart()
    {
        var errors = Compiler.Compile("""
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
        var names = Regex.Matches(File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md")), @"GetRequiredService<([A-Za-z_][\w\.]*)>\(\)")
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

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FileFlux.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("FileFlux.slnx not found above the test output directory");
    }
}
