namespace FileFlux.Core;

/// <summary>
/// Document reader interface - focuses on pure text extraction.
/// No LLM involvement, only file format-specific text extraction.
/// </summary>
public interface IDocumentReader
{
    /// <summary>
    /// Gets the list of supported file extensions.
    /// </summary>
    IEnumerable<string> SupportedExtensions { get; }

    /// <summary>
    /// Gets the reader type identifier (for logging/debugging).
    /// </summary>
    string ReaderType { get; }

    /// <summary>
    /// Checks if this reader can read the given file.
    /// </summary>
    /// <param name="fileName">File name to check.</param>
    /// <returns>True if the file can be read by this reader.</returns>
    bool CanRead(string fileName);

    /// <summary>
    /// Extracts raw text from a file.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted raw text content.</returns>
    Task<RawContent> ExtractAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts raw text from a stream.
    /// </summary>
    /// <param name="stream">Document stream.</param>
    /// <param name="fileName">Original file name (for extension detection).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted raw text content.</returns>
    Task<RawContent> ExtractAsync(Stream stream, string fileName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Factory for creating document readers.
/// </summary>
public interface IDocumentReaderFactory
{
    /// <summary>
    /// Gets a reader capable of reading the specified file.
    /// </summary>
    /// <param name="fileName">File name to get a reader for.</param>
    /// <returns>A document reader, or null if no suitable reader is found.</returns>
    IDocumentReader? GetReader(string fileName);

    /// <summary>
    /// Gets all registered readers.
    /// </summary>
    /// <returns>Collection of all available readers.</returns>
    IEnumerable<IDocumentReader> GetAllReaders();

    /// <summary>
    /// Checks if any reader can handle the specified file.
    /// </summary>
    /// <param name="fileName">File name to check.</param>
    /// <returns>True if a suitable reader exists.</returns>
    bool CanRead(string fileName);
}
