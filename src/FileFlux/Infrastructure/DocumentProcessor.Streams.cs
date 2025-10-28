using FileFlux.Domain;
using System.Runtime.CompilerServices;

namespace FileFlux.Infrastructure;

/// <summary>
/// DocumentProcessor - Stream API implementations
/// </summary>
public partial class DocumentProcessor
{
    // Full Pipeline - Streaming
    public async IAsyncEnumerable<DocumentChunk> ProcessStreamAsync(
        string filePath,
        ChunkingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var raw = await ExtractAsync(filePath, cancellationToken);
        var parsed = await ParseAsync(raw, (DocumentParsingOptions?)null, cancellationToken);
        var chunks = await ChunkAsync(parsed, options, cancellationToken);

        foreach (var chunk in chunks)
        {
            yield return chunk;
        }
    }

    // Extract - Streaming
    public async IAsyncEnumerable<RawContent> ExtractStreamAsync(
        string filePath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return await ExtractAsync(filePath, cancellationToken);
    }

    // Parse - Streaming (with ParsingOptions)
    public async Task<ParsedContent> ParseAsync(
        RawContent raw,
        ParsingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var docOptions = new DocumentParsingOptions
        {
            UseLlmParsing = options?.UseLlm ?? true
        };
        return await ParseAsync(raw, docOptions, cancellationToken);
    }

    public async IAsyncEnumerable<ParsedContent> ParseStreamAsync(
        RawContent raw,
        ParsingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return await ParseAsync(raw, options, cancellationToken);
    }

    // Chunk - Streaming
    public async IAsyncEnumerable<DocumentChunk> ChunkStreamAsync(
        ParsedContent parsed,
        ChunkingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chunks = await ChunkAsync(parsed, options, cancellationToken);
        foreach (var chunk in chunks)
        {
            yield return chunk;
        }
    }
}
