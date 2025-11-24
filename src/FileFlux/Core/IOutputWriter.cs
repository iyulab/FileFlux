using FileFlux.Domain;

namespace FileFlux.Core;

/// <summary>
/// Interface for writing processing results to output
/// </summary>
public interface IOutputWriter
{
    /// <summary>
    /// Write extraction result to output directory
    /// </summary>
    /// <param name="result">Extraction result</param>
    /// <param name="outputDirectory">Output directory path</param>
    /// <param name="options">Output options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task WriteExtractionAsync(
        ExtractionResult result,
        string outputDirectory,
        OutputOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Write chunking result to output directory
    /// </summary>
    /// <param name="result">Chunking result</param>
    /// <param name="outputDirectory">Output directory path</param>
    /// <param name="options">Output options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task WriteChunkingAsync(
        ChunkingResult result,
        string outputDirectory,
        OutputOptions options,
        CancellationToken cancellationToken = default);
}
