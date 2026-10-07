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
    /// One page at a time, each output checked against the page it replaces (<see cref="LlmRefineOptions.MinNativeCoverage"/>,
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

    /// <summary>
    /// The page's text is unchanged after the refiner: the model kept it, or (<see cref="PageRefinement.Reason"/>) none of
    /// the enabled passes applied to it, or every pass that was sent failed.
    /// </summary>
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

    /// <summary>The output kept less than <see cref="LlmRefineOptions.MinNativeCoverage"/> of the page's text.</summary>
    public const string LowCoverage = "low_coverage";

    /// <summary>The output's numbers differ from the page's (one dropped, changed or added).</summary>
    public const string NumbersChanged = "numbers_changed";

    /// <summary>The refiner threw or reported that it did not run.</summary>
    public const string RefinerFailed = "refiner_failed";

    /// <summary>The refiner returned no text.</summary>
    public const string EmptyOutput = "empty_output";

    /// <summary>
    /// <see cref="PageRefinementOutcome.Native"/>: no enabled pass found anything to fix, so the model was not called
    /// (<see cref="Passes"/> are all <see cref="LlmRefinementPassOutcome.NotNeeded"/>).
    /// </summary>
    public const string NoPassNeeded = "no_pass_needed";

    /// <summary>
    /// <see cref="PageRefinementOutcome.Native"/>: every pass that was sent failed — truncated, over the model context,
    /// an empty answer or an error (<see cref="Passes"/>, <see cref="Notes"/>).
    /// </summary>
    public const string PassesFailed = "passes_failed";

    /// <summary>What happened to the page.</summary>
    public PageRefinementOutcome Outcome { get; init; }

    /// <summary>
    /// Why the page was skipped or its output rejected, or why a <see cref="PageRefinementOutcome.Native"/> page was not
    /// changed by the model (<see cref="NoPassNeeded"/>, <see cref="PassesFailed"/>); null when the model kept the page, or
    /// the refiner does not report its passes.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Share of the page's text the refiner's output kept — what <see cref="LlmRefineOptions.MinNativeCoverage"/> is
    /// checked against (word by word, however spaced); null when nothing was checked.
    /// </summary>
    public double? NativeCoverage { get; init; }

    /// <summary>
    /// Share of the page's whitespace-separated words (case-folded) the output kept, for comparison with
    /// <see cref="NativeCoverage"/>: a re-spacing lowers this one and not that one. Not a check; null when nothing was checked.
    /// </summary>
    public double? TokenCoverage { get; init; }

    /// <summary>Whether the output has exactly the page's numbers; null when nothing was checked.</summary>
    public bool? NumbersMatched { get; init; }

    /// <summary>The refiner's passes on this page (<see cref="LlmRefinementInfo.Passes"/>); null when the refiner does not report them.</summary>
    public IReadOnlyList<LlmRefinementPass>? Passes { get; init; }

    /// <summary>The refiner's notes on this page (<see cref="LlmRefinementInfo.Warnings"/>) — why a pass was not applied.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}
