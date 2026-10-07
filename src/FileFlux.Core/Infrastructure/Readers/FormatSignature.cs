using System.IO.Compression;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Identifies a document format from its content, as the extension of the reader that parses it.
/// </summary>
/// <remarks>
/// <para>
/// A file's name is a claim, not a fact. A browser's "Save as PDF" that kept a <c>.docx</c> name, a
/// renamed download, or a mail attachment with a mangled extension reaches the reader its name
/// selects and fails there, although the content is a format another registered reader parses.
/// </para>
/// <para>
/// Only formats that can be told apart without guessing are detected: a PDF by its header, the
/// three OOXML packages by the part directory the package carries (<c>word/</c>, <c>xl/</c>,
/// <c>ppt/</c>), and the compound-file formats by the streams the container holds (HWP 5 and the
/// Office 97-2003 document, workbook and presentation). <see cref="ContainerSignature"/> stops at
/// the container because neither ZIP nor a compound file alone distinguishes a spreadsheet from a
/// presentation; the entries do. Anything else — plain text, HTML, an encrypted Office document, a
/// damaged container — returns <see langword="null"/>, and the declared name stays in charge. A
/// wrong route would be worse than the reader's own diagnosis.
/// </para>
/// </remarks>
public static class FormatSignature
{
    private static ReadOnlySpan<byte> PdfMagic => "%PDF-"u8;

    /// <summary>
    /// Classifies a byte prefix. Only formats decidable from the prefix alone are reported (PDF);
    /// an OOXML package needs its entries — use <see cref="DetectFile"/>, <see cref="DetectStream"/>,
    /// or <see cref="DetectBytes"/>.
    /// </summary>
    /// <returns>The canonical extension (for example <c>.pdf</c>), or <see langword="null"/>.</returns>
    public static string? Detect(ReadOnlySpan<byte> prefix)
        => prefix.StartsWith(PdfMagic) ? ".pdf" : null;

    /// <summary>Classifies a file on disk. Unreadable files classify as <see langword="null"/>.</summary>
    public static string? DetectFile(string filePath)
    {
        try
        {
            using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
            return DetectSeekable(stream);
        }
        catch (IOException)
        {
            // A probe failure must never turn a readable document into an error of its own; the
            // declared name keeps choosing, exactly as before.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Classifies a seekable stream, restoring its position afterwards. A stream that cannot seek
    /// classifies as <see langword="null"/> rather than consuming bytes the reader still needs.
    /// </summary>
    public static string? DetectStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
            return null;

        var origin = stream.Position;
        try
        {
            return DetectSeekable(stream);
        }
        catch (IOException)
        {
            return null;
        }
        finally
        {
            stream.Position = origin;
        }
    }

    /// <summary>Classifies an in-memory document.</summary>
    public static string? DetectBytes(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var stream = new MemoryStream(content, writable: false);
        return DetectSeekable(stream);
    }

    private static string? DetectSeekable(Stream stream)
    {
        Span<byte> prefix = stackalloc byte[ContainerSignature.ProbeLength];
        var read = stream.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false);
        prefix = prefix[..read];

        if (Detect(prefix) is { } byPrefix)
            return byPrefix;

        switch (ContainerSignature.Detect(prefix))
        {
            case OfficeContainer.Zip:
                stream.Position = 0;
                return DetectOfficePackage(stream);

            case OfficeContainer.CompoundFile:
                // The directory can sit anywhere in the container, so the whole file is read — as the readers
                // that parse a compound file do anyway.
                stream.Position = 0;
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    return DetectCompoundFile(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
                }

            default:
                return null;
        }
    }

    /// <summary>
    /// A compound file holds several formats, told apart by their streams: an HWP 5 document (<c>FileHeader</c> with
    /// <c>BodyText</c> or <c>DocInfo</c>), a legacy workbook (<c>Workbook</c> or <c>Book</c>), a Word 97-2003
    /// document (<c>WordDocument</c>) and a PowerPoint 97-2003 presentation (<c>PowerPoint Document</c>). An
    /// encrypted OOXML document is a compound file too, and is left to the declared reader, which reports it as
    /// encrypted.
    /// </summary>
    private static string? DetectCompoundFile(ReadOnlySpan<byte> content)
    {
        var names = CompoundFileDirectory.EnumerateNames(content);
        bool Has(string name) => names.Contains(name, StringComparer.Ordinal);

        if (Has("EncryptedPackage") || Has("EncryptionInfo"))
            return null;
        if (Has("FileHeader") && (Has("BodyText") || Has("DocInfo")))
            return ".hwp";
        if (Has("Workbook") || Has("Book"))
            return ".xls";
        if (Has("WordDocument"))
            return ".doc";
        if (Has("PowerPoint Document"))
            return ".ppt";
        return null;
    }

