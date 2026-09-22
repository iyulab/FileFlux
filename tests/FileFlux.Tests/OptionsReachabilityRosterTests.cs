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
        // Verdicts (run 58 cycle-920, FileFlux draft 20260920-143000): A = no implementation anywhere,
        // B = the feature is driven by another type's member, C = a same-class duplicate, D = a hard-coded literal
        // sits where the option should be read. Six D entries were wired in 0.24.2 and left this list.
        // ChunkingOptions: DeduplicateOverlaps B (RefiningOptions.TextRefinementPreset -> FluxCurator RemoveDuplicateLines) ·
        // MaxHeaderParagraphLength A · MaxHeaderParagraphs A · MaxHeadingLevel A (FluxCurator has no heading knob;
        // MarkdownConversionOptions.MaxHeadingLevel is a different concept) · RecognizeKoreanSectionMarkers B (LanguageCode) ·
        // SeparateDocumentHeader A · StrategyOptions A (CustomProperties is the working bag).
        ["FileFlux.Core.ChunkingOptions"] =
        [
            "MaxHeaderParagraphLength", "MaxHeaderParagraphs", "MaxHeadingLevel",
            "SeparateDocumentHeader", "StrategyOptions",
        ],
        // ExtractOptions: CustomOptions A · DetectBlockTypes A (RawContent.Blocks never populated) · ExtractTables A
        // (RawContent.Tables never populated) · MinTableConfidence A · PageRange D (PdfDocumentReader ExtractPerPage
        // all-pages loop + whole-document fast path — next) · PreserveCoordinates A. ExtractImages/MaxImageSize wired
        // in 0.24.2 (ImageExtractionPolicy, every reader).
        ["FileFlux.Core.ExtractOptions"] =
        [
            "CustomOptions", "DetectBlockTypes", "ExtractTables",
            "MinTableConfidence", "PageRange", "PreserveCoordinates",
        ],
        // GraphBuildOptions.IncludeReferenceEdges A (four gated edge builders, none for Reference).
        ["FileFlux.Core.GraphBuildOptions"] = ["IncludeReferenceEdges"],
        // HierarchicalEnrichmentOptions: EnrichParentsFirst D (OrderForHierarchicalEnrichment orders parents first
        // unconditionally — but the method has no callers) · MaxDepth A · PropagateParentKeywords A · PropagateParentSummary A.
        // The type reaches no interface method: type-level dead weight.
        ["FileFlux.Core.HierarchicalEnrichmentOptions"] =
        [
            "EnrichParentsFirst", "MaxDepth", "PropagateParentKeywords", "PropagateParentSummary",
        ],
        // LateChunkingOptions: all A — ILateChunkingProvider takes ChunkingOptions, this type reaches nothing
        // (OverlapSize/PreserveSectionHeaders/RespectParagraph|SentenceBoundaries have live twins on ChunkingOptions).
        ["FileFlux.Core.LateChunkingOptions"] =
        [
            "MaxBoundarySize", "MinBoundarySize", "OverlapSize", "PreserveSectionHeaders",
            "RespectParagraphBoundaries", "RespectSentenceBoundaries",
        ],
        // LlmRefineOptions: CustomInstructions A · DocumentType A · TargetLanguage A · VerboseLogging A (ILogger level is
        // the gate). PreserveFormatting wired in 0.24.2; MaxTokens / Temperature wired in 0.25.0
        // (IDocumentAnalysisService.GenerateAsync(prompt, GenerationSettings, ct)).
        ["FileFlux.Core.LlmRefineOptions"] =
        [
            "CustomInstructions", "DocumentType", "TargetLanguage", "VerboseLogging",
        ],
        // MetadataEnrichmentOptions.EnableAdaptiveSampling A. MaxTokens wired in 0.24.2 (TruncateContent budget).
        ["FileFlux.Core.MetadataEnrichmentOptions"] =
        [
            "EnableAdaptiveSampling",
        ],
        // ParsingOptions: Extra A. LlmModel B removed in 0.25.0 (the model is the analysis service's) ·
        // MaxTokens / Temperature wired in 0.25.0 (DocumentProcessor copies them into DocumentParsingOptions).
        ["FileFlux.Core.ParsingOptions"] =
        [
            "Extra",
        ],
        // RefineOptions: LlmModel A · MaxLlmTokens A (DocumentRefiner never calls an LLM). ProcessImages B removed in 0.25.0
        // (RefiningOptions.ProcessImagesToText).
        ["FileFlux.Core.RefineOptions"] =
        [
            "LlmModel", "MaxLlmTokens",
        ],
        // RefiningOptions: UseAIForDescriptions C (duplicate of ProcessImagesToText) and UseAIForOCRCorrection B
        // (LlmRefineOptions.CorrectOcrErrors) removed in 0.25.0.
        // DocumentCacheOptions.MinHitRatio A (no hit ratio is computed).
        ["FileFlux.DocumentCacheOptions"] = ["MinHitRatio"],
        // DocumentParsingOptions: CustomSettings A · StructuringLevel A (one prompt for every level). ExtractMetadata wired
        // in 0.24.2; DocumentTypeHint B (InferType always) and Language B (LanguageDetector always) removed in 0.25.0.
        ["FileFlux.DocumentParsingOptions"] =
        [
            "CustomSettings", "StructuringLevel",
        ],
        // EmbeddingOptions: all A — IEmbeddingService takes EmbeddingPurpose, this type reaches nothing (type-level dead weight;
        // Dimensions/Model live on the loaded model and LMSupplyOptions.EmbeddingModel).
        ["FileFlux.EmbeddingOptions"] =
        [
            "Dimensions", "Model", "Normalize", "Pooling",
        ],
        // ImageToTextOptions: CustomOptions A · Language D — a contract member the consumer's IImageToTextService reads
        // (the CLI's three vision providers do); the library's five call sites pass the literal "auto" where a pipeline
        // knob would be, and the shipped LMSupply OCR takes its hint from LMSupplyOptions.OcrLanguageHint at load time.
        // Kept (re-verdict 0.25.0: not B — nothing else is the knob). ExtractMetadata wired in 0.24.2 (OCR + captioner).
        ["FileFlux.ImageToTextOptions"] =
        [
            "CustomOptions", "Language",
        ],
        // LMSupplyOptions.AutoSelectMultilingualModel A (read only inside GetEmbeddingModelForLanguage, which has no callers).
        ["FileFlux.Providers.LMSupply.LMSupplyOptions"] = ["AutoSelectMultilingualModel"],
    };

    [Fact]
    public void EveryPublicOption_IsRead() =>
        OptionsReachability.Scan(Libraries, OptionsTypes.NamedWith("Options", "Config"))
            .ShouldMatchRoster(KnownUnread);
}
