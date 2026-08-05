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
