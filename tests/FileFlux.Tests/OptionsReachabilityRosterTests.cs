using System.Reflection;
using Iyu.Conventions.Testing;
using Xunit;

namespace FileFlux.Tests;

/// <summary>
/// Every public option in this library is read by the library. An option nothing reads is a promise it does not keep:
/// a caller sets it, and nothing changes and nothing is reported. The roster fails both ways — a new unread option,
/// and a listed one that has since been wired — so each change is recorded on purpose.
/// </summary>
public class OptionsReachabilityRosterTests
{
    private static readonly Assembly[] Libraries =
    [
        Assembly.Load("FileFlux"),
        Assembly.Load("FileFlux.Core"),
    ];

    /// <summary>
    /// Options accepted as unread today. Shrink this list; never grow it silently.
    /// <para>
    /// Everything below is the roster's opening baseline (2026-09-20), recorded as found rather than
    /// as judged: this library had no reachability roster, and its first run reported 58 unread public
    /// options across 14 types. None of them has been investigated yet, so none carries a reason of its
    /// own — recording them as "known" here is what makes the gate start green and makes the *next*
    /// unread option a failure instead of silently joining a crowd.
    /// </para>
    /// <para>
    /// Three were spot-checked to confirm the scanner is not producing noise:
    /// <c>ChunkingOptions.MaxHeadingLevel</c> (declared with a default of 3; the only
    /// <c>MaxHeadingLevel</c> anything reads belongs to a different type, so the duplicate name hides
    /// it from a reader), <c>ChunkingOptions.DeduplicateOverlaps</c> and
    /// <c>DocumentCacheOptions.MinHitRatio</c> (declaration only). Working through the rest — wiring
    /// each or deleting it — is tracked in the umbrella's issue draft.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string[]> KnownUnread = new()
    {
        ["FileFlux.Core.ChunkingOptions"] =
        [
            "DeduplicateOverlaps", "MaxHeaderParagraphLength", "MaxHeaderParagraphs", "MaxHeadingLevel",
            "RecognizeKoreanSectionMarkers", "SeparateDocumentHeader", "StrategyOptions",
        ],
        ["FileFlux.Core.ExtractOptions"] =
        [
            "CustomOptions", "DetectBlockTypes", "ExtractImages", "ExtractTables", "MaxImageSize",
            "MinTableConfidence", "PageRange", "PreserveCoordinates",
        ],
        ["FileFlux.Core.GraphBuildOptions"] = ["IncludeReferenceEdges"],
        ["FileFlux.Core.HierarchicalEnrichmentOptions"] =
        [
            "EnrichParentsFirst", "MaxDepth", "PropagateParentKeywords", "PropagateParentSummary",
        ],
        ["FileFlux.Core.LateChunkingOptions"] =
        [
            "MaxBoundarySize", "MinBoundarySize", "OverlapSize", "PreserveSectionHeaders",
            "RespectParagraphBoundaries", "RespectSentenceBoundaries",
        ],
        ["FileFlux.Core.LlmRefineOptions"] =
        [
            "CustomInstructions", "DocumentType", "MaxTokens", "PreserveFormatting", "TargetLanguage",
            "Temperature", "VerboseLogging",
        ],
        ["FileFlux.Core.MetadataEnrichmentOptions"] = ["EnableAdaptiveSampling", "MaxTokens"],
        ["FileFlux.Core.ParsingOptions"] = ["Extra", "LlmModel", "MaxTokens", "Temperature"],
        ["FileFlux.Core.RefineOptions"] = ["LlmModel", "MaxLlmTokens", "ProcessImages"],
        ["FileFlux.Core.RefiningOptions"] = ["UseAIForDescriptions", "UseAIForOCRCorrection"],
        ["FileFlux.DocumentCacheOptions"] = ["MinHitRatio"],
        ["FileFlux.DocumentParsingOptions"] =
        [
            "CustomSettings", "DocumentTypeHint", "ExtractMetadata", "Language", "StructuringLevel",
        ],
        ["FileFlux.EmbeddingOptions"] = ["Dimensions", "Model", "Normalize", "Pooling"],
        ["FileFlux.ImageToTextOptions"] = ["CustomOptions", "ExtractMetadata", "Language"],
    };

    [Fact]
    public void EveryPublicOption_IsRead() =>
        OptionsReachability.Scan(Libraries, OptionsTypes.NamedWith("Options", "Config"))
            .ShouldMatchRoster(KnownUnread);
}