    private static string? DetectOfficePackage(Stream stream)
    {
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

            // HWPX (OWPML) names itself in a stored "mimetype" entry, as EPUB and ODF do.
            if (archive.GetEntry("mimetype") is { Length: < 256 } mimetype)
            {
                using var reader = new StreamReader(mimetype.Open());
                if (reader.ReadToEnd().Trim() == "application/hwp+zip")
                    return ".hwpx";
            }

            var hasContentTypes = false;
            string? part = null;
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName;
                if (name.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
                    hasContentTypes = true;
                else if (name.StartsWith("word/", StringComparison.OrdinalIgnoreCase))
                    part = Merge(part, ".docx");
                else if (name.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                    part = Merge(part, ".xlsx");
                else if (name.StartsWith("ppt/", StringComparison.OrdinalIgnoreCase))
                    part = Merge(part, ".pptx");
            }

            // Without the content-types part it is a ZIP that merely contains such a folder, not an
            // OPC package; two main-part folders is ambiguous. Neither is guessed at.
            return hasContentTypes && part is not "" ? part : null;
        }
        catch (InvalidDataException)
        {
            // A damaged package keeps its declared reader, whose diagnosis names the damage.
            return null;
        }
    }

    // "" marks a package carrying more than one main-part folder.
    private static string Merge(string? current, string found)
        => current is null || current == found ? found : "";

    /// <summary>
    /// Records on <paramref name="raw"/> that its name claimed another format than the one its
    /// content was read as: a warning carrying <c>[extension_mismatch]</c>, and the claim itself as
    /// the <c>declared_extension</c> hint. <see cref="SourceFileInfo.Extension"/> already states what
    /// was parsed. Does nothing when nothing was detected or the name was right.
    /// </summary>
    /// <param name="raw">The extraction result to annotate.</param>
    /// <param name="declaredName">The file name (or bare extension) the caller supplied.</param>
    /// <param name="detectedExtension">What the content was recognised as, or <see langword="null"/>.</param>
    public static void NoteDeclaredMismatch(RawContent raw, string declaredName, string? detectedExtension)
    {
        ArgumentNullException.ThrowIfNull(raw);

        if (string.IsNullOrEmpty(detectedExtension))
            return;

        var declared = Path.GetExtension(declaredName).ToLowerInvariant();
        if (string.Equals(declared, detectedExtension, StringComparison.OrdinalIgnoreCase))
            return;

        raw.Hints["declared_extension"] = declared;
        raw.Warnings.Add(
            (string.IsNullOrEmpty(declared) ? "The file name declares no format" : $"The declared extension {declared} does not match the content")
            + $", which is {Describe(detectedExtension)}; it was read as {detectedExtension}. [extension_mismatch]");
    }

    /// <summary>
    /// Fails when the content is a format another reader parses. The Office parser reads whatever
    /// container it is handed, so a workbook under a <c>.docx</c> name would otherwise come back from
    /// the Word reader as a Word document; the reader factory routes by content, and a caller holding
    /// a reader directly gets the mismatch named instead.
    /// </summary>
    /// <param name="detectedExtension">What the content was recognised as, or <see langword="null"/>.</param>
    /// <param name="supportedExtensions">The formats the calling reader parses.</param>
    /// <param name="fileName">The document, as the caller named it.</param>
    /// <param name="failure">The leading text of the message, e.g. <c>Failed to extract Word document</c>.</param>
    internal static void ThrowIfAnotherReadersFormat(
        string? detectedExtension, IEnumerable<string> supportedExtensions, string fileName, string failure)
    {
        if (detectedExtension is null || supportedExtensions.Contains(detectedExtension, StringComparer.OrdinalIgnoreCase))
            return;

        throw new DocumentProcessingException(
            fileName,
            $"{failure}: the content is {Describe(detectedExtension)}, which this reader does not parse; it reads as " +
            $"{detectedExtension}. [extraction_failure_reason=container_mismatch] [detected_extension={detectedExtension}]");
    }

    /// <summary>The human name of a detected format, for messages.</summary>
    internal static string Describe(string extension) => extension switch
    {
        ".pdf" => "a PDF document",
        ".docx" => "a Word document (OOXML)",
        ".xlsx" => "an Excel workbook (OOXML)",
        ".pptx" => "a PowerPoint presentation (OOXML)",
        ".hwp" => "an HWP 5 document",
        ".hwpx" => "an HWPX document",
        ".xls" => "a legacy Excel workbook",
        ".doc" => "a legacy Word document",
        ".ppt" => "a legacy PowerPoint presentation",
        _ => $"a {extension} document"
    };

    /// <summary>
    /// Whether a reader that declares <paramref name="supportedExtensions"/> may parse content named
    /// <paramref name="fileName"/>: the name claims one of them, or the content is one of them.
    /// </summary>
    /// <remarks>
    /// Readers guard their entry points with this rather than with the name alone, so a caller that
    /// selected the reader by content — the reader factory, or a consumer holding its own reader —
    /// is not refused over the name it was handed.
    /// </remarks>
    internal static bool Accepts(IEnumerable<string> supportedExtensions, string fileName, Func<string?> detect)
    {
        var declared = Path.GetExtension(fileName);
        if (!string.IsNullOrEmpty(declared) && supportedExtensions.Contains(declared, StringComparer.OrdinalIgnoreCase))
            return true;

        return detect() is { } detected && supportedExtensions.Contains(detected, StringComparer.OrdinalIgnoreCase);
    }
}
