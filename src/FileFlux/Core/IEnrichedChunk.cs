namespace FileFlux.Core;

/// <summary>
/// FluxIndex 연동을 위한 강화된 청크 계약
/// RAG 시스템에서 필요한 모든 메타데이터를 표준화된 형태로 제공
/// </summary>
public interface IEnrichedChunk
{
    /// <summary>
    /// Chunk content text
    /// </summary>
    string Content { get; }

    /// <summary>
    /// Unique chunk identifier
    /// </summary>
    string ChunkId { get; }

    /// <summary>
    /// Zero-based chunk index in document
    /// </summary>
    int ChunkIndex { get; }

    /// <summary>
    /// Hierarchical heading path from document root
    /// Example: ["1장 서론", "1.2 배경", "1.2.1 연구 목적"]
    /// </summary>
    IReadOnlyList<string> HeadingPath { get; }

    /// <summary>
    /// Current section title (last element of HeadingPath)
    /// </summary>
    string? SectionTitle { get; }

    /// <summary>
    /// Start page number (1-based, null if not applicable)
    /// </summary>
    int? StartPage { get; }

    /// <summary>
    /// End page number (for chunks spanning multiple pages)
    /// </summary>
    int? EndPage { get; }

    /// <summary>
    /// Overall chunk quality score (0.0 - 1.0)
    /// </summary>
    double Quality { get; }

    /// <summary>
    /// Context dependency score (0.0 - 1.0)
    /// Higher values indicate more reliance on surrounding context
    /// Used to determine whether LLM-based contextual header is needed
    /// </summary>
    double ContextDependency { get; }

    /// <summary>
    /// Estimated token count
    /// </summary>
    int TokenCount { get; }

    /// <summary>
    /// Source document metadata
    /// </summary>
    ISourceMetadata Source { get; }
}

/// <summary>
/// Source document metadata for traceability and filtering
/// </summary>
public interface ISourceMetadata
{
    /// <summary>
    /// Unique source document identifier
    /// </summary>
    string SourceId { get; }

    /// <summary>
    /// Document type (PDF, DOCX, MD, etc.)
    /// </summary>
    string SourceType { get; }

    /// <summary>
    /// Document title
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Original file path (if available)
    /// </summary>
    string? FilePath { get; }

    /// <summary>
    /// Document creation timestamp
    /// </summary>
    DateTime CreatedAt { get; }

    /// <summary>
    /// Detected language (ISO 639-1 code: "ko", "en", "ja")
    /// </summary>
    string Language { get; }

    /// <summary>
    /// Language detection confidence (0.0 - 1.0)
    /// </summary>
    double LanguageConfidence { get; }

    /// <summary>
    /// Total word count in document
    /// </summary>
    int WordCount { get; }

    /// <summary>
    /// Total number of chunks generated
    /// </summary>
    int ChunkCount { get; }

    /// <summary>
    /// Total page count (if applicable)
    /// </summary>
    int? PageCount { get; }
}

/// <summary>
/// Hierarchical chunk interface for parent-child relationships
/// Enables multi-level granularity: small chunks for retrieval, larger parent chunks for context
/// </summary>
public interface IHierarchicalChunk : IEnrichedChunk
{
    /// <summary>
    /// Parent chunk ID (null for root/document-level chunks)
    /// </summary>
    string? ParentChunkId { get; }

    /// <summary>
    /// Child chunk IDs (empty list for leaf chunks)
    /// </summary>
    IReadOnlyList<string> ChildChunkIds { get; }

    /// <summary>
    /// Hierarchy level in document structure
    /// 0 = document level (largest context)
    /// 1 = section level
    /// 2 = subsection level
    /// 3 = paragraph level (smallest, most granular)
    /// </summary>
    int HierarchyLevel { get; }

    /// <summary>
    /// Type of chunk in the hierarchy
    /// </summary>
    HierarchyChunkType ChunkType { get; }

    /// <summary>
    /// Merge group ID for auto-merging related chunks during retrieval
    /// Chunks with the same merge group can be combined for richer context
    /// </summary>
    string? MergeGroupId { get; }
}

/// <summary>
/// Defines the type of chunk within a hierarchical structure
/// </summary>
public enum HierarchyChunkType
{
    /// <summary>
    /// Root/document-level chunk containing overview
    /// </summary>
    Root = 0,

    /// <summary>
    /// Parent chunk that has children (section or subsection header with summary)
    /// </summary>
    Parent = 1,

    /// <summary>
    /// Leaf chunk with no children (actual content for retrieval)
    /// </summary>
    Leaf = 2,

    /// <summary>
    /// Summary chunk aggregating child content
    /// </summary>
    Summary = 3
}


