namespace FileFlux.Core;

/// <summary>
/// Keys under which FileFlux writes values into <see cref="DocumentChunk.Props"/>.
/// Every key listed here is set by a FileFlux component; a key is absent from a chunk when the step that writes it did
/// not run or had nothing to record.
///
/// <para><b>Who writes which group:</b></para>
/// <list type="bullet">
///   <item>
///     <term><c>hierarchy.*</c></term>
///     <description>The chunking pipeline: the heading path of the chunk (<see cref="HierarchyPath"/>) and the Markdown
///     heading level lifted from its structural marker (<see cref="HierarchyHeadingLevel"/>).</description>
///   </item>
///   <item>
///     <term><c>document.*</c></term>
///     <description><c>FluxDocumentProcessor.ProcessAsync</c>, from the parsed document structure (topic and
///     keywords), on every chunk of the document. The stateful processor from <c>IDocumentProcessorFactory</c> does
///     not set them.</description>
///   </item>
///   <item>
///     <term><c>enriched.*</c></term>
///     <description>LLM enrichment (FluxImprover), when an enrichment service is configured and enrichment is enabled:
///     summary, keywords and contextual text, by both processors (the CLI sets summary and keywords). The pre-assessment quality score
///     (<see cref="QualityScore"/>) and the skip flag (<see cref="EnrichmentSkipped"/>) are set only by
///     <c>FluxDocumentProcessor.ProcessAsync</c> with <c>ChunkingOptions.EnableConditionalEnrichment</c>.</description>
///   </item>
/// </list>
///
/// <para>Document-level metadata inherited from the source (author, title, processing options) lives in
/// <c>DocumentMetadata.CustomProperties</c>, not here.</para>
/// </summary>
public static class ChunkPropsKeys
{
    // ========================================
    // Hierarchy keys (chunking pipeline)
    // ========================================

    /// <summary>Heading path of the chunk, joined with <c>" &gt; "</c> (string).</summary>
    public const string HierarchyPath = "hierarchy.path";

    /// <summary>Markdown heading level (1-6) lifted from the chunk's structural marker before the marker is stripped from content.</summary>
    public const string HierarchyHeadingLevel = "hierarchy.headingLevel";

    // ========================================
    // Document-level keys (from parsed structure)
    // ========================================

    /// <summary>Document topic/subject</summary>
    public const string DocumentTopic = "document.topic";

    /// <summary>Document keywords extracted from structure</summary>
    public const string DocumentKeywords = "document.keywords";

    // ========================================
    // Enrichment keys (from FluxImprover)
    // ========================================

    /// <summary>AI-generated summary of chunk content</summary>
    public const string EnrichedSummary = "enriched.summary";

    /// <summary>AI-extracted keywords (IReadOnlyList&lt;string&gt;)</summary>
    public const string EnrichedKeywords = "enriched.keywords";

    /// <summary>Contextualized text with surrounding context</summary>
    public const string EnrichedContextualText = "enriched.contextualText";

    /// <summary>Quality score from the conditional-enrichment pre-assessment (float, 0.0-1.0)</summary>
    public const string QualityScore = "enriched.qualityScore";

    /// <summary>Whether conditional enrichment skipped the chunk because its quality score was high enough (bool)</summary>
    public const string EnrichmentSkipped = "enriched.skipped";

    // ========================================
    // Helper Methods
    // ========================================

    /// <summary>
    /// Check if chunk has any enrichment data from FluxImprover.
    /// </summary>
    /// <param name="props">The Props dictionary to check</param>
    /// <returns>True if any enrichment property is present</returns>
    public static bool HasEnrichment(IDictionary<string, object> props)
        => props.ContainsKey(EnrichedSummary) ||
           props.ContainsKey(EnrichedKeywords) ||
           props.ContainsKey(EnrichedContextualText);

    /// <summary>
    /// Try to get a typed value from Props dictionary.
    /// </summary>
    /// <typeparam name="T">Expected type of the value</typeparam>
    /// <param name="props">The Props dictionary</param>
    /// <param name="key">The property key</param>
    /// <param name="value">The typed value if found and type matches</param>
    /// <returns>True if key exists and value is of type T</returns>
    public static bool TryGetValue<T>(IDictionary<string, object> props, string key, out T? value)
    {
        if (props.TryGetValue(key, out var obj) && obj is T typedValue)
        {
            value = typedValue;
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    /// Get a typed value from Props dictionary or default.
    /// </summary>
    /// <typeparam name="T">Expected type of the value</typeparam>
    /// <param name="props">The Props dictionary</param>
    /// <param name="key">The property key</param>
    /// <param name="defaultValue">Default value if not found</param>
    /// <returns>The typed value or default</returns>
    public static T GetValueOrDefault<T>(IDictionary<string, object> props, string key, T defaultValue = default!)
        => TryGetValue<T>(props, key, out var value) ? value! : defaultValue;
}
