using FileFlux.Domain;
using FileFlux.Infrastructure.Services;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// Common helper methods for chunking strategies
/// Provides HeadingPath resolution, page tracking, and metadata enrichment
/// </summary>
public static class ChunkingHelper
{
    /// <summary>
    /// Get heading path for a position in the document
    /// </summary>
    public static List<string> GetHeadingPathForPosition(List<ContentSection> sections, int position)
    {
        var path = new List<string>();
        FindSectionPath(sections, position, path);
        return path;
    }

    private static bool FindSectionPath(List<ContentSection> sections, int position, List<string> path)
    {
        foreach (var section in sections)
        {
            if (position >= section.StartPosition && position < section.EndPosition)
            {
                path.Add(section.Title);
                if (section.Children.Count > 0)
                {
                    FindSectionPath(section.Children, position, path);
                }
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Get page number for a character position
    /// </summary>
    public static int? GetPageForPosition(Dictionary<int, (int Start, int End)> pageRanges, int position)
    {
        if (pageRanges.Count == 0)
            return null;

        foreach (var kvp in pageRanges)
        {
            if (position >= kvp.Value.Start && position <= kvp.Value.End)
                return kvp.Key;
        }
        return null;
    }

    /// <summary>
    /// Create source metadata info from document content
    /// </summary>
    public static SourceMetadataInfo CreateSourceInfo(DocumentContent content)
    {
        // Auto-detect language if not already set
        var language = content.Metadata.Language;
        var confidence = content.Metadata.LanguageConfidence;

        if (string.IsNullOrEmpty(language) || language == "unknown")
        {
            (language, confidence) = LanguageDetector.Detect(content.Text);
        }

        return new SourceMetadataInfo
        {
            SourceId = Guid.NewGuid().ToString(),
            SourceType = content.Metadata.FileType,
            Title = content.Metadata.Title ?? content.Metadata.FileName,
            FilePath = null,
            CreatedAt = content.Metadata.CreatedAt ?? DateTime.UtcNow,
            Language = language ?? "en",
            LanguageConfidence = confidence,
            WordCount = content.Metadata.WordCount > 0 ? content.Metadata.WordCount : CountWords(content.Text),
            PageCount = content.Metadata.PageCount > 0 ? content.Metadata.PageCount : null
        };
    }

    /// <summary>
    /// Count words in text
    /// </summary>
    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>
    /// Enrich a chunk with structural metadata
    /// </summary>
    public static void EnrichChunk(DocumentChunk chunk, DocumentContent content, int startPosition, int endPosition)
    {
        chunk.Location.HeadingPath = GetHeadingPathForPosition(content.Sections, startPosition);
        chunk.Location.StartPage = GetPageForPosition(content.PageRanges, startPosition);
        chunk.Location.EndPage = GetPageForPosition(content.PageRanges, endPosition - 1);
        chunk.ContextDependency = ContextDependencyAnalyzer.Calculate(chunk.Content, content.Metadata.Language);
        chunk.SourceInfo = CreateSourceInfo(content);
    }

    /// <summary>
    /// Update chunk count in all chunks' source info after all chunks are generated
    /// </summary>
    public static void UpdateChunkCount(IEnumerable<DocumentChunk> chunks)
    {
        var chunkList = chunks.ToList();
        var count = chunkList.Count;
        foreach (var chunk in chunkList)
        {
            chunk.SourceInfo.ChunkCount = count;
        }
    }
}
