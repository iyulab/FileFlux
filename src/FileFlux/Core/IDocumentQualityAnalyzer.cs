using FileFlux.Domain;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FileFlux;

/// <summary>
/// Document quality analysis interface for RAG optimization.
/// Provides consistent quality metrics for both internal benchmarking and external API usage.
/// </summary>
public interface IDocumentQualityAnalyzer
{
    /// <summary>
    /// Analyzes the overall quality of document processing for RAG systems.
    /// Uses the same internal logic as benchmarking tests to ensure consistency.
    /// </summary>
    /// <param name="filePath">Path to the document to analyze</param>
    /// <param name="options">Chunking options to use for analysis</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Comprehensive quality report with metrics and recommendations</returns>
    Task<DocumentQualityReport> AnalyzeQualityAsync(string filePath, ChunkingOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates the quality of pre-generated document chunks.
    /// Useful for analyzing chunks from different processing strategies.
    /// </summary>
    /// <param name="chunks">Document chunks to evaluate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Quality metrics for the provided chunks</returns>
    Task<ChunkingQualityMetrics> EvaluateChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates QA benchmark dataset from document content.
    /// Essential for measuring RAG system performance and chunk answerability.
    /// </summary>
    /// <param name="filePath">Path to the document</param>
    /// <param name="questionCount">Number of questions to generate (default: 20)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>QA benchmark with generated questions and validation metrics</returns>
    Task<QABenchmark> GenerateQABenchmarkAsync(string filePath, int questionCount = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares different chunking strategies for the same document.
    /// Provides A/B testing capabilities for chunking optimization.
    /// </summary>
    /// <param name="filePath">Path to the document</param>
    /// <param name="strategies">Chunking strategy names to compare</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Comparative benchmark results</returns>
    Task<QualityBenchmarkResult> BenchmarkChunkingAsync(string filePath, string[] strategies, CancellationToken cancellationToken = default);
}
