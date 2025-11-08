using FileFlux.Domain;

namespace FileFlux.CLI.Output;

/// <summary>
/// Interface for output formatters
/// </summary>
public interface IOutputWriter
{
    /// <summary>
    /// Write chunks to output
    /// </summary>
    Task WriteAsync(IEnumerable<DocumentChunk> chunks, string outputPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Supported file extension
    /// </summary>
    string Extension { get; }
}
