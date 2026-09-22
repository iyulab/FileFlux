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
        // 0.25.0 (run 58 cycle-920~924) verdicts for what used to be here — A = no implementation anywhere, B = another
        // member drives it, C = a same-class duplicate, D = a literal sits where the option should be read:
        // ChunkingOptions: MaxHeaderParagraphLength/MaxHeaderParagraphs/SeparateDocumentHeader/MaxHeadingLevel/
        //   StrategyOptions A — removed (CustomProperties is the one bag; MarkdownConversionOptions.MaxHeadingLevel is a
        //   different, read concept). DeduplicateOverlaps/RecognizeKoreanSectionMarkers B — removed.
        // ExtractOptions: ExtractTables/DetectBlockTypes/PreserveCoordinates/MinTableConfidence/CustomOptions A — removed
        //   (no reader fills RawContent.Tables/Blocks). ExtractImages/MaxImageSize wired 0.24.2 (ImageExtractionPolicy).
        // GraphBuildOptions.IncludeReferenceEdges A (three edge builders, none for Reference) — removed.
        // LlmRefineOptions: VerboseLogging A — removed. CustomInstructions/DocumentType/TargetLanguage are CONTRACT
        //   members: FluxIndex.Integrations.FileFlux's LlmRefinerAdapter (an ILlmRefiner) reads all three into its prompt;
        //   this repository's own LlmRefiner did not (D) — wiring next (kept below until then). MaxTokens/Temperature wired 0.25.0.
        // MetadataEnrichmentOptions.EnableAdaptiveSampling A — removed (ExtractionStrategy is the sampling knob).
        // ParsingOptions.Extra A — removed. RefineOptions.LlmModel/MaxLlmTokens A (DocumentRefiner calls no LLM) — removed.
        // DocumentCacheOptions.MinHitRatio A (no hit ratio is computed) — removed.
        // DocumentParsingOptions.CustomSettings/StructuringLevel (+ enum) A — removed (BasicDocumentParser has one prompt).
        // ImageToTextOptions.CustomOptions A — removed (no implementation in the tree reads it, unlike Language).
        // LMSupplyOptions.AutoSelectMultilingualModel A — removed with GetEmbeddingModelForLanguage (no callers); the CLI
        //   set it to true and never got a per-language model (one index = one vector space, by design).
        // Types removed whole: LateChunkingOptions, HierarchicalEnrichmentOptions, EmbeddingOptions (+ PoolingStrategy).
        //
        // Still here, each with its reason:
        // ExtractOptions.PageRange D — PdfDocumentReader's ExtractPerPage all-pages loop + whole-document fast path;
        //   needs a multi-page PDF fixture to wire with a fact.
        ["FileFlux.Core.ExtractOptions"] = ["PageRange"],
        // LlmRefineOptions.CustomInstructions/DocumentType/TargetLanguage D — contract members (see above); wiring next.
        ["FileFlux.Core.LlmRefineOptions"] = ["CustomInstructions", "DocumentType", "TargetLanguage"],
        // ImageToTextOptions.Language D — a contract member the consumer's IImageToTextService reads (the CLI's three
        // vision providers do); the library's five call sites pass the literal "auto" where a pipeline knob would be, and
        // the shipped LMSupply OCR takes its hint from LMSupplyOptions.OcrLanguageHint at load time (knob design open).
        ["FileFlux.ImageToTextOptions"] = ["Language"],
    };

    [Fact]
    public void EveryPublicOption_IsRead() =>
        OptionsReachability.Scan(Libraries, OptionsTypes.NamedWith("Options", "Config"))
            .ShouldMatchRoster(KnownUnread);
}
