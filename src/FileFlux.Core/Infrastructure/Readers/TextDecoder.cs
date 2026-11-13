using System.Text;
using System.Text.RegularExpressions;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Turns the bytes of a text file into a string without losing characters. The readers used to hand the stream to a UTF-8
/// reader, whose default is to replace every byte it cannot decode with U+FFFD: a legacy Korean file saved as CP949 / EUC-KR
/// (older .txt, .md and web pages) came out as a run of replacement characters, with no error. The order is: a byte order
/// mark, then UTF-32 and UTF-16 saved without one (recognised by their NULs, see <see cref="TryDetectUtf32WithoutBom"/> and <see cref="TryDetectUtf16WithoutBom"/>), then strict UTF-8 (ASCII
/// is a subset), then the charset the document itself declares (HTML), then CP949.
/// </summary>
internal static class TextDecoder
{
    private static readonly Regex MetaCharset = new(
        @"<meta\s[^>]*?charset\s*=\s*[""']?\s*([A-Za-z0-9_\-:.]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    static TextDecoder() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>The decoded text and the name of the encoding used (<c>utf-8</c>, <c>utf-16le</c>, <c>cp949</c>, ...).</summary>
    /// <param name="bytes">The file content.</param>
    /// <param name="declaredCharsetFromContent">
    /// True to look for a <c>&lt;meta charset&gt;</c> declaration in the first bytes (HTML) when the content is not valid UTF-8.
    /// </param>
    public static (string Text, string EncodingName) Decode(byte[] bytes, bool declaredCharsetFromContent = false)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "utf-8-bom");
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
            return (Encoding.UTF32.GetString(bytes, 4, bytes.Length - 4), "utf-32le");
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "utf-16le");
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "utf-16be");

        // UTF-32 saved without a byte order mark (rare): see TryDetectUtf32WithoutBom.
        if (TryDetectUtf32WithoutBom(bytes, out var utf32BigEndian))
        {
            return utf32BigEndian
                ? (new UTF32Encoding(bigEndian: true, byteOrderMark: false).GetString(bytes), "utf-32be")
                : (new UTF32Encoding(bigEndian: false, byteOrderMark: false).GetString(bytes), "utf-32le");
        }

        // UTF-16 saved without a byte order mark (some Windows tools and database exports): ASCII characters make every other byte a
        // NUL, which is valid UTF-8, so the strict decode below would accept it and hand every reader NUL-interleaved text.
        if (TryDetectUtf16WithoutBom(bytes, out var bigEndian))
        {
            return bigEndian
                ? (Encoding.BigEndianUnicode.GetString(bytes), "utf-16be")
                : (Encoding.Unicode.GetString(bytes), "utf-16le");
        }

        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            return (strictUtf8.GetString(bytes), "utf-8");
        }
        catch (DecoderFallbackException)
        {
            // Not UTF-8: use what the document says it is, else the Korean legacy code page.
        }

        if (declaredCharsetFromContent && TryDeclaredEncoding(bytes, out var declared))
            return (declared.GetString(bytes), declared.WebName);

        return (Encoding.GetEncoding(949).GetString(bytes), "cp949");
    }

    /// <summary>
    /// A code point is at most U+10FFFF, so every UTF-32 character has its highest byte 0 and the next one at most 0x10 -
    /// the last two bytes of a little-endian character, the first two of a big-endian one - and a file of ASCII text is three
    /// NULs in four. UTF-8 and CP949 text has no NUL, so a file in which <em>every</em> character has that shape is UTF-32.
    /// </summary>
    private static bool TryDetectUtf32WithoutBom(byte[] bytes, out bool bigEndian)
    {
        bigEndian = false;
        if (bytes.Length < 8 || bytes.Length % 4 != 0) return false;

        var quads = Math.Min(bytes.Length, 8192) / 4;
        int little = 0, big = 0;
        for (var q = 0; q < quads; q++)
        {
            var o = q * 4;
            if (bytes[o + 3] == 0 && bytes[o + 2] <= 0x10) little++;
            if (bytes[o] == 0 && bytes[o + 1] <= 0x10) big++;
        }

        // Both shapes at once (a file of NULs) say nothing about the order.
        if (little == quads && big != quads) return true;
        if (big == quads && little != quads) { bigEndian = true; return true; }
        return false;
    }

    /// <summary>
    /// Text in UTF-16 has a NUL in the high byte of every ASCII character (spaces, digits, line breaks, Latin letters), all in
    /// the same position of the pair - the odd bytes in little-endian, the even bytes in big-endian - while UTF-8 and CP949
    /// text has no NUL at all. NULs that fall (almost) only on one parity are therefore read as UTF-16; a Hangul syllable whose
    /// low byte is zero (such as U+AC00) puts a NUL on the other parity now and then, which the 4-to-1 margin tolerates.
    /// </summary>
    private static bool TryDetectUtf16WithoutBom(byte[] bytes, out bool bigEndian)
    {
        bigEndian = false;
        if (bytes.Length < 4 || bytes.Length % 2 != 0) return false;

        var sample = Math.Min(bytes.Length, 8192);
        int nulEven = 0, nulOdd = 0;
        for (var i = 0; i < sample; i++)
        {
            if (bytes[i] != 0) continue;
            if ((i & 1) == 0) nulEven++;
            else nulOdd++;
        }

        if (nulOdd >= 2 && nulOdd >= 4 * nulEven) return true;       // little-endian: the high byte comes second
        if (nulEven >= 2 && nulEven >= 4 * nulOdd) { bigEndian = true; return true; }
        return false;
    }

    private static bool TryDeclaredEncoding(byte[] bytes, out Encoding encoding)
    {
        encoding = Encoding.UTF8;

        // The declaration is ASCII and sits in the head; Latin-1 maps every byte, so the head reads as text whatever the charset.
        var head = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 2048));
        var match = MetaCharset.Match(head);
        if (!match.Success) return false;

        try
        {
            var name = match.Groups[1].Value;
            // "ks_c_5601-1987" (Windows' name for CP949) is not an IANA name the framework knows; both mean the same code page.
            encoding = name.Equals("ks_c_5601-1987", StringComparison.OrdinalIgnoreCase) ? Encoding.GetEncoding(949) : Encoding.GetEncoding(name);
            return encoding.CodePage != Encoding.UTF8.CodePage;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