/// <summary>
/// Standard keys for DocumentChunk.Props dictionary.
/// Use these constants instead of magic strings for consistency.
/// </summary>
public static class ChunkPropsKeys
{
    // ============================================
    // Chunk Relationships (Phase A)
    // ============================================

    /// <summary>
    /// Previous chunk ID in sequence (string, Guid format)
    /// </summary>
    public const string PreviousChunkId = "relations_previous_id";

    /// <summary>
    /// Next chunk ID in sequence (string, Guid format)
    /// </summary>
    public const string NextChunkId = "relations_next_id";

    /// <summary>
    /// Parent chunk ID for hierarchical structure (string, Guid format)
    /// </summary>
    public const string ParentChunkId = "relations_parent_id";

    /// <summary>
    /// Child chunk IDs for hierarchical structure (List&lt;string&gt;)
    /// </summary>
    public const string ChildChunkIds = "relations_child_ids";

    // ============================================
    // Context Information (Phase A)
    // ============================================

    /// <summary>
    /// Full breadcrumb path as string (e.g., "Document > Chapter 1 > Section 1.2")
    /// </summary>
    public const string ContextBreadcrumb = "context_breadcrumb";

    /// <summary>
    /// Document title for context
    /// </summary>
    public const string ContextDocumentTitle = "context_document_title";

    /// <summary>
    /// Document type/category (e.g., "financial_report", "technical_documentation")
    /// </summary>
    public const string ContextDocumentType = "context_document_type";

    // ============================================
    // Hierarchy Information (Phase B)
    // ============================================

    /// <summary>
    /// Hierarchy level (0=document, 1=section, 2=subsection, 3=paragraph)
    /// </summary>
    public const string HierarchyLevel = "hierarchy_level";

    /// <summary>
    /// Chunk type in hierarchy ("parent", "child", "leaf")
    /// </summary>
    public const string HierarchyChunkType = "hierarchy_chunk_type";

    /// <summary>
    /// Merge group ID for auto-merge functionality (Guid format)
    /// </summary>
    public const string MergeGroupId = "merge_group_id";

    // ============================================
    // Quality Metrics (Phase C)
    // ============================================

    /// <summary>
    /// Semantic completeness score (0.0 - 1.0)
    /// </summary>
    public const string QualitySemanticCompleteness = "quality_semantic_completeness";

    /// <summary>
    /// Context independence score (0.0 - 1.0) - can chunk be understood alone?
    /// </summary>
    public const string QualityContextIndependence = "quality_context_independence";

    /// <summary>
    /// Information density score (0.0 - 1.0)
    /// </summary>
    public const string QualityInformationDensity = "quality_information_density";

    /// <summary>
    /// Boundary sharpness score (0.0 - 1.0) - clean semantic boundaries?
    /// </summary>
    public const string QualityBoundarySharpness = "quality_boundary_sharpness";

    // ============================================
    // Existing Keys (for reference - gradual migration)
    // ============================================

    /// <summary>
    /// Auto-selected chunking strategy name
    /// </summary>
    public const string AutoSelectedStrategy = "AutoSelectedStrategy";

    /// <summary>
    /// Selection reasoning for auto strategy
    /// </summary>
    public const string SelectionReasoning = "SelectionReasoning";

    /// <summary>
    /// Selection confidence score
    /// </summary>
    public const string SelectionConfidence = "SelectionConfidence";

    /// <summary>
    /// Detected document domain (e.g., "Technical", "Legal", "Medical")
    /// </summary>
    public const string Domain = "Domain";

    /// <summary>
    /// Contextual header for RAG retrieval
    /// </summary>
    public const string ContextualHeader = "ContextualHeader";

    /// <summary>
    /// Structural role of the chunk (e.g., "introduction", "conclusion", "body")
    /// </summary>
    public const string StructuralRole = "StructuralRole";

    /// <summary>
    /// Relevance score for search
    /// </summary>
    public const string RelevanceScore = "RelevanceScore";

    /// <summary>
    /// Technical keywords extracted from chunk
    /// </summary>
    public const string TechnicalKeywords = "TechnicalKeywords";

    /// <summary>
    /// Completeness score
    /// </summary>
    public const string Completeness = "Completeness";

    /// <summary>
    /// Semantic coherence score
    /// </summary>
    public const string SemanticCoherence = "SemanticCoherence";

    /// <summary>
    /// Sentence integrity score
    /// </summary>
    public const string SentenceIntegrity = "SentenceIntegrity";

    /// <summary>
    /// RAG suitability score
    /// </summary>
    public const string RagSuitability = "RagSuitability";

    /// <summary>
    /// Quality grade (A, B, C, D, F)
    /// </summary>
    public const string QualityGrade = "QualityGrade";
}
