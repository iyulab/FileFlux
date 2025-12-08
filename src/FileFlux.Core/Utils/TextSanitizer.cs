using System.Text;
using System.Text.RegularExpressions;

namespace FileFlux.Core;

/// <summary>
/// Utility class for sanitizing extracted text to ensure UTF-8 validity.
/// Removes null bytes and other invalid characters that may be present
/// in binary document formats (PDF, DOCX, etc.).
/// </summary>
public static partial class TextSanitizer
{
    /// <summary>
    /// Removes null bytes (0x00) from text.
    /// Null bytes are invalid in UTF-8 text strings and cause failures
    /// when storing in databases that enforce UTF-8 validity.
    /// </summary>
    /// <param name="text">Text to sanitize.</param>
    /// <returns>Text with null bytes removed.</returns>
    public static string RemoveNullBytes(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        // Fast path: check if null bytes exist before creating new string
        if (!text.Contains('\0'))
            return text;

        return text.Replace("\0", string.Empty);
    }

    /// <summary>
    /// Sanitizes extracted text by removing invalid UTF-8 characters.
    /// This includes null bytes and other control characters that are
    /// not valid in text content.
    /// </summary>
    /// <param name="text">Text to sanitize.</param>
    /// <param name="removeControlChars">
    /// If true, also removes other control characters (0x01-0x08, 0x0B, 0x0C, 0x0E-0x1F).
    /// Default is false (only removes null bytes).
    /// </param>
    /// <returns>Sanitized UTF-8 valid text.</returns>
    public static string Sanitize(string? text, bool removeControlChars = false)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        // Always remove null bytes
        var result = RemoveNullBytes(text);

        // Optionally remove other control characters
        if (removeControlChars && ContainsControlCharacters(result))
        {
            result = ControlCharactersRegex().Replace(result, string.Empty);
        }

        return result;
    }

    /// <summary>
    /// Checks if the text contains any null bytes.
    /// </summary>
    /// <param name="text">Text to check.</param>
    /// <returns>True if text contains null bytes.</returns>
    public static bool ContainsNullBytes(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        return text.Contains('\0');
    }

    /// <summary>
    /// Checks if the text contains control characters (excluding common whitespace).
    /// </summary>
    /// <param name="text">Text to check.</param>
    /// <returns>True if text contains control characters.</returns>
    private static bool ContainsControlCharacters(string text)
    {
        foreach (var c in text)
        {
            // Control characters: 0x00-0x08, 0x0B, 0x0C, 0x0E-0x1F
            // Excluding: 0x09 (tab), 0x0A (LF), 0x0D (CR)
            if (c < 0x09 || (c > 0x0A && c < 0x0D) || (c > 0x0D && c < 0x20))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Validates that text is UTF-8 compatible and contains no invalid sequences.
    /// </summary>
    /// <param name="text">Text to validate.</param>
    /// <returns>True if text is valid UTF-8.</returns>
    public static bool IsValidUtf8(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return true;

        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var decoded = Encoding.UTF8.GetString(bytes);
            return string.Equals(text, decoded, StringComparison.Ordinal) && !ContainsNullBytes(text);
        }
        catch
        {
            return false;
        }
    }

    // Regex pattern for control characters (excluding tab, LF, CR)
    // Matches: 0x00-0x08, 0x0B, 0x0C, 0x0E-0x1F
    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F]", RegexOptions.Compiled)]
    private static partial Regex ControlCharactersRegex();
}
