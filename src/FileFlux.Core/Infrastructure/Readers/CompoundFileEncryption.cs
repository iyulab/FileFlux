namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Recognizes an encrypted Office document from the streams its compound-file container holds.
/// </summary>
/// <remarks>
/// <para>
/// A password-protected <c>.xlsx</c> is not an OOXML package on disk. It is an OLE2/compound file
/// wrapping the real package, holding <c>EncryptionInfo</c> and <c>EncryptedPackage</c> (usually
/// beside a <c>DataSpaces</c> storage) instead of the <c>Workbook</c>/<c>Book</c> streams a legacy
/// reader looks for. Dispatching it to that reader by its magic bytes is correct as far as it goes —
/// the file really is a compound file — but the reader then reports
/// <c>"Neither stream 'Workbook' nor 'Book' was found"</c>, which reads as a damaged file. It is not
/// damaged: it opens in Excel with the password. A precisely identifiable condition was being
/// reported as an unexplained one, and the caller retried it three times because nothing said the
/// failure was permanent.
/// </para>
/// <para>
/// The container's directory is enough to tell, before any parse is attempted, and the same shape
/// covers protected <c>.docx</c> and <c>.pptx</c>. This walks that directory rather than scanning for
/// the names anywhere in the file: a byte sequence that happens to appear inside cell data would
/// otherwise make an ordinary workbook look encrypted.
/// </para>
/// </remarks>
public static class CompoundFileEncryption
{
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FreeSector = 0xFFFFFFFF;

    /// <summary>Directory entries are fixed-size records, whatever the sector size.</summary>
    private const int DirectoryEntrySize = 128;

    /// <summary>Offset of the entry's name length, in bytes including the terminator.</summary>
    private const int NameLengthOffset = 0x40;

    /// <summary>
    /// A malformed FAT can describe a cycle. The walk is bounded rather than trusted: a probe run
    /// before parsing must not be able to hang the caller on a hostile file.
    /// </summary>
    private const int MaxDirectorySectors = 512;

    /// <summary>
    /// Fails with <see cref="FileFlux.Core.EncryptedDocumentException"/> when the content is an
    /// encrypted Office document, and does nothing otherwise.
    /// </summary>
    /// <remarks>
    /// Shared by every reader that can be handed a compound file rather than repeated in each: the
    /// condition and its remedy are the same for a workbook, a document and a presentation, and
    /// three copies of one rule is how two of them end up saying different things.
    /// </remarks>
    /// <param name="content">The file's bytes.</param>
    /// <param name="fileName">Named on the exception so a caller can say which file it was.</param>
    internal static void ThrowIfEncrypted(ReadOnlySpan<byte> content, string fileName)
    {
        if (IsEncryptedDocument(content))
            throw new FileFlux.Core.EncryptedDocumentException(fileName);
    }

    /// <summary>The stream names that identify an encrypted Office document.</summary>
    private static readonly string[] EncryptionStreams = ["EncryptedPackage", "EncryptionInfo"];

    /// <summary>
    /// Whether the compound-file content holds the streams of an encrypted Office document.
    /// Content that is not a compound file, or is too damaged to read a directory from, is not
    /// encrypted as far as this can tell — the caller carries on to the reader it would have chosen.
    /// </summary>
    public static bool IsEncryptedDocument(ReadOnlySpan<byte> content)
    {
        foreach (var name in EnumerateDirectoryNames(content))
        {
            foreach (var marker in EncryptionStreams)
            {
                if (string.Equals(name, marker, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads the names of every directory entry in the container. Returns nothing when the content is
    /// not a readable compound file: this is a probe, so anything it cannot understand is simply not
    /// a positive identification.
    /// </summary>
    private static List<string> EnumerateDirectoryNames(ReadOnlySpan<byte> content)
    {
        var names = new List<string>();

        if (ContainerSignature.Detect(content) != OfficeContainer.CompoundFile)
            return names;

        // Header: sector size is a power of two given as a shift, the first directory sector is a
        // sector id, and the first 109 FAT sector ids are inline. Anything beyond those 109 lives in
        // DIFAT sectors, which a directory this early in the file does not reach.
        if (content.Length < 512)
            return names;

        var sectorShift = ReadUInt16(content, 0x1E);
        if (sectorShift is < 7 or > 20)
            return names;

        var sectorSize = 1 << sectorShift;
        var firstDirectorySector = ReadUInt32(content, 0x30);
        var fatEntriesPerSector = sectorSize / 4;

        var sector = firstDirectorySector;
        var visited = 0;

        while (sector != EndOfChain && sector != FreeSector && visited < MaxDirectorySectors)
        {
            var offset = SectorOffset(sector, sectorSize);
            if (offset < 0 || offset + sectorSize > content.Length)
                break;

            var sectorSpan = content.Slice(offset, sectorSize);
            for (var entry = 0; entry + DirectoryEntrySize <= sectorSpan.Length; entry += DirectoryEntrySize)
            {
                var name = ReadEntryName(sectorSpan.Slice(entry, DirectoryEntrySize));
                if (name.Length > 0)
                    names.Add(name);
            }

            sector = NextSector(content, sector, sectorSize, fatEntriesPerSector);
            visited++;
        }

        return names;
    }

    /// <summary>Byte offset of a sector. The header occupies the first one, so ids are shifted by it.</summary>
    private static int SectorOffset(uint sector, int sectorSize)
    {
        var offset = ((long)sector + 1) * sectorSize;
        return offset > int.MaxValue ? -1 : (int)offset;
    }

    /// <summary>Follows the FAT one link. Returns end-of-chain when the entry cannot be read.</summary>
    private static uint NextSector(ReadOnlySpan<byte> content, uint sector, int sectorSize, int fatEntriesPerSector)
    {
        var fatIndex = (int)(sector / (uint)fatEntriesPerSector);
        if (fatIndex >= 109)
            return EndOfChain;

        var fatSector = ReadUInt32(content, 0x4C + (fatIndex * 4));
        if (fatSector is EndOfChain or FreeSector)
            return EndOfChain;

        var fatOffset = SectorOffset(fatSector, sectorSize);
        if (fatOffset < 0 || fatOffset + sectorSize > content.Length)
            return EndOfChain;

        var entryOffset = fatOffset + ((int)(sector % (uint)fatEntriesPerSector) * 4);
        return ReadUInt32(content, entryOffset);
    }

    /// <summary>
    /// The entry's name: UTF-16LE, with a declared length in bytes that includes the terminator.
    /// A length outside the fixed 64-byte field means the entry is unusable, which reads as no name.
    /// </summary>
    private static string ReadEntryName(ReadOnlySpan<byte> entry)
    {
        var nameLength = ReadUInt16(entry, NameLengthOffset);
        if (nameLength is < 4 or > 64 || nameLength % 2 != 0)
            return string.Empty;

        // Drop the trailing null, which the declared length counts.
        var characters = entry[..(nameLength - 2)];
        return System.Text.Encoding.Unicode.GetString(characters);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> content, int offset) =>
        offset + 2 <= content.Length
            ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(content[offset..])
            : (ushort)0;

    private static uint ReadUInt32(ReadOnlySpan<byte> content, int offset) =>
        offset + 4 <= content.Length
            ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(content[offset..])
            : EndOfChain;
}
