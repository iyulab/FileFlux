namespace FileFlux.Core;

/// <summary>
/// Core document processor interface - clean and consistent API
/// </summary>
public interface IDocumentProcessor
{
    // ========================================
    // Full Pipeline: File -> Chunks
    // ========================================

    /// <summary>
    /// Process entire pipeline - batch return
    /// </summary>
    Task<DocumentChunk[]> ProcessAsync(
        string filePath,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Process entire pipeline - streaming return
    /// </summary>
    IAsyncEnumerable<DocumentChunk> ProcessStreamAsync(
        string filePath,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);

    // ========================================
    // Stage 1: Extract (File -> RawContent)
    // ========================================

    /// <summary>
    /// Extract raw text - batch return
    /// </summary>
    Task<RawContent> ExtractAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extract raw text - streaming return (for large files)
    /// </summary>
    IAsyncEnumerable<RawContent> ExtractStreamAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    // ========================================
    // Stage 2: Parse (RawContent -> ParsedContent)
    // ========================================

    /// <summary>
    /// Parse structure - batch return
    /// </summary>
    Task<ParsedContent> ParseAsync(
        RawContent raw,
        ParsingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parse structure - streaming return
    /// </summary>
    IAsyncEnumerable<ParsedContent> ParseStreamAsync(
        RawContent raw,
        ParsingOptions? options = null,
        CancellationToken cancellationToken = default);

    // ========================================
    // Stage 3: Chunk (ParsedContent -> Chunks)
    // ========================================

    /// <summary>
    /// Create chunks - batch return
    /// </summary>
    Task<DocumentChunk[]> ChunkAsync(
        ParsedContent parsed,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create chunks - streaming return
    /// </summary>
    IAsyncEnumerable<DocumentChunk> ChunkStreamAsync(
        ParsedContent parsed,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);
}
