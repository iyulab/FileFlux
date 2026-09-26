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
            var skipped = new List<string>();
            var refinedText = refined.Text;
            var inputTokens = 0;
            var outputTokens = 0;
            var quality = new LlmRefinementQuality
            {
                InputCharCount = refined.Text.Length
            };

            // Apply LLM improvements based on options
            // LlmRefineOptions.Temperature / MaxTokens reach every call from here (0.25.0); MaxTokens <= 0 means the
            // service's default, as its documentation says.
            var settings = new GenerationSettings(options.Temperature, options.MaxTokens is > 0 ? options.MaxTokens : null);
            // DocumentType / TargetLanguage / CustomInstructions become rules in every prompt below (0.25.0) — the same three
            // the FluxIndex ILlmRefiner implementation already put in its prompt; before this the library's own refiner
            // ignored them.
            var context = ContextRules(options);

            if (options.RestoreSentences)
            {
                var (text, improved, tokens) = await RestoreBrokenSentencesAsync(refinedText, context, settings, skipped, cancellationToken).ConfigureAwait(false);
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
                var (text, improved, tokens) = await RemoveNoiseAsync(refinedText, options.PreserveFormatting, context, settings, skipped, cancellationToken).ConfigureAwait(false);
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
                var (text, improved, tokens) = await CorrectOcrErrorsAsync(refinedText, options.PreserveFormatting, context, settings, skipped, cancellationToken).ConfigureAwait(false);
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
                var (text, improved, tokens) = await RestructureSectionsAsync(refinedText, options.PreserveFormatting, context, settings, skipped, cancellationToken).ConfigureAwait(false);
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
                var (text, improved, tokens) = await MergeDuplicatesAsync(refinedText, context, settings, skipped, cancellationToken).ConfigureAwait(false);
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

            // Sections carry character offsets; after a rewrite the input's offsets describe a different text.
            var outputText = refinedText.Trim();
            var sections = outputText == refined.Text || refined.Sections.Count == 0
                ? refined.Sections
                : SectionPathCalculator.BuildSections(outputText);

            return new LlmRefinedContent
            {
                RefinedId = refined.Id,
                RawId = refined.RawId,
                Text = outputText,
                Sections = sections,
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
                    Improvements = improvements,
                    Warnings = skipped
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
        string text, string context, GenerationSettings settings, List<string> skipped, CancellationToken cancellationToken)
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
            {context}

            Text:
            {text}

            Return only the fixed text without any explanation.
            """;

        try
        {
            var result = await GenerateBoundedAsync(prompt, text, settings, cancellationToken).ConfigureAwait(false);
            var improved = !string.IsNullOrWhiteSpace(result) && result != text;
            return (improved ? result : text, improved, EstimateTokens(prompt));
        }
        catch (Exception ex)
        {
            LogRestoreSentencesFailed(_logger, ex);
            skipped.Add(SkipNote("RestoreSentences", ex));
            return (text, false, 0);
        }
    }

    /// <summary>
    /// Remove noise content (ads, legal notices, irrelevant content).
    /// </summary>
    private async Task<(string Text, bool Improved, int Tokens)> RemoveNoiseAsync(
        string text, bool preserveFormatting, string context, GenerationSettings settings, List<string> skipped, CancellationToken cancellationToken)
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
            {FormattingRule(preserveFormatting)}
            {context}

            Text:
            {text}

            Return only the cleaned text without any explanation.
            """;

        try
        {
            var result = await GenerateBoundedAsync(prompt, text, settings, cancellationToken).ConfigureAwait(false);
            var improved = !string.IsNullOrWhiteSpace(result) && result.Length < text.Length * 0.95;
            return (improved ? result : text, improved, EstimateTokens(prompt));
        }
        catch (Exception ex)
        {
            LogRemoveNoiseFailed(_logger, ex);
            skipped.Add(SkipNote("RemoveNoise", ex));
            return (text, false, 0);
        }
    }

    /// <summary>
    /// Correct OCR errors in scanned documents.
    /// </summary>
    private async Task<(string Text, bool Improved, int Tokens)> CorrectOcrErrorsAsync(
        string text, bool preserveFormatting, string context, GenerationSettings settings, List<string> skipped, CancellationToken cancellationToken)
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
            {FormattingRule(preserveFormatting)}
            - Do not change the meaning of content
            - Keep technical terms and proper nouns as-is unless clearly wrong
            {context}

            Text:
            {text}

            Return only the corrected text without any explanation.
            """;

        try
        {
            var result = await GenerateBoundedAsync(prompt, text, settings, cancellationToken).ConfigureAwait(false);
            var improved = !string.IsNullOrWhiteSpace(result) && result != text;
            return (improved ? result : text, improved, EstimateTokens(prompt));
        }
        catch (Exception ex)
        {
            LogCorrectOcrFailed(_logger, ex);
            skipped.Add(SkipNote("CorrectOcrErrors", ex));
            return (text, false, 0);
        }
    }

    /// <summary>
    /// Restructure document sections for better organization.
    /// </summary>
    private async Task<(string Text, bool Improved, int Tokens)> RestructureSectionsAsync(
        string text, bool preserveFormatting, string context, GenerationSettings settings, List<string> skipped, CancellationToken cancellationToken)
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
            {FormattingRule(preserveFormatting)}
            {context}

            Text:
            {text}

            Return only the restructured text without any explanation.
            """;

        try
        {
            var result = await GenerateBoundedAsync(prompt, text, settings, cancellationToken).ConfigureAwait(false);
            var improved = !string.IsNullOrWhiteSpace(result) && result != text;
            return (improved ? result : text, improved, EstimateTokens(prompt));
        }
        catch (Exception ex)
        {
            LogRestructureSectionsFailed(_logger, ex);
            skipped.Add(SkipNote("RestructureSections", ex));
            return (text, false, 0);
        }
    }

    /// <summary>
    /// Merge semantically duplicate content.
    /// </summary>
    private async Task<(string Text, bool Improved, int Tokens)> MergeDuplicatesAsync(
        string text, string context, GenerationSettings settings, List<string> skipped, CancellationToken cancellationToken)
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
            {context}

            Text:
            {text}

            Return only the deduplicated text without any explanation.
            """;

        try
        {
            var result = await GenerateBoundedAsync(prompt, text, settings, cancellationToken).ConfigureAwait(false);
            // Consider improved if text was reduced by more than 5%
            var improved = !string.IsNullOrWhiteSpace(result) && result.Length < text.Length * 0.95;
            return (improved ? result : text, improved, EstimateTokens(prompt));
        }
        catch (Exception ex)
        {
            LogMergeDuplicatesFailed(_logger, ex);
            skipped.Add(SkipNote("MergeDuplicates", ex));
            return (text, false, 0);
        }
    }

    // Helper methods

    private static bool HasPotentialBrokenSentences(string text)
    {
        // Check for lines ending without punctuation followed by lowercase letter
        return BrokenSentenceRegex().IsMatch(text);
    }

    /// <summary>
    /// The prompt rule <see cref="LlmRefineOptions.PreserveFormatting"/> stands for. Before 0.24.2 the option was
    /// declared and read by nothing: the OCR prompt always said "preserve intentional formatting" and the other two
    /// said nothing about it.
    /// </summary>
    internal static string FormattingRule(bool preserveFormatting) => preserveFormatting
        ? "- Preserve the original formatting exactly (line breaks, spacing, markdown markers)"
        : "- Formatting (line breaks, spacing) may be normalized where it improves readability";

    /// <summary>
    /// The rules <c>LlmRefineOptions.DocumentType</c>, <c>TargetLanguage</c> and <c>CustomInstructions</c> add to every
    /// refinement prompt — one line each, in that order, nothing when all three are unset. Kept as a plain function so a
    /// test can pin the text and so every prompt gets the same block.
    /// </summary>
    internal static string ContextRules(LlmRefineOptions options)
    {
        var sb = new StringBuilder();
        if (options.DocumentType != DocumentTypeHint.Auto)
            sb.Append("- The document is ").Append(DescribeDocumentType(options.DocumentType)).Append('\n');
        if (!string.IsNullOrWhiteSpace(options.TargetLanguage))
            sb.Append("- Write the result in the language '").Append(options.TargetLanguage.Trim())
              .Append("' (keep passages that are already in it; do not translate technical terms)\n");
        if (!string.IsNullOrWhiteSpace(options.CustomInstructions))
            sb.Append("- Additional instructions: ").Append(options.CustomInstructions.Trim()).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }

    private static string DescribeDocumentType(DocumentTypeHint hint) => hint switch
    {
        DocumentTypeHint.General => "a general document",
        DocumentTypeHint.Technical => "technical documentation (keep code, identifiers and commands exact)",
        DocumentTypeHint.Legal => "a legal document (keep clause numbering and defined terms exact)",
        DocumentTypeHint.Academic => "an academic paper (keep citations, figures and equations exact)",
        DocumentTypeHint.Pdf => "text extracted from a PDF (line breaks may fall mid-sentence)",
        DocumentTypeHint.ScannedDocument => "a scanned document (expect OCR errors)",
        _ => hint.ToString(),
    };

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

    /// <summary>
    /// One refinement call, with an output budget the rewrite fits in. Every pass returns the whole text, so an unset
    /// <see cref="LlmRefineOptions.MaxTokens"/> is sized from the text instead of being left to the service's default —
    /// 1000 tokens for the OpenAI-compatible service, which cut off every document past roughly 4 KB. The call is not
    /// sent when the prompt and that budget exceed the context the service declares
    /// (<see cref="DocumentAnalysisServiceInfo.MaxContextLength"/>; 0 = not declared, not checked).
    /// </summary>
    private async Task<string> GenerateBoundedAsync(string prompt, string text, GenerationSettings settings, CancellationToken cancellationToken)
    {
        var maxTokens = settings.MaxTokens ?? OutputBudget(text);
        var contextLength = _textCompletionService!.ProviderInfo?.MaxContextLength ?? 0;
        var promptTokens = PromptTokens(prompt);
        if (contextLength > 0 && promptTokens + maxTokens > contextLength)
        {
            throw new InvalidOperationException(
                $"the prompt (~{promptTokens} tokens) and the output budget ({maxTokens} tokens) exceed the model context ({contextLength} tokens); the pass was not sent");
        }

        return await _textCompletionService.GenerateAsync(prompt, settings with { MaxTokens = maxTokens }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Output budget for rewriting <paramref name="text"/>: two characters per token plus headroom. Generous on purpose
    /// — unused budget costs nothing, and CJK text runs well under four characters per token.
    /// </summary>
    internal static int OutputBudget(string text) => text.Length / 2 + 256;

    /// <summary>Prompt size for the context check: three characters per token.</summary>
    private static int PromptTokens(string prompt) => prompt.Length / 3;

    private static string SkipNote(string pass, Exception ex) => ex is Flux.Abstractions.TextCompletionTruncatedException
        ? $"{pass}: the response was truncated at the output token limit; the pass was not applied"
        : $"{pass}: {ex.Message}";

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to restore broken sentences; the pass was not applied")]
    private static partial void LogRestoreSentencesFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to remove noise; the pass was not applied")]
    private static partial void LogRemoveNoiseFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to correct OCR errors; the pass was not applied")]
    private static partial void LogCorrectOcrFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to restructure sections; the pass was not applied")]
    private static partial void LogRestructureSectionsFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to merge duplicates; the pass was not applied")]
    private static partial void LogMergeDuplicatesFailed(ILogger logger, Exception ex);

    #endregion

    [GeneratedRegex(@"[a-z,]\n[a-z]", RegexOptions.Compiled)]
    private static partial Regex BrokenSentenceRegex();

    [GeneratedRegex(@"[Il1O0]{2,}|[a-z]\s[a-z]\s[a-z]|rn(?=[aeiou])", RegexOptions.Compiled)]
    private static partial Regex OcrErrorPatternRegex();

    [GeneratedRegex(@"^#{1,6}\s", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex HeadingRegex();
}
