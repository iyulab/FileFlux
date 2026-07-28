using System.Buffers;
using System.Text;

namespace FileFlux.Core;

/// <summary>
/// Utility class for UTF-8 filename handling
/// </summary>
public static class FileNameHelper
{
    /// <summary>
    /// Validates and normalizes filename to proper UTF-8 encoding.
    /// </summary>
    /// <param name="fileName">Filename to validate</param>
    /// <returns>UTF-8 normalized filename</returns>
    public static string NormalizeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return string.Empty;

        // Verify if already proper UTF-8 string
        try
        {
            var bytes = Encoding.UTF8.GetBytes(fileName);
            var decoded = Encoding.UTF8.GetString(bytes);

            // If identical to original, it's already valid UTF-8
            if (string.Equals(fileName, decoded, StringComparison.Ordinal))
                return fileName;

            return decoded;
        }
        catch (EncoderFallbackException)
        {
            // On UTF-8 encoding failure, convert to safe ASCII
            return ConvertToSafeFileName(fileName);
        }
    }

    /// <summary>
    /// Converts to safe filename (UTF-8 compatible)
    /// </summary>
    /// <param name="fileName">Original filename</param>
    /// <returns>Safe UTF-8 filename</returns>
    private static string ConvertToSafeFileName(string fileName)
    {
        var safeChars = fileName.Select(c =>
        {
            // Keep safe characters within ASCII range
            if (c < 128 && char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')
                return c;

            // Keep Unicode letters and digits (Korean, Chinese, etc.)
            if (char.IsLetter(c) || char.IsDigit(c))
                return c;

            // Convert other characters to underscore
            return '_';
        });

        return new string(safeChars.ToArray());
    }

    /// <summary>
    /// Extracts UTF-8 safe filename from file path
    /// </summary>
    /// <param name="filePath">File path</param>
    /// <returns>UTF-8 normalized filename</returns>
    public static string GetSafeFileName(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return string.Empty;

        // Both separators are stripped regardless of host OS. Path.GetFileName alone is host-relative:
        // on Linux it does not treat '\' as a separator, so a Windows-authored path arrives intact and
        // this helper would hand back "C:\dir\file.txt" as if it were a file name. Document paths
        // routinely cross platforms, so the answer must not depend on where the library happens to run.
        var cut = filePath.LastIndexOfAny(['/', '\\']);
        var fileName = cut >= 0 ? filePath[(cut + 1)..] : filePath;
        return NormalizeFileName(fileName);
    }

    /// <summary>
    /// Checks if filename contains invalid characters
    /// </summary>
    /// <param name="fileName">Filename to check</param>
    /// <returns>True if valid, false if invalid</returns>
    public static bool IsValidFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;

        return !fileName.Any(PortablyInvalidFileNameChars.Contains);
    }

    /// <summary>
    /// Characters rejected by <see cref="IsValidFileName"/>, on every host OS.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="Path.GetInvalidFileNameChars"/>, which is host-relative: on Linux it
    /// reports only '/' and NUL, so <c>a&lt;b&gt;c|d.txt</c> validates there and then breaks the moment the
    /// document set is opened on Windows. A library that answers "is this name usable?" differently
    /// depending on which machine it runs on cannot be relied on, so the union is used everywhere.
    /// </remarks>
    private static readonly SearchValues<char> PortablyInvalidFileNameChars =
        SearchValues.Create(BuildPortablyInvalidFileNameChars());

    private static string BuildPortablyInvalidFileNameChars()
    {
        // Windows' reserved set (a superset of Linux's, which is only '/' and NUL) plus every control
        // character, which no platform handles well in a file name.
        var chars = new StringBuilder("<>:\"/\\|?*");
        for (char c = '\0'; c < ' '; c++)
            chars.Append(c);
        return chars.ToString();
    }

    /// <summary>
    /// Safely extracts UTF-8 metadata from FileInfo
    /// </summary>
    /// <param name="fileInfo">File information</param>
    /// <returns>UTF-8 safe filename</returns>
    public static string ExtractSafeFileName(FileInfo fileInfo)
    {
        if (fileInfo == null)
            return string.Empty;

        return NormalizeFileName(fileInfo.Name);
    }

    /// <summary>
    /// Extracts filename from a path string that may be an alt text or image reference.
    /// Handles both Windows and Unix-style paths.
    /// </summary>
    /// <param name="pathOrText">Path or text that may contain a file path</param>
    /// <returns>Extracted filename, or original text if no path detected</returns>
    /// <example>
    /// "C:\Users\Admin\Desktop\image.jpg" => "image.jpg"
    /// "/home/user/docs/photo.png" => "photo.png"
    /// "Simple text" => "Simple text"
    /// </example>
    public static string ExtractFileNameFromPathOrText(string? pathOrText)
    {
        if (string.IsNullOrEmpty(pathOrText))
            return string.Empty;

        // Check if it looks like a path (contains path separators)
        if (pathOrText.Contains('\\') || pathOrText.Contains('/'))
        {
            // Use Path.GetFileName which handles both separators
            var fileName = Path.GetFileName(pathOrText);
            return string.IsNullOrEmpty(fileName) ? pathOrText : fileName;
        }

        return pathOrText;
    }
}
