using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using FileFlux.Core;
using Microsoft.Extensions.Logging;

namespace FileFlux.Infrastructure;

/// <summary>
/// LLM-based document refiner implementation.
/// Transforms RefinedContent into LlmRefinedContent by applying LLM-powered improvements.
/// </summary>
public sealed partial class LlmRefiner : ILlmRefiner
{
    private readonly IDocumentAnalysisService? _textCompletionService;
    private readonly ILogger<LlmRefiner> _logger;

    /// <inheritdoc/>
    public string RefinerType => "LlmRefiner";

    /// <inheritdoc/>
    public bool IsAvailable => _textCompletionService != null;

    /// <inheritdoc/>
    public string? ModelName => _textCompletionService?.ProviderInfo?.Name;

    /// <summary>
    /// Creates a new LLM refiner.
    /// </summary>
    public LlmRefiner(
        IDocumentAnalysisService? textCompletionService = null,
        ILogger<LlmRefiner>? logger = null)
    {
        _textCompletionService = textCompletionService;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LlmRefiner>.Instance;
    }

    /// <inheritdoc/>
    public async Task<LlmRefinedContent> RefineAsync(
        RefinedContent refined,
        LlmRefineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refined);
        options ??= LlmRefineOptions.Default;

        var sw = Stopwatch.StartNew();
        LogStartingLlmRefinement(_logger);

        // If no LLM service available, return passthrough
        if (!IsAvailable)
        {
            LogLlmNotAvailable(_logger);
            return CreatePassthroughResult(refined, "LLM service not available");
        }

        // If no improvements are enabled, return passthrough
        if (!options.HasAnyImprovementEnabled)
        {
            LogNoImprovementsEnabled(_logger);
            return CreatePassthroughResult(refined, "No improvements enabled");
        }

