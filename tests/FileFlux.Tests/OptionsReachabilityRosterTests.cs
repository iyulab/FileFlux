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
        Assembly.Load("FileFlux.Providers.LMSupply"),
    ];

    /// <summary>
    /// Options accepted as unread today. Shrink this list; never grow it silently.
    /// <para>
    /// Opening baseline (2026-09-20): 58 unread public options across 15 types, recorded as found rather than
    /// as judged - none has been investigated, so none carries a reason of its own. Recording them is what makes
    /// the gate start green and makes the *next* unread option a failure instead of silently joining a crowd.
    /// </para>
    /// <para>
    /// The assembly list above must cover every assembly this repository ships. Scanning only the main one
    /// reports options that a sibling assembly reads as unread - that mistake inflated an early baseline elsewhere threefold.
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
        ["FileFlux.Core.MetadataEnrichmentOptions"] =
        [
            "EnableAdaptiveSampling", "MaxTokens",
        ],
        ["FileFlux.Core.ParsingOptions"] =
        [
            "Extra", "LlmModel", "MaxTokens", "Temperature",
        ],
        ["FileFlux.Core.RefineOptions"] =
        [
            "LlmModel", "MaxLlmTokens", "ProcessImages",
        ],
        ["FileFlux.Core.RefiningOptions"] =
        [
            "UseAIForDescriptions", "UseAIForOCRCorrection",
        ],
        ["FileFlux.DocumentCacheOptions"] = ["MinHitRatio"],
        ["FileFlux.DocumentParsingOptions"] =
        [
            "CustomSettings", "DocumentTypeHint", "ExtractMetadata", "Language", "StructuringLevel",
        ],
        ["FileFlux.EmbeddingOptions"] =
        [
            "Dimensions", "Model", "Normalize", "Pooling",
        ],
        ["FileFlux.ImageToTextOptions"] =
        [
            "CustomOptions", "ExtractMetadata", "Language",
        ],
        ["FileFlux.Providers.LMSupply.LMSupplyOptions"] = ["AutoSelectMultilingualModel"],
    };

    [Fact]
    public void EveryPublicOption_IsRead() =>
        OptionsReachability.Scan(Libraries, OptionsTypes.NamedWith("Options", "Config"))
            .ShouldMatchRoster(KnownUnread);
}
