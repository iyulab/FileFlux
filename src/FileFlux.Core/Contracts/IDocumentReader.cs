namespace FileFlux.Core;

/// <summary>
/// Document reader interface - Stage 0: Read (file parsing).
/// Parses document structure without content extraction.
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
    /// Stage 0: Read - Parses document and returns structural information.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed document structure.</returns>
    Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stage 0: Read - Parses document from stream.
    /// </summary>
    /// <param name="stream">Document stream.</param>
    /// <param name="fileName">Original file name (for extension detection).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed document structure.</returns>
    Task<ReadResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stage 1: Extract - Extracts raw content from parsed document (no markdown conversion).
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="options">Extraction options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted raw content with structured data.</returns>
    Task<RawContent> ExtractAsync(string filePath, ExtractOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stage 1: Extract - Extracts raw content from stream (no markdown conversion).
    /// </summary>
    /// <param name="stream">Document stream.</param>
    /// <param name="fileName">Original file name (for extension detection).</param>
    /// <param name="options">Extraction options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted raw content with structured data.</returns>
    Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for content extraction stage.
/// </summary>
public class ExtractOptions
{
    /// <summary>
    /// Whether to extract images from document.
    /// </summary>
    public bool ExtractImages { get; set; } = true;

    /// <summary>
    /// Whether to detect and extract tables.
    /// </summary>
    public bool ExtractTables { get; set; } = true;

    /// <summary>
    /// Whether to preserve coordinate/position information.
    /// </summary>
    public bool PreserveCoordinates { get; set; } = true;

    /// <summary>
    /// Whether to detect text block types (heading, list, etc.).
    /// </summary>
    public bool DetectBlockTypes { get; set; } = true;

    /// <summary>
    /// Maximum image size in bytes (null for no limit).
    /// </summary>
    public int? MaxImageSize { get; set; }

    /// <summary>
    /// Minimum table confidence threshold (0.0 - 1.0).
    /// Tables below this threshold will have PlainTextFallback set.
    /// </summary>
    public double MinTableConfidence { get; set; } = 0.5;

    /// <summary>
    /// Page range to extract (null for all pages).
    /// </summary>
    public (int Start, int End)? PageRange { get; set; }

    /// <summary>
    /// Additional extraction options.
    /// </summary>
    public Dictionary<string, object> CustomOptions { get; set; } = [];

    /// <summary>
    /// Default extraction options.
    /// </summary>
    public static ExtractOptions Default => new();

    /// <summary>
    /// Minimal extraction (text only, no images/tables).
    /// </summary>
    public static ExtractOptions TextOnly => new()
    {
        ExtractImages = false,
        ExtractTables = false,
        PreserveCoordinates = false,
        DetectBlockTypes = false
    };

    /// <summary>
    /// Full extraction with all features enabled.
    /// </summary>
    public static ExtractOptions Full => new()
    {
        ExtractImages = true,
        ExtractTables = true,
        PreserveCoordinates = true,
        DetectBlockTypes = true
    };
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
