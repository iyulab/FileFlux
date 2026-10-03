namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Reads the stream and storage names of an OLE2 / compound-file container's directory.
/// </summary>
/// <remarks>
/// The directory, not a scan for the names anywhere in the file, is what identifies a container's contents: a byte
/// sequence that happens to appear inside cell data or document text would otherwise misidentify the file. Used to
/// recognise an encrypted Office document (<see cref="CompoundFileEncryption"/>) and to tell a legacy workbook or an
/// HWP 5 document from other compound files (<see cref="FormatSignature"/>).
/// </remarks>
internal static class CompoundFileDirectory
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
    /// Reads the names of every directory entry in the container. Returns nothing when the content is
    /// not a readable compound file: this is a probe, so anything it cannot understand is simply not
    /// a positive identification.
    /// </summary>
    internal static List<string> EnumerateNames(ReadOnlySpan<byte> content)
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
