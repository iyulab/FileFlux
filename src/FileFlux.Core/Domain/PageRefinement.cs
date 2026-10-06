namespace FileFlux.Core;

/// <summary>
/// What an LLM refinement pass rewrites.
/// </summary>
public enum LlmRefineScope
{
    /// <summary>
    /// The whole document in one pass. The output replaces the text without a check, and the page spans are not kept.
    /// </summary>
    Document = 0,

    /// <summary>
    /// One page at a time, each output checked against the page it replaces (<see cref="LlmRefineOptions.MinTokenCoverage"/>,
    /// <see cref="LlmRefineOptions.RequireSameNumbers"/>). A page that fails keeps its text, and the page spans are kept.
    /// Needs page spans; a document without them is left as it is.
    /// </summary>
    Pages = 1
}

/// <summary>
/// What happened to one page in a <see cref="LlmRefineScope.Pages"/> refinement.
/// </summary>
public enum PageRefinementOutcome
{
    /// <summary>The page was not sent to the refiner (see <see cref="PageRefinement.Reason"/>); its text is unchanged.</summary>
    Skipped = 0,

    /// <summary>The refiner returned the page unchanged.</summary>
    Native = 1,

    /// <summary>The refiner's output passed the checks and replaced the page's text.</summary>
    Refined = 2,

    /// <summary>The refiner's output failed a check (see <see cref="PageRefinement.Reason"/>); the page kept its text.</summary>
    Rejected = 3
}

/// <summary>
/// One page's result in a <see cref="LlmRefineScope.Pages"/> refinement.
/// </summary>
/// <param name="Page">1-based page number, as in the page spans.</param>
public sealed record PageRefinement(int Page)
{
    /// <summary>Reason for <see cref="PageRefinementOutcome.Skipped"/> and <see cref="PageRefinementOutcome.Rejected"/>.</summary>
    public const string NotSelected = "not_selected";

    /// <summary>The page is longer than <see cref="LlmRefineOptions.MaxPageCharacters"/>.</summary>
    public const string TooLong = "too_long";

    /// <summary>The output kept less than <see cref="LlmRefineOptions.MinTokenCoverage"/> of the page's word tokens.</summary>
    public const string LowCoverage = "low_coverage";

    /// <summary>The output's numbers differ from the page's (one dropped, changed or added).</summary>
    public const string NumbersChanged = "numbers_changed";

    /// <summary>The refiner threw or reported that it did not run.</summary>
    public const string RefinerFailed = "refiner_failed";

    /// <summary>The refiner returned no text.</summary>
    public const string EmptyOutput = "empty_output";

    /// <summary>What happened to the page.</summary>
    public PageRefinementOutcome Outcome { get; init; }

    /// <summary>Why the page was skipped or its output rejected; null otherwise.</summary>
    public string? Reason { get; init; }

    /// <summary>Share of the page's word tokens (case-folded) the refiner's output kept; null when nothing was checked.</summary>
    public double? TokenCoverage { get; init; }

    /// <summary>Whether the output has exactly the page's numbers; null when nothing was checked.</summary>
    public bool? NumbersMatched { get; init; }
}
