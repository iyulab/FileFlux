namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Recognizes an encrypted Office document from the streams its compound-file container holds.
/// </summary>
/// <remarks>
/// <para>
/// A password-protected <c>.xlsx</c> is not an OOXML package on disk. It is an OLE2/compound file
/// wrapping the real package, holding <c>EncryptionInfo</c> and <c>EncryptedPackage</c> (usually
/// beside a <c>DataSpaces</c> storage) instead of the <c>Workbook</c>/<c>Book</c> streams of a binary
/// workbook. Treating it as a binary workbook by its magic bytes is correct as far as it goes — the
/// file really is a compound file — but the binary-workbook reader this was written for then reported
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
        foreach (var name in CompoundFileDirectory.EnumerateNames(content))
        {
            foreach (var marker in EncryptionStreams)
            {
                if (string.Equals(name, marker, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }
}
