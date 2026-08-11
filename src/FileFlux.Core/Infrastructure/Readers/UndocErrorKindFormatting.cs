using Undoc;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Shared diagnostic formatting for <see cref="UndocException"/>, used by the three readers that
/// delegate to Undoc (Excel, Word, PowerPoint). Mirrors the <c>extraction_error_kind</c> channel
/// <see cref="PdfDocumentReader"/> already exposes for Unpdf — same key, same tail-append shape —
/// so a consumer classifies a failure on one vocabulary regardless of which native reader produced
/// it. Undoc and Unpdf assign their error-kind numbers independently, so the two enums are not
/// interchangeable; only the message-channel convention is shared.
/// </summary>
internal static class UndocErrorKindFormatting
{
    /// <summary>
    /// Diagnostic key naming the Undoc error kind behind a failed extraction. The exception path
    /// has no <see cref="RawContent"/> to hang hints on and consumers persist only
    /// <see cref="Exception.Message"/>, so the kind travels as a producer-emitted
    /// <c>key=value</c> token inside the message.
    /// </summary>
    internal const string ErrorKindKey = "extraction_error_kind";

    /// <summary>
    /// Formats an Undoc error kind for diagnostics. Deliberately <see cref="Enum.ToString()"/> and
    /// not <see cref="Enum.GetName(Type, object)"/>: Undoc's ABI assigns new reasons new numbers
    /// and never reuses old ones, so a value minted by a newer native build must round-trip as its
    /// number rather than collapse to null.
    /// </summary>
    internal static string FormatErrorKind(UndocErrorKind kind) => kind.ToString();

    /// <summary>Appends the error-kind token to a failure message, tail-anchored.</summary>
    internal static string WithErrorKind(string message, UndocErrorKind kind)
        => $"{message} [{ErrorKindKey}={FormatErrorKind(kind)}]";
}
