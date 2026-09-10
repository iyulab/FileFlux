using System.Buffers.Binary;
using System.Text;
using AwesomeAssertions;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Recognizing a password-protected Office document before a reader is chosen for it.
/// </summary>
/// <remarks>
/// <para>
/// Reported from a production upload: two <c>.xlsx</c> files failed with
/// <c>"Neither stream 'Workbook' nor 'Book' was found in file"</c>. Neither was damaged and neither
/// was a legacy workbook — both were encrypted OOXML documents, which on disk are compound files
/// holding <c>EncryptionInfo</c>/<c>EncryptedPackage</c>. Because the failure looked generic, the
/// consumer retried each file three times, and no retry can supply a password.
/// </para>
/// <para>
/// The containers here are built byte by byte rather than checked in as sample files: an encrypted
/// workbook cannot be committed to an OSS repository without also committing whatever it contains,
/// and the property under test is the container layout, which is exactly what a constructed one
/// pins.
/// </para>
/// </remarks>
public class CompoundFileEncryptionTests
{
    [Fact]
    public void AContainerHoldingEncryptedPackage_IsRecognizedAsEncrypted()
    {
        var container = CompoundFile(["Root Entry", "EncryptedPackage", "DataSpaces"]);

        CompoundFileEncryption.IsEncryptedDocument(container).Should().BeTrue();
    }

    [Fact]
    public void AContainerHoldingEncryptionInfo_IsRecognizedAsEncrypted()
    {
        // Either stream identifies it; a reader must not depend on both being enumerated.
        var container = CompoundFile(["Root Entry", "EncryptionInfo"]);

        CompoundFileEncryption.IsEncryptedDocument(container).Should().BeTrue();
    }

    [Fact]
    public void ALegacyWorkbook_IsNotEncrypted()
    {
        // The case that must keep working: a real .xls routed to the legacy reader as before.
        var container = CompoundFile(["Root Entry", "Workbook", "SummaryInformation"]);

        CompoundFileEncryption.IsEncryptedDocument(container).Should().BeFalse();
    }

    [Fact]
    public void AnOoxmlPackage_IsNotEncrypted()
    {
        // A normal .xlsx is a zip and never reaches the compound-file path at all.
        var zip = new byte[512];
        zip[0] = 0x50; zip[1] = 0x4B; zip[2] = 0x03; zip[3] = 0x04;

        CompoundFileEncryption.IsEncryptedDocument(zip).Should().BeFalse();
    }

    [Fact]
    public void ContentThatIsNotAContainer_IsNotEncrypted()
    {
        CompoundFileEncryption.IsEncryptedDocument(Encoding.UTF8.GetBytes("not a document at all"))
            .Should().BeFalse();
    }

    [Fact]
    public void TheNamesAreReadFromTheDirectory_NotFoundAnywhereInTheFile()
    {
        // A scan for the byte sequence would call this encrypted. It is an ordinary workbook whose
        // cell data happens to contain the word - the difference between a probe and a guess.
        var container = CompoundFile(
            ["Root Entry", "Workbook"],
            trailingData: Encoding.Unicode.GetBytes("EncryptedPackage"));

        CompoundFileEncryption.IsEncryptedDocument(container).Should().BeFalse();
    }

    [Fact]
    public void ATruncatedContainer_IsNotReportedAsEncrypted()
    {
        // A probe run before parsing must not turn a damaged file into a different diagnosis: the
        // reader still gets to report truncation in its own words.
        var container = CompoundFile(["Root Entry", "EncryptedPackage"]);

        CompoundFileEncryption.IsEncryptedDocument(container.AsSpan(0, 700).ToArray())
            .Should().BeFalse();
    }

    [Fact]
    public void ADirectoryChainThatLoops_Terminates()
    {
        // A hostile or corrupt FAT can describe a cycle. The probe runs before any parse, so it must
        // not be able to hang the caller.
        var container = CompoundFile(["Root Entry", "Workbook"]);
        // Point the directory sector's FAT entry back at itself.
        BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(SectorOffset(0) + (1 * 4)), 1);

        var act = () => CompoundFileEncryption.IsEncryptedDocument(container);

        act.Should().NotThrow();
    }

    // ---- container construction -------------------------------------------------------------
    //
    // Version-3 layout: 512-byte header, then 512-byte sectors. Sector 0 holds the FAT, sector 1 the
    // directory. Sector id N begins at (N + 1) * 512, since the header occupies the first one.

    private const int SectorSize = 512;
    private const int EntrySize = 128;
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FatSectorMarker = 0xFFFFFFFD;

    private static int SectorOffset(uint sector) => (int)((sector + 1) * SectorSize);

    internal static byte[] CompoundFile(string[] entryNames, byte[]? trailingData = null)
    {
        var sectorsNeeded = 2; // FAT + directory
        var extra = trailingData?.Length ?? 0;
        var bytes = new byte[SectorSize + (sectorsNeeded * SectorSize) + extra];

        // Header
        ReadOnlySpan<byte> magic = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        magic.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x1E), 9);   // 1 << 9 == 512
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x2C), 1);   // one FAT sector
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x30), 1);   // directory at sector 1
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x4C), 0);   // FAT itself at sector 0

        // FAT: sector 0 is the FAT, sector 1 ends the directory chain.
        var fat = bytes.AsSpan(SectorOffset(0), SectorSize);
        BinaryPrimitives.WriteUInt32LittleEndian(fat, FatSectorMarker);
        BinaryPrimitives.WriteUInt32LittleEndian(fat[4..], EndOfChain);

        // Directory entries
        var directory = bytes.AsSpan(SectorOffset(1), SectorSize);
        for (var i = 0; i < entryNames.Length && ((i + 1) * EntrySize) <= SectorSize; i++)
        {
            var entry = directory.Slice(i * EntrySize, EntrySize);
            var name = Encoding.Unicode.GetBytes(entryNames[i]);
            name.CopyTo(entry);
            // Declared length counts the terminator, as the format specifies.
            BinaryPrimitives.WriteUInt16LittleEndian(entry[0x40..], (ushort)(name.Length + 2));
        }

        trailingData?.CopyTo(bytes, bytes.Length - extra);
        return bytes;
    }
}

/// <summary>
/// The reader's own behaviour on an encrypted workbook — the part a consumer sees.
/// </summary>
public class EncryptedWorkbookExtractionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fileflux-enc-" + Guid.NewGuid().ToString("N"));

    public EncryptedWorkbookExtractionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort temp cleanup */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AnEncryptedWorkbook_FailsAsEncrypted_NotAsAMissingWorkbookStream()
    {
        // The reported message named a stream, which reads as a damaged file. It is not damaged:
        // it opens in Excel with the password.
        var path = Path.Combine(_dir, "quotation.xlsx");
        await File.WriteAllBytesAsync(
            path,
            EncryptedContainer(),
            TestContext.Current.CancellationToken);

        var act = async () => await new FileFlux.Core.Infrastructure.Readers.ExcelDocumentReader()
            .ExtractAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        var ex = (await act.Should().ThrowAsync<FileFlux.Core.EncryptedDocumentException>()).Which;

        ex.IsPermanent.Should().BeTrue(
            "no number of retries supplies a password - the consumer retried each file three times");
        ex.Message.Should().NotContain("Workbook",
            "naming the stream a legacy reader wanted is what sent the investigation after corruption");
    }

    private static byte[] EncryptedContainer() =>
        CompoundFileEncryptionTests.CompoundFile(["Root Entry", "EncryptionInfo", "EncryptedPackage"]);
}
