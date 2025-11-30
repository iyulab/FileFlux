namespace FileFlux.Core;

/// <summary>
/// Standard property keys for DocumentChunk.Props dictionary.
/// Ensures consistent key naming across all chunking strategies and processors.
/// </summary>
public static class ChunkPropsKeys
{
    // Context-related keys
    public const string ContextBreadcrumb = "context.breadcrumb";
    public const string ContextDocumentTitle = "context.documentTitle";
    public const string ContextDocumentType = "context.documentType";

    // Navigation keys for chunk linking
    public const string PreviousChunkId = "nav.previousChunkId";
    public const string NextChunkId = "nav.nextChunkId";
    public const string ParentChunkId = "nav.parentChunkId";

    // Quality metrics keys
    public const string QualitySemanticCompleteness = "quality.semanticCompleteness";
    public const string QualityContextIndependence = "quality.contextIndependence";
    public const string QualityInformationDensity = "quality.informationDensity";
    public const string QualityBoundarySharpness = "quality.boundarySharpness";
    public const string QualityOverall = "quality.overall";

    // Hierarchy keys
    public const string HierarchyLevel = "hierarchy.level";
    public const string HierarchyType = "hierarchy.type";
    public const string HierarchyPath = "hierarchy.path";
    public const string HierarchyChunkType = "hierarchy.chunkType";
    public const string MergeGroupId = "hierarchy.mergeGroupId";
    public const string ChildChunkIds = "hierarchy.childChunkIds";

    // Source position keys
    public const string SourceStartPage = "source.startPage";
    public const string SourceEndPage = "source.endPage";
    public const string SourceStartOffset = "source.startOffset";
    public const string SourceEndOffset = "source.endOffset";

    // Embedding-related keys
    public const string EmbeddingVector = "embedding.vector";
    public const string EmbeddingModel = "embedding.model";
    public const string EmbeddingDimensions = "embedding.dimensions";

    // Metadata keys
    public const string MetadataLanguage = "metadata.language";
    public const string MetadataTokenCount = "metadata.tokenCount";
    public const string MetadataWordCount = "metadata.wordCount";
    public const string MetadataCreatedAt = "metadata.createdAt";
}
