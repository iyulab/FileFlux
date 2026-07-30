namespace FileFlux.Core;

/// <summary>
/// Detects the MIME type of an embedded image from its actual bytes, falling back to the
/// resource identifier's extension only when the bytes are absent or unrecognized.
/// </summary>
/// <remarks>
/// OOXML/HWP resource identifiers are container-internal keys (e.g. "rId11") and frequently
/// carry no file extension at all, so guessing from the identifier alone silently degrades to
/// "application/octet-stream" for a large share of real documents even though the actual image
/// format is trivially identifiable from its header bytes.
/// </remarks>
public static class ImageMimeTypeDetector
{
    public static string Detect(byte[]? data, string resourceId)
    {
        return DetectFromMagicBytes(data) ?? GuessFromExtension(resourceId);
    }

    private static string? DetectFromMagicBytes(byte[]? data)
    {
        if (data == null)
            return null;

        if (data.Length >= 8 &&
            data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
            data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
            return "image/png";

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return "image/jpeg";

        if (data.Length >= 6 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F' &&
            data[3] == (byte)'8' && (data[4] == (byte)'7' || data[4] == (byte)'9') && data[5] == (byte)'a')
            return "image/gif";

        if (data.Length >= 12 &&
            data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F' &&
            data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
            return "image/webp";

        if (data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M')
            return "image/bmp";

        // EMF: 32-bit record type 1 (EMR_HEADER) at offset 0, " EMF" signature at offset 40.
        if (data.Length >= 44 &&
            data[0] == 0x01 && data[1] == 0x00 && data[2] == 0x00 && data[3] == 0x00 &&
            data[40] == 0x20 && data[41] == 0x45 && data[42] == 0x4D && data[43] == 0x46)
            return "image/emf";

        // WMF: either the placeable header magic, or a bare standard-format header
        // (mtType 1 or 2, headerSize 9) with no placeable wrapper.
        if (data.Length >= 4 &&
            ((data[0] == 0xD7 && data[1] == 0xCD && data[2] == 0xC6 && data[3] == 0x9A) ||
             ((data[0] == 0x01 || data[0] == 0x02) && data[1] == 0x00 && data[2] == 0x09 && data[3] == 0x00)))
            return "image/wmf";

        return null;
    }

    private static string GuessFromExtension(string resourceId)
    {
        var lower = resourceId.ToLowerInvariant();
        if (lower.EndsWith(".png")) return "image/png";
        if (lower.EndsWith(".jpg") || lower.EndsWith(".jpeg")) return "image/jpeg";
        if (lower.EndsWith(".gif")) return "image/gif";
        if (lower.EndsWith(".webp")) return "image/webp";
        if (lower.EndsWith(".bmp")) return "image/bmp";
        if (lower.EndsWith(".svg")) return "image/svg+xml";
        if (lower.EndsWith(".emf")) return "image/emf";
        if (lower.EndsWith(".wmf")) return "image/wmf";
        return "application/octet-stream";
    }
}
