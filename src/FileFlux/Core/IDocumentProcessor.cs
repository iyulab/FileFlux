using FileFlux.Domain;
using System.Runtime.CompilerServices;

namespace FileFlux;

/// <summary>
/// Core document processor interface - clean and consistent API
/// </summary>
public interface IDocumentProcessor
{
    // ========================================
    // Full Pipeline: File → Chunks
    // ========================================

    /// <summary>
    /// Process entire pipeline - batch return
    /// </summary>
    /// <param name="filePath">Document file path</param>
    /// <param name="options">Chunking options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of document chunks</returns>
    Task<DocumentChunk[]> ProcessAsync(
        string filePath,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Process entire pipeline - streaming return
    /// </summary>
    /// <param name="filePath">Document file path</param>
    /// <param name="options">Chunking options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream of document chunks</returns>
    IAsyncEnumerable<DocumentChunk> ProcessStreamAsync(
        string filePath,
        ChunkingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    // ========================================
    // Stage 1: Extract (File → RawContent)
    // ========================================

    /// <summary>
    /// Extract raw text - batch return
    /// </summary>
    /// <param name="filePath">Document file path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Raw extraction result</returns>
    Task<RawContent> ExtractAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extract raw text - streaming return (for large files)
    /// </summary>
    /// <param name="filePath">Document file path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream of raw content (typically single item)</returns>
    IAsyncEnumerable<RawContent> ExtractStreamAsync(
        string filePath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    // ========================================
    // Stage 2: Parse (RawContent → ParsedContent)
    // ========================================

    /// <summary>
    /// Parse structure - batch return
    /// </summary>
    /// <param name="raw">Raw content from extraction</param>
    /// <param name="options">Parsing options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parsed structure result</returns>
    Task<ParsedContent> ParseAsync(
        RawContent raw,
        ParsingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parse structure - streaming return (for progressive parsing)
    /// </summary>
    /// <param name="raw">Raw content from extraction</param>
    /// <param name="options">Parsing options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream of parsed content (typically single item)</returns>
    IAsyncEnumerable<ParsedContent> ParseStreamAsync(
        RawContent raw,
        ParsingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    // ========================================
    // Stage 3: Chunk (ParsedContent → Chunks)
    // ========================================

    /// <summary>
    /// Create chunks - batch return
    /// </summary>
    /// <param name="parsed">Parsed content from parsing</param>
    /// <param name="options">Chunking options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of chunks</returns>
    Task<DocumentChunk[]> ChunkAsync(
        ParsedContent parsed,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create chunks - streaming return
    /// </summary>
    /// <param name="parsed">Parsed content from parsing</param>
    /// <param name="options">Chunking options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream of chunks as they are created</returns>
    IAsyncEnumerable<DocumentChunk> ChunkStreamAsync(
        ParsedContent parsed,
        ChunkingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    // ========================================
    // Quality Analysis (Optional)
    // ========================================

    /// <summary>
    /// Analyze document processing quality for RAG optimization
    /// </summary>
    /// <param name="filePath">Document file path</param>
    /// <param name="options">Chunking options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Quality analysis report</returns>
    Task<DocumentQualityReport> AnalyzeQualityAsync(
        string filePath,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate QA benchmark dataset from document
    /// </summary>
    /// <param name="filePath">Document file path</param>
    /// <param name="questionCount">Number of questions to generate</param>
    /// <param name="existingQA">Existing QA dataset to merge with</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>QA benchmark with generated questions</returns>
    Task<QABenchmark> GenerateQAAsync(
        string filePath,
        int questionCount = 20,
        QABenchmark? existingQA = null,
        CancellationToken cancellationToken = default);
}
