namespace FileFlux.Core;

/// <summary>
/// Thrown when a document cannot be extracted because it is encrypted and no usable password was
/// supplied.
/// </summary>
/// <remarks>
/// <para>
/// This exists so a caller can branch. Before it, a password-protected workbook surfaced as a
/// generic processing failure whose message came from the legacy reader —
/// <c>"Neither stream 'Workbook' nor 'Book' was found"</c> — which reads as a damaged file and sent
/// investigations after data corruption. The file was not damaged; it opens with its password.
/// </para>
/// <para>
/// The second half matters as much as the diagnosis: this failure is <b>permanent for the given
/// input</b>. A caller that treats extraction failures as transient retried each such file three
/// times, and no number of attempts supplies a password. <see cref="IsPermanent"/> says so on the
/// exception rather than leaving the caller to infer it from prose — string-matching a message is
/// precisely the coupling a typed exception exists to remove.
/// </para>
/// </remarks>
public class EncryptedDocumentException : FileFluxException
{
    /// <summary>The document that is encrypted, when the caller supplied a name.</summary>
    public string? FileName { get; }

    /// <summary>
    /// Always true: the input cannot be extracted without a password, so retrying the same input
    /// cannot succeed. Present as a property so callers branch on a value rather than on the type
    /// alone, and so a future non-permanent sibling reads consistently.
    /// </summary>
    public bool IsPermanent { get; } = true;

    /// <summary>Creates the exception with the default message.</summary>
    public EncryptedDocumentException()
        : base("The document is encrypted (password-protected) and cannot be extracted without a password.")
    {
    }

    /// <summary>Creates the exception for a named document.</summary>
    /// <param name="fileName">The document that is encrypted.</param>
    public EncryptedDocumentException(string fileName)
        : base($"The document is encrypted (password-protected) and cannot be extracted without a " +
               $"password: {fileName}. [extraction_failure_reason=encrypted_document]")
    {
        FileName = fileName;
    }

    /// <summary>Creates the exception with an explicit message.</summary>
    public EncryptedDocumentException(string fileName, string message)
        : base(message)
    {
        FileName = fileName;
    }

    /// <summary>Creates the exception wrapping the failure that revealed the encryption.</summary>
    public EncryptedDocumentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
