using FileFlux.Core;
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

        // Add context breadcrumb metadata
        EnrichWithContextMetadata(chunk, content.Metadata.Title ?? content.Metadata.FileName);
    }

    /// <summary>
    /// Build context breadcrumb string from document title and heading path
    /// Example: "Document Title > Chapter 1 > Section 1.1 > Subsection"
    /// </summary>
    /// <param name="documentTitle">Document title or filename</param>
    /// <param name="headingPath">List of section headings</param>
    /// <param name="separator">Separator string (default: " > ")</param>
    /// <returns>Formatted breadcrumb string</returns>
    public static string BuildContextBreadcrumb(string? documentTitle, IReadOnlyList<string> headingPath, string separator = " > ")
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(documentTitle))
        {
            parts.Add(documentTitle);
        }

        if (headingPath != null && headingPath.Count > 0)
        {
            parts.AddRange(headingPath.Where(h => !string.IsNullOrWhiteSpace(h)));
        }

        return parts.Count > 0 ? string.Join(separator, parts) : string.Empty;
    }

    /// <summary>
    /// Enrich chunk with context metadata in Props dictionary
    /// Adds context breadcrumb, document title, and optional document type
    /// </summary>
    public static void EnrichWithContextMetadata(DocumentChunk chunk, string? documentTitle = null, string? documentType = null)
    {
        // Build and store context breadcrumb
        var breadcrumb = BuildContextBreadcrumb(documentTitle, chunk.Location.HeadingPath);
        if (!string.IsNullOrEmpty(breadcrumb))
        {
            chunk.Props[ChunkPropsKeys.ContextBreadcrumb] = breadcrumb;
        }

        // Store document title
        if (!string.IsNullOrWhiteSpace(documentTitle))
        {
            chunk.Props[ChunkPropsKeys.ContextDocumentTitle] = documentTitle;
        }

        // Store document type if provided
        if (!string.IsNullOrWhiteSpace(documentType))
        {
            chunk.Props[ChunkPropsKeys.ContextDocumentType] = documentType;
        }
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

    /// <summary>
    /// Update chunk relationships (previous/next) in Props dictionary
    /// Enables RAG systems to navigate between adjacent chunks
    /// </summary>
    public static void UpdateChunkRelationships(IEnumerable<DocumentChunk> chunks)
    {
        var chunkList = chunks.ToList();
        for (int i = 0; i < chunkList.Count; i++)
        {
            var chunk = chunkList[i];

            // Set previous chunk ID
            if (i > 0)
            {
                chunk.Props[ChunkPropsKeys.PreviousChunkId] = chunkList[i - 1].Id.ToString();
            }

            // Set next chunk ID
            if (i < chunkList.Count - 1)
            {
                chunk.Props[ChunkPropsKeys.NextChunkId] = chunkList[i + 1].Id.ToString();
            }
        }
    }

    /// <summary>
    /// Finalize chunks by updating count and relationships
    /// This is the recommended method to call after chunk generation
    /// </summary>
    public static void FinalizeChunks(IEnumerable<DocumentChunk> chunks)
    {
        var chunkList = chunks.ToList();
        UpdateChunkCount(chunkList);
        UpdateChunkRelationships(chunkList);
    }

    #region Multi-Dimensional Quality Scoring (Phase C)

    /// <summary>
    /// Calculate and store all quality metrics for a chunk
    /// Rule-based scoring without LLM dependency
    /// </summary>
    public static void CalculateQualityMetrics(DocumentChunk chunk)
    {
        var content = chunk.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            chunk.Props[ChunkPropsKeys.QualitySemanticCompleteness] = 0.0;
            chunk.Props[ChunkPropsKeys.QualityContextIndependence] = 0.0;
            chunk.Props[ChunkPropsKeys.QualityInformationDensity] = 0.0;
            chunk.Props[ChunkPropsKeys.QualityBoundarySharpness] = 0.0;
            return;
        }

        chunk.Props[ChunkPropsKeys.QualitySemanticCompleteness] = CalculateSemanticCompleteness(content);
        chunk.Props[ChunkPropsKeys.QualityContextIndependence] = CalculateContextIndependence(content);
        chunk.Props[ChunkPropsKeys.QualityInformationDensity] = CalculateInformationDensity(content);
        chunk.Props[ChunkPropsKeys.QualityBoundarySharpness] = CalculateBoundarySharpness(content);
    }

    /// <summary>
    /// Calculate semantic completeness score (0.0 - 1.0)
    /// Measures sentence integrity and complete thoughts
    /// </summary>
    public static double CalculateSemanticCompleteness(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0.0;

        var score = 1.0;
        var trimmed = content.Trim();

        // Check for sentence-ending punctuation
        var sentenceEndings = new[] { '.', '!', '?', '。', '！', '？' };
        var endsWithPunctuation = sentenceEndings.Any(p => trimmed.EndsWith(p));
        if (!endsWithPunctuation)
            score -= 0.2;

        // Check if starts with capital letter or proper beginning
        var startsProper = char.IsUpper(trimmed[0]) || char.IsDigit(trimmed[0]) || trimmed.StartsWith('#') || trimmed.StartsWith('-') || trimmed.StartsWith('•');
        if (!startsProper)
            score -= 0.15;

        // Check for incomplete sentences (starts with lowercase conjunction)
        var lowercaseStarters = new[] { "and ", "but ", "or ", "so ", "yet ", "because ", "however ", "therefore " };
        if (lowercaseStarters.Any(s => trimmed.StartsWith(s, StringComparison.OrdinalIgnoreCase) && char.IsLower(trimmed[0])))
            score -= 0.2;

        // Check for truncated content (ends with ellipsis or dash)
        if (trimmed.EndsWith("...") || trimmed.EndsWith("--") || trimmed.EndsWith("—"))
            score -= 0.25;

        // Check for balanced brackets/parentheses
        var openParens = trimmed.Count(c => c == '(');
        var closeParens = trimmed.Count(c => c == ')');
        var openBrackets = trimmed.Count(c => c == '[');
        var closeBrackets = trimmed.Count(c => c == ']');
        if (openParens != closeParens || openBrackets != closeBrackets)
            score -= 0.15;

        // Minimum viable content length bonus
        if (trimmed.Length >= 50)
            score = Math.Min(1.0, score + 0.05);

        return Math.Max(0.0, Math.Min(1.0, score));
    }

    /// <summary>
    /// Calculate context independence score (0.0 - 1.0)
    /// Measures how well chunk can be understood standalone
    /// </summary>
    public static double CalculateContextIndependence(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0.0;

        var score = 1.0;
        var words = content.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var wordCount = words.Length;

        if (wordCount == 0)
            return 0.0;

        // Check for dangling pronouns at the start
        var danglingPronouns = new[] { "it", "this", "that", "these", "those", "they", "them", "he", "she", "its" };
        var firstWord = words[0].ToLowerInvariant().TrimEnd(',', '.', ':', ';');
        if (danglingPronouns.Contains(firstWord))
            score -= 0.25;

        // Calculate pronoun density
        var pronouns = new[] { "it", "its", "this", "that", "these", "those", "they", "them", "their", "he", "him", "his", "she", "her", "hers", "we", "us", "our" };
        var pronounCount = words.Count(w => pronouns.Contains(w.ToLowerInvariant().TrimEnd(',', '.', ':', ';', '?', '!')));
        var pronounDensity = (double)pronounCount / wordCount;
        if (pronounDensity > 0.15)
            score -= 0.2;
        else if (pronounDensity > 0.10)
            score -= 0.1;

        // Check for referential phrases
        var referentialPhrases = new[] { "as mentioned", "as stated", "as shown", "see above", "see below", "previously", "following", "the above", "the below" };
        var lowerContent = content.ToLowerInvariant();
        foreach (var phrase in referentialPhrases)
        {
            if (lowerContent.Contains(phrase))
            {
                score -= 0.15;
                break;
            }
        }

        // Bonus for self-contained structures
        if (content.Contains(":") && (content.Contains("\n-") || content.Contains("\n•") || content.Contains("\n*")))
            score = Math.Min(1.0, score + 0.1);

        // Bonus for having a clear topic sentence
        var sentences = content.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        if (sentences.Length > 0 && sentences[0].Trim().Length >= 20)
            score = Math.Min(1.0, score + 0.05);

        return Math.Max(0.0, Math.Min(1.0, score));
    }

    /// <summary>
    /// Calculate information density score (0.0 - 1.0)
    /// Measures meaningful content ratio vs filler/whitespace
    /// </summary>
    public static double CalculateInformationDensity(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0.0;

        var score = 0.5; // Start at midpoint

        // Calculate character density (non-whitespace ratio)
        var nonWhitespace = content.Count(c => !char.IsWhiteSpace(c));
        var charDensity = (double)nonWhitespace / content.Length;
        score += (charDensity - 0.7) * 0.5; // Adjust based on typical density

        // Calculate word length (longer words often carry more meaning)
        var words = content.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0)
        {
            var avgWordLength = words.Average(w => w.Length);
            if (avgWordLength >= 5)
                score += 0.1;
            else if (avgWordLength < 3)
                score -= 0.1;
        }

        // Penalize excessive repetition
        var uniqueWords = words.Select(w => w.ToLowerInvariant()).Distinct().Count();
        if (words.Length > 10)
        {
            var uniquenessRatio = (double)uniqueWords / words.Length;
            if (uniquenessRatio < 0.5)
                score -= 0.2;
            else if (uniquenessRatio > 0.8)
                score += 0.1;
        }

        // Check for substantive content indicators
        var hasNumbers = content.Any(char.IsDigit);
        var hasProperNouns = words.Any(w => w.Length > 1 && char.IsUpper(w[0]) && w.Skip(1).All(c => char.IsLower(c) || !char.IsLetter(c)));
        var hasTechnicalTerms = content.Contains("()") || content.Contains("[]") || content.Contains("{}") || content.Contains("=>") || content.Contains("::");

        if (hasNumbers) score += 0.05;
        if (hasProperNouns) score += 0.05;
        if (hasTechnicalTerms) score += 0.1;

        // Penalize very short content
        if (content.Length < 50)
            score -= 0.2;
        else if (content.Length < 100)
            score -= 0.1;

        return Math.Max(0.0, Math.Min(1.0, score));
    }

    /// <summary>
    /// Calculate boundary sharpness score (0.0 - 1.0)
    /// Measures how clean the semantic boundaries are
    /// </summary>
    public static double CalculateBoundarySharpness(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0.0;

        var score = 0.8; // Start optimistic
        var trimmed = content.Trim();

        // Check start boundary quality
        var goodStarts = new[] { '#', '-', '•', '*', '1', '2', '3', '4', '5', '6', '7', '8', '9' };
        var startsWithStructure = goodStarts.Any(c => trimmed.StartsWith(c.ToString())) ||
                                   (trimmed.Length > 0 && char.IsUpper(trimmed[0]));
        if (!startsWithStructure)
            score -= 0.15;

        // Check for mid-sentence start
        var midSentenceIndicators = new[] { "and ", "but ", "or ", "which ", "that ", "who ", "where ", "when " };
        var lowerTrimmed = trimmed.ToLowerInvariant();
        if (midSentenceIndicators.Any(i => lowerTrimmed.StartsWith(i)))
            score -= 0.2;

        // Check end boundary quality
        var sentenceEndings = new[] { '.', '!', '?', ':', '。', '！', '？' };
        var endsCleanly = sentenceEndings.Any(p => trimmed.EndsWith(p));
        if (!endsCleanly)
            score -= 0.15;

        // Check for mid-sentence end
        var midSentenceEndings = new[] { ",", ";", " and", " or", " but", " the", " a", " an" };
        if (midSentenceEndings.Any(e => trimmed.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            score -= 0.2;

        // Bonus for structural boundaries (headers, lists)
        if (trimmed.StartsWith("#") || trimmed.StartsWith("##") || trimmed.StartsWith("###"))
            score = Math.Min(1.0, score + 0.15);

        // Bonus for paragraph-like structure (multiple sentences)
        var sentences = trimmed.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        if (sentences.Length >= 2)
            score = Math.Min(1.0, score + 0.1);

        // Check for code block boundaries
        if ((trimmed.StartsWith("```") && trimmed.EndsWith("```")) ||
            (trimmed.StartsWith("```") && trimmed.Contains("\n```")))
            score = Math.Min(1.0, score + 0.2);

        return Math.Max(0.0, Math.Min(1.0, score));
    }

    /// <summary>
    /// Calculate overall quality score from multi-dimensional metrics
    /// Weighted average of all quality dimensions
    /// </summary>
    public static double CalculateOverallQuality(DocumentChunk chunk)
    {
        var completeness = chunk.Props.TryGetValue(ChunkPropsKeys.QualitySemanticCompleteness, out var c) ? Convert.ToDouble(c) : 0.5;
        var independence = chunk.Props.TryGetValue(ChunkPropsKeys.QualityContextIndependence, out var i) ? Convert.ToDouble(i) : 0.5;
        var density = chunk.Props.TryGetValue(ChunkPropsKeys.QualityInformationDensity, out var d) ? Convert.ToDouble(d) : 0.5;
        var sharpness = chunk.Props.TryGetValue(ChunkPropsKeys.QualityBoundarySharpness, out var s) ? Convert.ToDouble(s) : 0.5;

        // Weighted average: completeness and independence are most important for RAG
        return (completeness * 0.30) + (independence * 0.30) + (density * 0.20) + (sharpness * 0.20);
    }

    /// <summary>
    /// Enhanced finalization with quality metrics calculation
    /// Use this for comprehensive chunk post-processing
    /// </summary>
    public static void FinalizeChunksWithQuality(IEnumerable<DocumentChunk> chunks)
    {
        var chunkList = chunks.ToList();
        UpdateChunkCount(chunkList);
        UpdateChunkRelationships(chunkList);

        foreach (var chunk in chunkList)
        {
            CalculateQualityMetrics(chunk);
            chunk.Quality = CalculateOverallQuality(chunk);
        }
    }

    #endregion
}
