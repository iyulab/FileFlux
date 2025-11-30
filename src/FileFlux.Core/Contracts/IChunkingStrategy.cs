namespace FileFlux.Core;

/// <summary>
/// Chunking strategy interface - defines various methods to split documents into chunks
/// </summary>
public interface IChunkingStrategy
{
    /// <summary>
    /// Strategy name
    /// </summary>
    string StrategyName { get; }

    /// <summary>
    /// Split document content into chunks
    /// </summary>
    /// <param name="content">Document content</param>
    /// <param name="options">Chunking options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of generated chunks</returns>
    Task<IEnumerable<DocumentChunk>> ChunkAsync(
        DocumentContent content,
        ChunkingOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimate chunk count (for performance optimization)
    /// </summary>
    /// <param name="content">Document content</param>
    /// <param name="options">Chunking options</param>
    /// <returns>Estimated chunk count</returns>
    int EstimateChunkCount(DocumentContent content, ChunkingOptions options);

    /// <summary>
    /// List of supported option keys for this strategy
    /// </summary>
    IEnumerable<string> SupportedOptions { get; }
}
