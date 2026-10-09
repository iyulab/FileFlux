namespace FileFlux.Core;

/// <summary>
/// Image information with optional binary data
/// </summary>
public class ImageInfo
{
    /// <summary>
    /// Unique image identifier (e.g., "img_001")
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Image caption or alt text
    /// </summary>
    public string? Caption { get; set; }

    /// <summary>
    /// Character position in extracted text
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// The page the image is drawn on (1-based, the numbering of the document's page spans), when the reader knows
    /// it - a PDF page, a presentation's slide. For an image shown on several pages, the first; see
    /// <see cref="PageNumbers"/>. Null for formats without pages and when the reader cannot tell.
    /// </summary>
    public int? PageNumber { get; set; }

    /// <summary>
    /// Every page that shows the image, ascending - a picture a presentation reuses on several slides lists each slide.
    /// When the reader knows a single page this is <see cref="PageNumber"/> alone; empty when the page is unknown.
    /// Setting it sets <see cref="PageNumber"/> to its first element.
    /// </summary>
    public IReadOnlyList<int> PageNumbers
    {
        get => _pageNumbers ?? (PageNumber is { } page ? [page] : []);
        set
        {
            _pageNumbers = value is { Count: > 0 } ? value : null;
            PageNumber = _pageNumbers?[0];
        }
    }

    private IReadOnlyList<int>? _pageNumbers;

    /// <summary>
    /// MIME type (e.g., "image/png", "image/jpeg")
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// Image binary data (optional, for embedded/base64 images)
    /// </summary>
    public byte[]? Data { get; set; }

    /// <summary>
    /// Source reference: external URL or "embedded:{id}" for extracted images
    /// </summary>
    public string? SourceUrl { get; set; }

    /// <summary>
    /// Original data size in bytes (before extraction)
    /// </summary>
    public long OriginalSize { get; set; }

    /// <summary>
    /// The image is on a page whose text was replaced by a read of the rendered page
    /// (<see cref="ExtractOptions.PageReading"/>): what it shows is already in the text, so describing it again would
    /// repeat that page — a full-page scan is the common case.
    /// </summary>
    public bool ReadAsPage { get; set; }

    /// <summary>
    /// Set when the image is a rendering of a whole page or slide rather than a picture the document embeds
    /// (<see cref="ExtractOptions.SlideRendering"/>): its size and what the renderer could not paint. Null for embedded images.
    /// </summary>
    public RenderedPage? RenderedPage { get; set; }

    /// <summary>
    /// Additional properties
    /// </summary>
    public Dictionary<string, object> Properties { get; } = new();
}
