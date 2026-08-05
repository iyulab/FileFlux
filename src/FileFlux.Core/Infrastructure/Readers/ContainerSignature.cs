namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// The container an Office file actually is, read from its leading bytes.
/// </summary>
public enum OfficeContainer
{
    /// <summary>Neither an OOXML package nor a compound file — or too short to tell.</summary>
    Unknown = 0,

    /// <summary>ZIP (<c>PK..</c>) — the OOXML package used by .xlsx / .docx / .pptx.</summary>
    Zip,

    /// <summary>OLE2 / Compound File Binary — the legacy container used by .xls / .doc / .ppt.</summary>
    CompoundFile
}

/// <summary>
/// Identifies an Office container from its magic bytes.
/// </summary>
/// <remarks>
/// <para>
/// A file's declared extension is a claim, not a fact, and renaming across container generations is
/// routine in the field — a legacy <c>.xls</c> saved or copied as <c>.xlsx</c> reaches a reader that
/// expects a ZIP package and fails with "could not find EOCD", which reads as a corrupt file when the
/// file is perfectly valid and a reader for it already exists.
/// </para>
/// <para>
/// Only the container is inferred here, never the document type: ZIP alone does not distinguish a
/// spreadsheet from a presentation, and pretending otherwise would trade one wrong guess for another.
/// Deciding what to do with a mismatch belongs to the reader that knows both formats.
/// </para>
/// </remarks>
public static class ContainerSignature
{
    private static ReadOnlySpan<byte> ZipMagic => [0x50, 0x4B, 0x03, 0x04];

    private static ReadOnlySpan<byte> CompoundFileMagic =>
        [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    /// <summary>Bytes needed to classify a container.</summary>
    public const int ProbeLength = 8;

    /// <summary>Classifies the container from a byte prefix.</summary>
    public static OfficeContainer Detect(ReadOnlySpan<byte> prefix)
    {
        if (prefix.StartsWith(CompoundFileMagic))
            return OfficeContainer.CompoundFile;

        // Checked after CFB because the ZIP magic is shorter; an empty-archive marker (PK\x05\x06)
        // or a spanned one (PK\x07\x08) is not a readable package, so only the local-file header
        // counts as ZIP here.
        if (prefix.StartsWith(ZipMagic))
            return OfficeContainer.Zip;

        return OfficeContainer.Unknown;
    }

    /// <summary>Classifies the container of a file on disk. Unreadable files classify as Unknown.</summary>
    public static OfficeContainer DetectFile(string filePath)
    {
        try
        {
            using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read, ProbeLength, FileOptions.SequentialScan);
            Span<byte> prefix = stackalloc byte[ProbeLength];
            var read = stream.ReadAtLeast(prefix, ProbeLength, throwOnEndOfStream: false);
            return Detect(prefix[..read]);
        }
        catch (IOException)
        {
            // Reporting Unknown lets the caller carry on to the reader it would have chosen anyway,
            // so a probe failure never turns a readable document into an error of its own.
            return OfficeContainer.Unknown;
        }
        catch (UnauthorizedAccessException)
        {
            return OfficeContainer.Unknown;
        }
    }

    /// <summary>
    /// Adds a container-mismatch note to a parser's failure message when the content is neither
    /// Office container, and returns the message unchanged otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A parser handed something that is not its container complains about its own format — "could
    /// not find EOCD" for a ZIP reader — which is accurate and misleading at once: it sends whoever
    /// reads it after data corruption, when the file is usually not a document at all. An error page
    /// or a placeholder saved under a document name is the common case.
    /// </para>
    /// <para>
    /// The token is the same <c>extraction_failure_reason</c> vocabulary the PDF reader already uses,
    /// so a consumer classifies on one channel rather than on prose. A damaged package still
    /// classifies as its own container and keeps the parser's diagnosis — truncated is not
    /// mislabelled, and conflating them would send the next investigation the wrong way again.
    /// </para>
    /// </remarks>
    /// <param name="message">The parser's own failure message, kept as the leading text.</param>
    /// <param name="actual">The container the content actually is.</param>
    /// <param name="acceptable">
    /// The containers this reader can actually handle. What counts as a mismatch differs per reader:
    /// the Excel readers accept both, because a workbook exists in both containers and they route
    /// between them, while Word and PowerPoint accept only the package — there is no legacy reader
    /// for them to hand a compound file to, so for them a compound file is the mismatch itself.
    /// </param>
    public static string AnnotateFailure(
        string message,
        OfficeContainer actual,
        params OfficeContainer[] acceptable)
    {
        if (Array.IndexOf(acceptable, actual) >= 0)
            return message;

        var described = actual switch
        {
            OfficeContainer.CompoundFile =>
                "The content is a legacy compound file (the .doc / .xls / .ppt container), not the " +
                "OOXML package its extension claims",
            OfficeContainer.Zip =>
                "The content is an OOXML package, not the container its extension claims",
            _ =>
                "The content is neither an OOXML package nor a compound file, so despite the " +
                "extension it is not a document this reader can parse"
        };

        return $"{message} {described}. [extraction_failure_reason=container_mismatch]";
    }

    /// <summary>
    /// Classifies the container of a seekable stream, restoring the position afterwards. A stream
    /// that cannot seek classifies as Unknown rather than consuming bytes the reader still needs.
    /// </summary>
    public static OfficeContainer DetectStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
            return OfficeContainer.Unknown;

        var origin = stream.Position;
        try
        {
            Span<byte> prefix = stackalloc byte[ProbeLength];
            var read = stream.ReadAtLeast(prefix, ProbeLength, throwOnEndOfStream: false);
            return Detect(prefix[..read]);
        }
        catch (IOException)
        {
            return OfficeContainer.Unknown;
        }
        finally
        {
            stream.Position = origin;
        }
    }
}
