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

        var fileName = Path.GetFileName(filePath);
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

        var invalidChars = Path.GetInvalidFileNameChars();
        return !fileName.Any(c => invalidChars.Contains(c));
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
}
