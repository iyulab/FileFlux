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