        try
        {
            var improvements = new List<string>();
            var refinedText = refined.Text;
            var inputTokens = 0;
            var outputTokens = 0;
            var quality = new LlmRefinementQuality
            {
                InputCharCount = refined.Text.Length
            };

            // Apply LLM improvements based on options
            if (options.RestoreSentences)
            {
                var (text, improved, tokens) = await RestoreBrokenSentencesAsync(refinedText, cancellationToken).ConfigureAwait(false);
                if (improved)
                {
                    refinedText = text;
                    improvements.Add("Restored broken sentences");
                    quality = quality with { SentencesRestored = CountRestoredSentences(refined.Text, refinedText) };
                }
                inputTokens += tokens;
            }

            if (options.RemoveNoise)
            {
                var (text, improved, tokens) = await RemoveNoiseAsync(refinedText, cancellationToken).ConfigureAwait(false);
                if (improved)
                {
                    refinedText = text;
                    improvements.Add("Removed noise content");
                    quality = quality with { NoiseSegmentsRemoved = CountRemovedSegments(refined.Text, refinedText) };
                }
                inputTokens += tokens;
            }

            if (options.CorrectOcrErrors)
            {
                var (text, improved, tokens) = await CorrectOcrErrorsAsync(refinedText, cancellationToken).ConfigureAwait(false);
                if (improved)
                {
                    refinedText = text;
                    improvements.Add("Corrected OCR errors");
                    quality = quality with { OcrErrorsCorrected = CountCorrectedErrors(refined.Text, refinedText) };
                }
                inputTokens += tokens;
            }

            if (options.RestructureSections)
            {
                var (text, improved, tokens) = await RestructureSectionsAsync(refinedText, cancellationToken).ConfigureAwait(false);
                if (improved)
                {
                    refinedText = text;
                    improvements.Add("Restructured sections");
                    quality = quality with { StructureChanges = 1 };
                }
                inputTokens += tokens;
            }

            if (options.MergeDuplicates)
            {
                var (text, improved, tokens) = await MergeDuplicatesAsync(refinedText, cancellationToken).ConfigureAwait(false);
                if (improved)
                {
                    refinedText = text;
                    improvements.Add("Merged duplicate content");
                    quality = quality with { DuplicatesMerged = 1 };
                }
                inputTokens += tokens;
            }

            // Calculate final quality metrics
            quality = quality with
            {
                OutputCharCount = refinedText.Length,
                ImprovementScore = improvements.Count > 0 ? Math.Min(1.0, improvements.Count * 0.2) : 0.0,
                ConfidenceScore = improvements.Count > 0 ? 0.85 : 1.0
            };

            sw.Stop();

            return new LlmRefinedContent
            {
                RefinedId = refined.Id,
                RawId = refined.RawId,
                Text = refinedText.Trim(),
                Sections = refined.Sections,
                Structures = refined.Structures,
                Metadata = refined.Metadata,
                Quality = quality,
                Info = new LlmRefinementInfo
                {
                    LlmWasUsed = improvements.Count > 0,
                    Model = ModelName,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    Duration = sw.Elapsed,
                    Improvements = improvements
                }
            };
        }
        catch (Exception ex)
        {
            LogLlmRefinementFailed(_logger, ex);
            return CreatePassthroughResult(refined, $"LLM refinement failed: {ex.Message}");
        }
    }

    private static LlmRefinedContent CreatePassthroughResult(RefinedContent refined, string reason)
    {
        return new LlmRefinedContent
        {
            RefinedId = refined.Id,
            RawId = refined.RawId,
            Text = refined.Text,
            Sections = refined.Sections,
            Structures = refined.Structures,
            Metadata = refined.Metadata,
            Quality = new LlmRefinementQuality
            {
                InputCharCount = refined.Text.Length,
                OutputCharCount = refined.Text.Length,
                ImprovementScore = 0.0,
                ConfidenceScore = 1.0
            },
            Info = new LlmRefinementInfo
            {
                LlmWasUsed = false,
                SkipReason = reason
            }
        };
    }

    /// <summary>
    /// Restore broken sentences caused by PDF line breaks.
    /// </summary>
    private async Task<(string Text, bool Improved, int Tokens)> RestoreBrokenSentencesAsync(
        string text, CancellationToken cancellationToken)
    {
        if (_textCompletionService == null)
            return (text, false, 0);

        // Only process if there are potential broken sentences
        if (!HasPotentialBrokenSentences(text))
            return (text, false, 0);

        var prompt = $"""
            Fix broken sentences in the following text. PDF documents often have line breaks
            in the middle of sentences. Join these broken lines while preserving paragraph breaks.

            Rules:
            - Join lines that end without punctuation to the next line
            - Preserve actual paragraph breaks (double newlines)
            - Keep headings and bullet points separate
            - Do not add or remove content, only fix line breaks

            Text:
            {text}

            Return only the fixed text without any explanation.
            """;

        try
        {
            var result = await _textCompletionService.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
            var improved = !string.IsNullOrWhiteSpace(result) && result != text;
            return (improved ? result : text, improved, EstimateTokens(prompt));
        }
        catch (Exception ex)
        {
            LogRestoreSentencesFailed(_logger, ex);
            return (text, false, 0);
        }
    }

    /// <summary>
    /// Remove noise content (ads, legal notices, irrelevant content).
    /// </summary>
    private async Task<(string Text, bool Improved, int Tokens)> RemoveNoiseAsync(
        string text, CancellationToken cancellationToken)
    {
        if (_textCompletionService == null)
            return (text, false, 0);

        // Only process if text is long enough to potentially have noise
        if (text.Length < 500)
            return (text, false, 0);

        var prompt = $"""
            Remove noise content from the following document while preserving all meaningful content.

            Noise includes:
            - Legal disclaimers and copyright notices
            - Advertisement sections
            - Repetitive header/footer content
            - Page numbers and navigation text
            - Boilerplate text

            Rules:
            - Preserve all main content
            - Keep document structure (headings, lists)
            - Do not summarize or rephrase content
            - Only remove clearly irrelevant sections

            Text:
            {text}

            Return only the cleaned text without any explanation.
            """;

        try
        {
            var result = await _textCompletionService.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
            var improved = !string.IsNullOrWhiteSpace(result) && result.Length < text.Length * 0.95;
            return (improved ? result : text, improved, EstimateTokens(prompt));
        }
        catch (Exception ex)
        {
            LogRemoveNoiseFailed(_logger, ex);
            return (text, false, 0);
        }
    }

    /// <summary>
    /// Correct OCR errors in scanned documents.
    /// </summary>
    private async Task<(string Text, bool Improved, int Tokens)> CorrectOcrErrorsAsync(
        string text, CancellationToken cancellationToken)
    {
        if (_textCompletionService == null)
            return (text, false, 0);

        // Only process if there are potential OCR errors
        if (!HasPotentialOcrErrors(text))
            return (text, false, 0);

        var prompt = $"""
            Fix OCR (optical character recognition) errors in the following text.

            Common OCR errors include:
            - 'rn' misread as 'm' or vice versa
            - '0' (zero) misread as 'O' (letter O)
            - '1' (one) misread as 'l' (letter l) or 'I'
            - Missing spaces between words
            - Extra spaces within words
            - Special characters misread

            Rules:
            - Fix obvious OCR errors based on context
            - Preserve intentional formatting
            - Do not change the meaning of content
            - Keep technical terms and proper nouns as-is unless clearly wrong

            Text:
            {text}

            Return only the corrected text without any explanation.
            """;

        try
        {
            var result = await _textCompletionService.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
            var improved = !string.IsNullOrWhiteSpace(result) && result != text;
            return (improved ? result : text, improved, EstimateTokens(prompt));
        }
        catch (Exception ex)
        {
            LogCorrectOcrFailed(_logger, ex);
            return (text, false, 0);
        }
    }

    /// <summary>
    /// Restructure document sections for better organization.
    /// </summary>
    private async Task<(string Text, bool Improved, int Tokens)> RestructureSectionsAsync(
        string text, CancellationToken cancellationToken)
    {
        if (_textCompletionService == null)
            return (text, false, 0);

        // Only process if there are headings to restructure
        if (!HasHeadings(text))
            return (text, false, 0);

        var prompt = $"""
            Improve the heading structure of the following document.

            Tasks:
            - Fix inconsistent heading levels (e.g., H1 -> H3 should become H1 -> H2)
            - Demote annotation-like headings to plain text
            - Ensure logical hierarchy
            - Split very long sections if needed

            Rules:
            - Preserve all content
            - Only adjust markdown heading markers (#, ##, etc.)
            - Keep the same number of sections
            - Do not merge or remove sections

            Text:
            {text}

            Return only the restructured text without any explanation.
            """;

        try
        {
            var result = await _textCompletionService.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
            var improved = !string.IsNullOrWhiteSpace(result) && result != text;
            return (improved ? result : text, improved, EstimateTokens(prompt));
        }
        catch (Exception ex)
        {
            LogRestructureSectionsFailed(_logger, ex);
            return (text, false, 0);
        }
    }

    /// <summary>
    /// Merge semantically duplicate content.
    /// </summary>
    private async Task<(string Text, bool Improved, int Tokens)> MergeDuplicatesAsync(
        string text, CancellationToken cancellationToken)
    {
        if (_textCompletionService == null)
            return (text, false, 0);

        // Only process if text is long enough to potentially have duplicates
        if (text.Length < 1000)
            return (text, false, 0);

        var prompt = $"""
            Identify and merge duplicate content in the following document.

            Tasks:
            - Find paragraphs or sections that say the same thing
            - Merge duplicates, keeping the most complete version
            - Remove exact duplicates

            Rules:
            - Preserve unique content
            - Keep the better-written version of duplicates
            - Maintain document structure
            - Do not summarize content

            Text:
            {text}

            Return only the deduplicated text without any explanation.
            """;

        try
        {
            var result = await _textCompletionService.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
            // Consider improved if text was reduced by more than 5%
            var improved = !string.IsNullOrWhiteSpace(result) && result.Length < text.Length * 0.95;
            return (improved ? result : text, improved, EstimateTokens(prompt));
        }
        catch (Exception ex)
        {
            LogMergeDuplicatesFailed(_logger, ex);
            return (text, false, 0);
        }
    }

    // Helper methods

    private static bool HasPotentialBrokenSentences(string text)
    {
        // Check for lines ending without punctuation followed by lowercase letter
        return BrokenSentenceRegex().IsMatch(text);
    }

    private static bool HasPotentialOcrErrors(string text)
    {
        // Check for common OCR error patterns
        return OcrErrorPatternRegex().IsMatch(text);
    }

    private static bool HasHeadings(string text)
    {
        return HeadingRegex().IsMatch(text);
    }

    private static int CountRestoredSentences(string original, string refined)
    {
        var originalLineBreaks = original.Split('\n').Length;
        var refinedLineBreaks = refined.Split('\n').Length;
        return Math.Max(0, originalLineBreaks - refinedLineBreaks);
    }

    private static int CountRemovedSegments(string original, string refined)
    {
        var originalLength = original.Length;
        var refinedLength = refined.Length;
        var removed = originalLength - refinedLength;
        // Estimate segments as ~200 chars per segment
        return Math.Max(0, removed / 200);
    }

    private static int CountCorrectedErrors(string original, string refined)
    {
        // Simple heuristic: count character differences
        var changes = 0;
        var minLen = Math.Min(original.Length, refined.Length);
        for (int i = 0; i < minLen; i++)
        {
            if (original[i] != refined[i])
                changes++;
        }
        return changes;
    }

    private static int EstimateTokens(string text)
    {
        // Rough estimation: ~4 characters per token
        return text.Length / 4;
    }

    #region LoggerMessage

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting LLM refinement")]
    private static partial void LogStartingLlmRefinement(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "LLM service not available, creating passthrough result")]
    private static partial void LogLlmNotAvailable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No LLM improvements enabled, creating passthrough result")]
    private static partial void LogNoImprovementsEnabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM refinement failed, returning passthrough result")]
    private static partial void LogLlmRefinementFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to restore broken sentences")]
    private static partial void LogRestoreSentencesFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to remove noise")]
    private static partial void LogRemoveNoiseFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to correct OCR errors")]
    private static partial void LogCorrectOcrFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to restructure sections")]
    private static partial void LogRestructureSectionsFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to merge duplicates")]
    private static partial void LogMergeDuplicatesFailed(ILogger logger, Exception ex);

    #endregion

    [GeneratedRegex(@"[a-z,]\n[a-z]", RegexOptions.Compiled)]
    private static partial Regex BrokenSentenceRegex();

    [GeneratedRegex(@"[Il1O0]{2,}|[a-z]\s[a-z]\s[a-z]|rn(?=[aeiou])", RegexOptions.Compiled)]
    private static partial Regex OcrErrorPatternRegex();

    [GeneratedRegex(@"^#{1,6}\s", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex HeadingRegex();
}
