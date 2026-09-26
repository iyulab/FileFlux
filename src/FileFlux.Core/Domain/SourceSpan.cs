namespace FileFlux.Core;

/// <summary>
/// Where a stretch of a document's text came from in its source: a page of a paginated document, a time range of a
/// recording. Offsets are character positions in the text the span belongs to (<see cref="RawContent.Text"/> for a
/// reader's spans, <see cref="RefinedContent.Text"/> after refinement). Spans are ordered and read as a partition:
/// each covers the text from its <see cref="Start"/> up to the next span's start.
/// </summary>
/// <param name="Start">First character of the span.</param>
/// <param name="End">One past the last character of the span.</param>
public sealed record SourceSpan(int Start, int End)
{
    /// <summary>1-based page number, for paginated sources.</summary>
    public int? Page { get; init; }

    /// <summary>Start of the span in a timed source (audio, video).</summary>
    public TimeSpan? StartTime { get; init; }

    /// <summary>End of the span in a timed source.</summary>
    public TimeSpan? EndTime { get; init; }
}
