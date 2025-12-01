namespace FileFlux.Core;

/// <summary>
/// Standard property keys for DocumentChunk.Props dictionary.
/// Ensures consistent key naming across all chunking strategies and processors.
///
/// <para><b>Usage Guidelines:</b></para>
/// <list type="bullet">
///   <item>
///     <term>DocumentChunk.Props</term>
///     <description>Chunk-specific properties added during processing (enrichment, quality metrics, navigation)</description>
///   </item>
///   <item>
///     <term>DocumentMetadata.CustomProperties</term>
///     <description>Document-level metadata inherited from source (author, title, processing options)</description>
///   </item>
/// </list>
///
/// <para><b>Key Naming Convention:</b></para>
/// <list type="bullet">
///   <item>Format: "category.propertyName" (e.g., "enriched.summary", "quality.overall")</item>
///   <item>Categories: context, nav, quality, hierarchy, source, embedding, metadata, enriched, document</item>
/// </list>
/// </summary>
public static class ChunkPropsKeys
{
    // ========================================
    // Context-related keys
    // ========================================

    /// <summary>Breadcrumb path showing chunk location in document hierarchy</summary>
    public const string ContextBreadcrumb = "context.breadcrumb";

    /// <summary>Title of the source document</summary>
    public const string ContextDocumentTitle = "context.documentTitle";

    /// <summary>Type/category of the source document</summary>
    public const string ContextDocumentType = "context.documentType";

    // ========================================
    // Navigation keys for chunk linking
    // ========================================

    /// <summary>ID of the previous chunk in sequence</summary>
    public const string PreviousChunkId = "nav.previousChunkId";

    /// <summary>ID of the next chunk in sequence</summary>
    public const string NextChunkId = "nav.nextChunkId";

    /// <summary>ID of the parent chunk in hierarchy</summary>
    public const string ParentChunkId = "nav.parentChunkId";

    // ========================================
    // Quality metrics keys
    // ========================================

    /// <summary>Semantic completeness score (0.0-1.0)</summary>
    public const string QualitySemanticCompleteness = "quality.semanticCompleteness";

    /// <summary>Context independence score (0.0-1.0)</summary>
    public const string QualityContextIndependence = "quality.contextIndependence";

    /// <summary>Information density score (0.0-1.0)</summary>
    public const string QualityInformationDensity = "quality.informationDensity";

    /// <summary>Boundary sharpness score (0.0-1.0)</summary>
    public const string QualityBoundarySharpness = "quality.boundarySharpness";

    /// <summary>Overall quality score (0.0-1.0)</summary>
    public const string QualityOverall = "quality.overall";

    /// <summary>Relevance score for retrieval (0.0-1.0)</summary>
    public const string QualityRelevanceScore = "quality.relevanceScore";

    /// <summary>Completeness score for content coverage (0.0-1.0)</summary>
    public const string QualityCompleteness = "quality.completeness";

    // ========================================
    // Hierarchy keys
    // ========================================

    /// <summary>Level in document hierarchy (0 = root)</summary>
    public const string HierarchyLevel = "hierarchy.level";

    /// <summary>Type of hierarchy node (section, paragraph, etc.)</summary>
    public const string HierarchyType = "hierarchy.type";

    /// <summary>Path in hierarchy tree</summary>
    public const string HierarchyPath = "hierarchy.path";

    /// <summary>Chunk type classification</summary>
    public const string HierarchyChunkType = "hierarchy.chunkType";

    /// <summary>Group ID for merged chunks</summary>
    public const string MergeGroupId = "hierarchy.mergeGroupId";

    /// <summary>IDs of child chunks</summary>
    public const string ChildChunkIds = "hierarchy.childChunkIds";

    // ========================================
    // Source position keys
    // ========================================

    /// <summary>Starting page number in source document</summary>
    public const string SourceStartPage = "source.startPage";

    /// <summary>Ending page number in source document</summary>
    public const string SourceEndPage = "source.endPage";

    /// <summary>Starting character offset in source text</summary>
    public const string SourceStartOffset = "source.startOffset";

    /// <summary>Ending character offset in source text</summary>
    public const string SourceEndOffset = "source.endOffset";

    // ========================================
    // Embedding-related keys
    // ========================================

    /// <summary>Embedding vector (float array)</summary>
    public const string EmbeddingVector = "embedding.vector";

    /// <summary>Model used for embedding generation</summary>
    public const string EmbeddingModel = "embedding.model";

    /// <summary>Embedding vector dimensions</summary>
    public const string EmbeddingDimensions = "embedding.dimensions";

    // ========================================
    // Metadata keys
    // ========================================

    /// <summary>Detected language (ISO 639-1 code)</summary>
    public const string MetadataLanguage = "metadata.language";

    /// <summary>Estimated token count</summary>
    public const string MetadataTokenCount = "metadata.tokenCount";

    /// <summary>Word count</summary>
    public const string MetadataWordCount = "metadata.wordCount";

    /// <summary>Chunk creation timestamp</summary>
    public const string MetadataCreatedAt = "metadata.createdAt";

    // ========================================
    // Document-level keys (from parsed structure)
    // ========================================

    /// <summary>Document topic/subject</summary>
    public const string DocumentTopic = "document.topic";

    /// <summary>Document keywords extracted from structure</summary>
    public const string DocumentKeywords = "document.keywords";

    // ========================================
    // Content classification keys
    // ========================================

    /// <summary>Content type (text, code, table, list, heading)</summary>
    public const string ContentType = "content.type";

    /// <summary>Structural role (content, title, code_block, table_content, list_content)</summary>
    public const string StructuralRole = "content.structuralRole";

    // ========================================
    // Enrichment keys (from FluxImprover)
    // ========================================

    /// <summary>AI-generated summary of chunk content</summary>
    public const string EnrichedSummary = "enriched.summary";

    /// <summary>AI-extracted keywords (IReadOnlyList&lt;string&gt;)</summary>
    public const string EnrichedKeywords = "enriched.keywords";

    /// <summary>Contextualized text with surrounding context</summary>
    public const string EnrichedContextualText = "enriched.contextualText";

    /// <summary>AI-generated topics (string, comma-separated)</summary>
    public const string EnrichedTopics = "enriched.topics";

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
