namespace FileFlux.Core;

/// <summary>
/// Text block with position and style information.
/// Represents a unit of text extracted from document before markdown conversion.
/// </summary>
public class TextBlock
{
    /// <summary>
    /// Text content of the block.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Block type (paragraph, heading, list, etc.).
    /// </summary>
    public BlockType Type { get; set; } = BlockType.Paragraph;

    /// <summary>
    /// Page number where block appears (1-based).
    /// </summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// Bounding box location in document.
    /// </summary>
    public BoundingBox? Location { get; set; }

    /// <summary>
    /// Text style information.
    /// </summary>
    public TextStyle? Style { get; set; }

    /// <summary>
    /// Order in document (for sorting).
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Heading level (1-6) if Type is Heading.
    /// </summary>
    public int? HeadingLevel { get; set; }

    /// <summary>
    /// List item level (0-based indentation) if Type is ListItem.
    /// </summary>
    public int? ListLevel { get; set; }

    /// <summary>
    /// Whether list is ordered (numbered) or unordered (bullet).
    /// </summary>
    public bool? IsOrderedList { get; set; }

    /// <summary>
    /// Additional properties.
    /// </summary>
    public Dictionary<string, object> Props { get; set; } = [];

    /// <summary>
    /// Whether block has meaningful content.
    /// </summary>
    public bool HasContent => !string.IsNullOrWhiteSpace(Content);
}

/// <summary>
/// Block type enumeration.
/// </summary>
public enum BlockType
{
    /// <summary>
    /// Regular paragraph text.
    /// </summary>
    Paragraph,

    /// <summary>
    /// Heading (use HeadingLevel for specific level).
    /// </summary>
    Heading,

    /// <summary>
    /// List item (use ListLevel and IsOrderedList for details).
    /// </summary>
    ListItem,

    /// <summary>
    /// Code block or preformatted text.
    /// </summary>
    CodeBlock,

    /// <summary>
    /// Block quote.
    /// </summary>
    Quote,

    /// <summary>
    /// Page footer content.
    /// </summary>
    Footer,

    /// <summary>
    /// Page header content.
    /// </summary>
    Header,

    /// <summary>
    /// Caption for table or figure.
    /// </summary>
    Caption,

    /// <summary>
    /// Table of contents entry.
    /// </summary>
    TocEntry,

    /// <summary>
    /// Footnote or endnote.
    /// </summary>
    Note
}

/// <summary>
/// Text style information.
/// </summary>
public class TextStyle
{
    /// <summary>
    /// Font family name.
    /// </summary>
    public string? FontFamily { get; set; }

    /// <summary>
    /// Font size in points.
    /// </summary>
    public double? FontSize { get; set; }

    /// <summary>
    /// Whether text is bold.
    /// </summary>
    public bool IsBold { get; set; }

    /// <summary>
    /// Whether text is italic.
    /// </summary>
    public bool IsItalic { get; set; }

    /// <summary>
    /// Whether text is underlined.
    /// </summary>
    public bool IsUnderline { get; set; }

    /// <summary>
    /// Whether text is strikethrough.
    /// </summary>
    public bool IsStrikethrough { get; set; }

    /// <summary>
    /// Text color (hex format).
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Background color (hex format).
    /// </summary>
    public string? BackgroundColor { get; set; }

    /// <summary>
    /// Text alignment.
    /// </summary>
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;
}

/// <summary>
/// Text alignment enumeration.
/// </summary>
public enum TextAlignment
{
    Left,
    Center,
    Right,
    Justify
}
