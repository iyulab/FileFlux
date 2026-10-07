namespace FileFlux.Core;

/// <summary>
/// What became of one enabled pass of an LLM refinement (<see cref="LlmRefinementInfo.Passes"/>).
/// </summary>
public enum LlmRefinementPassOutcome
{
    /// <summary>The text showed nothing this pass looks for, so no model call was made.</summary>
    NotNeeded = 0,

    /// <summary>The model answered and the text was kept as it was (the answer was the same, or did not meet the pass's own rule).</summary>
    Kept = 1,

    /// <summary>The model's answer replaced the text.</summary>
    Applied = 2,

    /// <summary>The pass was sent and produced nothing usable (see <see cref="LlmRefinementPass.Reason"/>); the text was kept.</summary>
    Failed = 3
}

/// <summary>
/// One enabled pass of an LLM refinement — <c>RestoreSentences</c>, <c>RemoveNoise</c>, <c>CorrectOcrErrors</c>,
/// <c>RestructureSections</c> or <c>MergeDuplicates</c> — and what became of it.
/// </summary>
/// <param name="Name">The pass, named after its <see cref="LlmRefineOptions"/> switch.</param>
public sealed record LlmRefinementPass(string Name)
{
    /// <summary>The response was cut off at the output token limit.</summary>
    public const string Truncated = "truncated";

    /// <summary>The prompt and the output budget exceed the model context the service declares; the call was not sent.</summary>
    public const string ContextTooSmall = "context_too_small";

    /// <summary>The model returned no text.</summary>
    public const string EmptyOutput = "empty_output";

    /// <summary>The call failed for another reason (see <see cref="Detail"/>).</summary>
    public const string Error = "error";

    /// <summary>What became of the pass.</summary>
    public LlmRefinementPassOutcome Outcome { get; init; }

    /// <summary>Why a <see cref="LlmRefinementPassOutcome.Failed"/> pass failed; null otherwise.</summary>
    public string? Reason { get; init; }

    /// <summary>The failure as text (the same note <see cref="LlmRefinementInfo.Warnings"/> carries); null otherwise.</summary>
    public string? Detail { get; init; }
}
