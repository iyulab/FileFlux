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
/// <remarks>
/// The readers shipped here produce Markdown text (tables and headings inline) plus images; none of them fills
/// <see cref="RawContent.Tables"/> or <see cref="RawContent.Blocks"/>. The four switches that promised structured
/// table/block extraction (<c>ExtractTables</c>, <c>DetectBlockTypes</c>, <c>PreserveCoordinates</c>,
/// <c>MinTableConfidence</c>) and the <c>CustomOptions</c> bag were read by nothing and were removed in 0.25.0.
/// </remarks>
public class ExtractOptions
{
    /// <summary>
    /// Whether to extract images from document.
    /// </summary>
    /// <remarks>Applied by every reader (PDF, DOCX/XLSX, PPTX, HWP) through <c>ImageExtractionPolicy</c>: when false
    /// the result carries no images and no image hints. Before 0.24.2 no reader read this member.</remarks>
    public bool ExtractImages { get; set; } = true;

    /// <summary>
    /// Maximum image size in bytes (null for no limit).
    /// </summary>
    /// <remarks>An image whose data is larger is dropped from the result and named in <c>RawContent.Warnings</c>
    /// (every reader, same rule). Before 0.24.2 no reader read this member.</remarks>
    public int? MaxImageSize { get; set; }

    /// <summary>
    /// Page range to extract (null for all pages).
    /// </summary>
    public (int Start, int End)? PageRange { get; set; }

    /// <summary>
    /// Default extraction options.
    /// </summary>
    public static ExtractOptions Default => new();

    /// <summary>
    /// Minimal extraction (text only, no images). Every reader converts tables it finds to Markdown in the text;
    /// there is no separate table/block extraction to switch off (see the class remarks).
    /// </summary>
    public static ExtractOptions TextOnly => new()
    {
        ExtractImages = false
    };

    /// <summary>
    /// Full extraction (images included).
    /// </summary>
    public static ExtractOptions Full => new()
    {
        ExtractImages = true
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
