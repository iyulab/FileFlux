using FileFlux.Core;

namespace FileFlux.Domain;

/// <summary>
/// Collects the notes a caller should see next to an extraction: what the reader reported in
/// <see cref="RawContent.Warnings"/>, and a note when a file that has content produced no text at all.
/// </summary>
public static class ExtractionWarnings
{
    /// <summary>
    /// The warnings for one extraction, in the order a reader would want them read: the reader's own notes first,
    /// then the zero-text note.
    /// </summary>
    /// <param name="rawContent">What the reader returned.</param>
    /// <param name="fileSize">Size of the input file in bytes.</param>
    /// <param name="extractedText">The text that will be written out (after image handling).</param>
    /// <returns>The warnings; empty when the extraction needs no note.</returns>
    public static List<string> Collect(RawContent rawContent, long fileSize, string extractedText)
    {
        ArgumentNullException.ThrowIfNull(rawContent);

        var warnings = new List<string>(rawContent.Warnings);

        if (fileSize > 0 && string.IsNullOrWhiteSpace(extractedText))
        {
            warnings.Add(
                $"No text was extracted (0 characters) from a non-empty file ({fileSize} bytes). " +
                "The file was read but produced no text: it may hold only images, an empty sheet or page, " +
                "or a variant of the format the reader does not understand.");
        }

        return warnings;
    }
}
