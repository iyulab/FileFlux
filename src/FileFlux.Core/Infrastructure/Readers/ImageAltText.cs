using System.Text.RegularExpressions;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// What a picture's alt text (the drawing's <c>descr</c>) actually holds. Office files carry three kinds of value
/// there: text an author wrote, the local file path the picture was inserted from (older Office versions record it,
/// and it names the author's user folder and whatever else lives on that path), and a description the application
/// generated and marked with a disclaimer. Only the first is document content.
/// </summary>
internal enum AltTextKind
{
    /// <summary>No alt text.</summary>
    None,

    /// <summary>Alt text written for the picture.</summary>
    Authored,

    /// <summary>
    /// An absolute file path, a <c>file:</c> URI, or a bare image file name (what Office writes when the picture is
    /// inserted from a file) — metadata of the machine and folder the file was made from, not a description.
    /// </summary>
    FilePath,

    /// <summary>An application-generated description, recognised by the disclaimer the application appends.</summary>
    Generated,
}

/// <summary>
/// Classifies a picture's alt text and keeps only authored text in a document's body and image captions. A file path
/// is dropped; a generated description is moved out of the body into <see cref="GeneratedProperty"/> on the image, so
/// a consumer that wants it can still read it as what it is.
/// </summary>
internal static partial class ImageAltText
{
    /// <summary>
    /// <see cref="ImageInfo.Properties"/> key holding an application-generated description (disclaimer removed).
    /// </summary>
    public const string GeneratedProperty = "generated_alt_text";

    /// <summary>
    /// The older endings Office appended to a description it generated. The current disclaimer is matched as a family by
    /// <see cref="AiDisclaimer"/>: Office ships more than one localization of the same sentence (Korean has «…올바르지 않을 수
    /// 있습니다» and «…부정확할 수 있습니다»), so an exact-sentence list keeps missing the next one.
    /// </summary>
    private static readonly string[] GeneratedMarkers =
    [
        "Description automatically generated",
        "자동 생성된 설명",
    ];

    /// <summary>
    /// The kind of <paramref name="alt"/> and the text to keep: the trimmed text with whitespace runs collapsed to one
    /// space for <see cref="AltTextKind.Authored"/>, the description without its marker for
    /// <see cref="AltTextKind.Generated"/>, and <c>null</c> otherwise.
    /// </summary>
    public static (AltTextKind Kind, string? Text) Classify(string? alt)
    {
        if (string.IsNullOrWhiteSpace(alt))
            return (AltTextKind.None, null);

        var text = WhitespaceRun().Replace(alt, " ").Trim();
        if (FilePathShape().IsMatch(text) || ImageFileName().IsMatch(text))
            return (AltTextKind.FilePath, null);

        var disclaimer = AiDisclaimer().Match(text);
        if (disclaimer.Success)
        {
            var description = text[..disclaimer.Index].Trim().TrimEnd('.', ',', ';', ':', '-', ' ').Trim();
            return (AltTextKind.Generated, description.Length == 0 ? null : description);
        }

        foreach (var marker in GeneratedMarkers)
        {
            var body = text.TrimEnd('.', ' ');
            var end = marker.TrimEnd('.', ' ');
            if (body.EndsWith(end, StringComparison.OrdinalIgnoreCase))
            {
                var description = body[..^end.Length].Trim().TrimEnd('.', ',', ';', ':', '-', ' ').Trim();
                return (AltTextKind.Generated, description.Length == 0 ? null : description);
            }
        }

        return (AltTextKind.Authored, text);
    }

    /// <summary>
    /// Sets <paramref name="image"/>'s caption from <paramref name="alt"/>: authored text becomes the caption, a
    /// generated description goes to <see cref="GeneratedProperty"/>, a file path is dropped.
    /// </summary>
    public static void Attach(ImageInfo image, string? alt)
    {
        var (kind, text) = Classify(alt);
        image.Caption = kind == AltTextKind.Authored ? text : null;
        if (kind == AltTextKind.Generated && text is not null)
            image.Properties[GeneratedProperty] = text;
    }

    /// <summary>
    /// Rewrites every Markdown image <c>![alt](target)</c> so its alt is the authored text on one line, or empty when
    /// the alt is a file path or a generated description. Alt text spanning several lines (which breaks the image
    /// syntax and leaves its tail as a paragraph) is joined first.
    /// </summary>
    public static string CleanMarkdown(string markdown)
    {
        if (string.IsNullOrEmpty(markdown) || !markdown.Contains("![", StringComparison.Ordinal))
            return markdown;

        return MarkdownImage().Replace(markdown, match =>
        {
            // Classify the text the alt stands for: Markdown escapes a backslash as a doubled one, so in the body a UNC path
            // starts with four backslashes and would not look like one. An authored alt keeps its escaped form, collapsed to one line.
            var escaped = match.Groups["alt"].Value;
            var (kind, _) = Classify(MarkdownEscape().Replace(escaped, "$1"));
            var alt = kind == AltTextKind.Authored ? WhitespaceRun().Replace(escaped, " ").Trim() : string.Empty;
            return $"![{alt}]({match.Groups["target"].Value})";
        });
    }

    // Drive-letter and UNC paths, file: URIs, and absolute POSIX paths under a user or volume root.
    [GeneratedRegex(@"^(?:[A-Za-z]:[\\/]|\\\\[^\\/\s]|file:|/(?:Users|home|Volumes|mnt|private|tmp|var)/|~[\\/])", RegexOptions.IgnoreCase)]
    private static partial Regex FilePathShape();

    // One token ending in an image file extension: "back.png", "IMG_1234.JPG".
    [GeneratedRegex(@"^[^\s\\/]+\.(?:png|jpe?g|gif|bmp|tiff?|webp|emf|wmf|svg|wdp|jxr|heic)$", RegexOptions.IgnoreCase)]
    private static partial Regex ImageFileName();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    // The disclaimer Office appends to a generated description, as a family: «AI-generated content may be <word(s)>.» and
    // «AI가 생성한 콘텐츠는 <어구> 수 있습니다.», at the end of the text.
    [GeneratedRegex(@"(?:AI-generated content may be [\p{L} ]{1,40}|AI가 생성한 콘텐츠는 [\p{L} ]{1,40}수 있습니다)\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex AiDisclaimer();

    // A Markdown backslash escape of an ASCII punctuation character.
    [GeneratedRegex(@"\\([!-/:-@\[-`{-~])")]
    private static partial Regex MarkdownEscape();

    // The alt may span lines (no ']' inside); the target has no whitespace or ')'.
    [GeneratedRegex(@"!\[(?<alt>[^\]]*)\]\((?<target>[^)\s]*)\)")]
    private static partial Regex MarkdownImage();
}
