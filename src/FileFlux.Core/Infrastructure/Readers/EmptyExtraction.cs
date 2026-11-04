namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Marks an extraction that read a non-empty file and found nothing in it — no text, no image, no table — and whose
/// reader did not say why. Without the mark the result is indistinguishable from a legitimately short document:
/// a consumer sees an empty string and a success.
/// </summary>
/// <remarks>
/// A reader that knows the reason sets <see cref="HintKey"/> itself (the PDF reader's <c>no_text_layer</c>,
/// <c>blank_page</c>, …) and this leaves its answer alone. The mark is the general fallback for every other reader,
/// applied in the one place every extraction passes through, next to
/// <see cref="FormatSignature.NoteDeclaredMismatch"/>.
/// </remarks>
public static class EmptyExtraction
{
    /// <summary>The <see cref="RawContent.Hints"/> key that explains an empty extraction.</summary>
    public const string HintKey = "extraction_failure_reason";

    /// <summary>
    /// The <see cref="HintKey"/> value written when a non-empty file produced nothing and the reader gave no reason.
    /// </summary>
    public const string Reason = "empty_extraction";

    /// <summary>
    /// Adds <see cref="Reason"/> to <see cref="RawContent.Hints"/> and a note to <see cref="RawContent.Warnings"/> when
    /// <paramref name="raw"/> holds no text, no image and no table although the source had content.
    /// </summary>
    /// <param name="raw">The extraction result to annotate.</param>
    /// <param name="sourceLength">
    /// The source's size in bytes when the reader did not record it in <see cref="RawContent.File"/>; <see langword="null"/>
    /// when unknown (nothing is marked then: an unknown size is not known to be non-empty).
    /// </param>
    public static void Note(RawContent raw, long? sourceLength = null)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var size = raw.File.Size > 0 ? raw.File.Size : sourceLength ?? 0;
        if (size <= 0
            || !string.IsNullOrWhiteSpace(raw.Text)
            || raw.Images.Count > 0
            || raw.Tables.Count > 0
            || raw.Hints.ContainsKey(HintKey))
        {
            return;
        }

        raw.Hints[HintKey] = Reason;
        raw.Warnings.Add(
            $"No text was extracted (0 characters) from a non-empty file ({size} bytes). " +
            "The file was read but produced no text: it may hold only an empty sheet or page, " +
            "or a variant of the format the reader does not understand. [" + Reason + "]");
    }
}
