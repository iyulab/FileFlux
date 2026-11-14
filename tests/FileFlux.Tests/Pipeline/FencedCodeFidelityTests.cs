using FileFlux.Core;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FluxCurator.Infrastructure.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// A fenced code block comes out of both processors as it went in: the lines a prose cleaner would tidy (repeated closing
/// braces, deep or tab indentation, blank lines inside, trailing spaces, a number-only line, a line repeated far apart)
/// are code.
/// </summary>
public sealed class FencedCodeFidelityTests : IDisposable
{
    private static readonly string[] Blocks =
    [
        "```python\ndef f(x):\n    if x:\n        return {\n            \"a\": 1,\n        }\n    return None\n\n\ndef g():\n\ttabbed = 1   \n    pass\n```",
        "```c\nif (a) {\n  if (b) {\n    run();\n  }\n}\n}\n}\n42\nEND\nEND\nEND\n```",
        // (The Markdown reader writes every fence with backticks; the code is what has to stay.)
        "```sh\nset -e\n\n\n\necho   spaced    words\n\n---\n    deep\n```",
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "fileflux-codefidelity-" + Guid.NewGuid().ToString("N"));

    public FencedCodeFidelityTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static string Document() =>
        "# Guide\n\n" + string.Join(
            "\n\n",
            Blocks.Select((block, i) => $"## Part {i + 1}\n\nSome prose before part {i + 1} that is long enough to be a paragraph.\n\n{block}\n\nSome prose after part {i + 1}.")) + "\n";

    private static string Joined(IEnumerable<DocumentChunk> chunks) =>
        string.Join("\n", chunks.Select(c => c.Content.ReplaceLineEndings("\n")));

    private async Task<string> WriteAsync()
    {
        var path = Path.Combine(_root, "guide.md");
        await File.WriteAllTextAsync(path, Document(), TestContext.Current.CancellationToken);
        return path;
    }

    private static ChunkingOptions OneBigChunk() =>
        new() { Strategy = ChunkingStrategies.Paragraph, MaxChunkSize = 8192, MinChunkSize = 1, OverlapSize = 0 };

    [Fact]
    public async Task The_legacy_processor_keeps_every_code_block_as_written()
    {
        var path = await WriteAsync();

        var chunks = await new FluxDocumentProcessor(new DocumentReaderFactory(), new DocumentParserFactory(), new ChunkerFactory())
            .ProcessAsync(path, OneBigChunk(), TestContext.Current.CancellationToken);

        var text = Joined(chunks);
        foreach (var block in Blocks)
            Assert.True(text.Contains(block, StringComparison.Ordinal), "a code block changed on its way through; chunk text:\n" + text);
    }

    [Fact]
    public async Task The_stateful_processor_keeps_every_code_block_as_written()
    {
        var path = await WriteAsync();

        await using var processor = new DocumentProcessorFactory(new DocumentReaderFactory(), new ChunkerFactory(), loggerFactory: NullLoggerFactory.Instance)
            .Create(path);
        await processor.ProcessAsync(new ProcessingOptions { Chunking = OneBigChunk() }, TestContext.Current.CancellationToken);

        var text = Joined(processor.Result.Chunks!);
        foreach (var block in Blocks)
            Assert.True(text.Contains(block, StringComparison.Ordinal), "a code block changed on its way through; chunk text:\n" + text);
    }
}
