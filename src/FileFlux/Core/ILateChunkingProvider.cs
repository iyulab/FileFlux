using FileFlux.Domain;

namespace FileFlux.Core;

/// <summary>
/// Interface for Late Chunking workflow support.
/// Late Chunking (Jina AI, 2024) embeds entire document first, then splits at boundaries.
/// This preserves long-range contextual dependencies in embeddings.
/// </summary>
public interface ILateChunkingProvider
{
    /// <summary>
    /// Prepare a document for late chunking by identifying chunk boundaries without splitting.
    /// Consumer's embedding model processes the full document, then splits at boundaries.
    /// </summary>
    /// <param name="content">Document content to prepare</param>
    /// <param name="options">Chunking options to determine boundaries</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Document with marked chunk boundaries for embedding-first workflow</returns>
    Task<LateChunkingDocument> PrepareForLateChunkingAsync(
        DocumentContent content,
        ChunkingOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Document prepared for Late Chunking workflow.
/// Contains full document text with marked chunk boundaries.
/// Consumer embeds full text first, then extracts embeddings at boundaries.
/// </summary>
public class LateChunkingDocument
{
    /// <summary>
    /// Full document text for embedding (no chunking applied)
    /// </summary>
    public string FullText { get; init; } = string.Empty;

    /// <summary>
    /// Character-level chunk boundaries within the full text
    /// </summary>
    public IReadOnlyList<ChunkBoundary> Boundaries { get; init; } = Array.Empty<ChunkBoundary>();

    /// <summary>
    /// Source document metadata
    /// </summary>
    public ISourceMetadata Source { get; init; } = null!;

    /// <summary>
    /// Total estimated token count for the full document
    /// </summary>
    public int TotalTokenCount { get; init; }

    /// <summary>
    /// Strategy used to determine boundaries
    /// </summary>
    public string BoundaryStrategy { get; init; } = string.Empty;

    /// <summary>
    /// Extract text for a specific boundary
    /// </summary>
    public string GetBoundaryText(int boundaryIndex)
    {
        if (boundaryIndex < 0 || boundaryIndex >= Boundaries.Count)
            throw new ArgumentOutOfRangeException(nameof(boundaryIndex));

        var boundary = Boundaries[boundaryIndex];
        return FullText.Substring(boundary.StartChar, boundary.EndChar - boundary.StartChar);
    }

    /// <summary>
    /// Get all boundary texts as enumerable
    /// </summary>
    public IEnumerable<string> GetAllBoundaryTexts()
    {
        foreach (var boundary in Boundaries)
        {
            yield return FullText.Substring(boundary.StartChar, boundary.EndChar - boundary.StartChar);
        }
    }
}

/// <summary>
/// Represents a chunk boundary within the document.
/// Used to mark where chunks should be split after embedding.
/// </summary>
public class ChunkBoundary
{
    /// <summary>
    /// Start character position (0-based, inclusive)
    /// </summary>
    public int StartChar { get; init; }

    /// <summary>
    /// End character position (exclusive)
    /// </summary>
    public int EndChar { get; init; }

    /// <summary>
    /// Estimated token count for this boundary segment
    /// </summary>
    public int TokenCount { get; init; }

    /// <summary>
    /// Section title if applicable
    /// </summary>
    public string? SectionTitle { get; init; }

    /// <summary>
    /// Heading path from document root
    /// </summary>
    public IReadOnlyList<string> HeadingPath { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Boundary quality score (0.0 - 1.0)
    /// Higher scores indicate cleaner semantic boundaries
    /// </summary>
    public double BoundaryQuality { get; init; } = 1.0;

    /// <summary>
    /// Hierarchy level (0=document, 1=section, 2=subsection, 3=paragraph)
    /// </summary>
    public int HierarchyLevel { get; init; }

    /// <summary>
    /// Sequential index of this boundary
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Context breadcrumb string
    /// </summary>
    public string? ContextBreadcrumb { get; init; }

    /// <summary>
    /// Character length of this boundary segment
    /// </summary>
    public int Length => EndChar - StartChar;
}

/// <summary>
/// Options for late chunking boundary detection
/// </summary>
public class LateChunkingOptions
{
    /// <summary>
    /// Maximum characters per boundary segment
    /// </summary>
    public int MaxBoundarySize { get; set; } = 1500;

    /// <summary>
    /// Minimum characters per boundary segment
    /// </summary>
    public int MinBoundarySize { get; set; } = 100;

    /// <summary>
    /// Target overlap between adjacent boundaries (in characters)
    /// </summary>
    public int OverlapSize { get; set; } = 200;

    /// <summary>
    /// Whether to respect sentence boundaries
    /// </summary>
    public bool RespectSentenceBoundaries { get; set; } = true;

    /// <summary>
    /// Whether to respect paragraph boundaries
    /// </summary>
    public bool RespectParagraphBoundaries { get; set; } = true;

    /// <summary>
    /// Whether to include section headers in boundaries
    /// </summary>
    public bool PreserveSectionHeaders { get; set; } = true;
}
