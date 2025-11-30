using FileFlux.Core;
using FileFlux.Domain;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// Hierarchical chunking strategy that creates multi-level parent-child chunk relationships.
/// Produces both section-level (parent) chunks for context and paragraph-level (child) chunks for retrieval.
/// </summary>
public partial class HierarchicalChunkingStrategy : IChunkingStrategy
{
    private static readonly Regex HeaderRegex = MyRegex();
    private static readonly Regex ParagraphRegex = new(@"\n\s*\n+", RegexOptions.Compiled);

    public string StrategyName => "Hierarchical";

    public IEnumerable<string> SupportedOptions => new[]
    {
        "MaxParentChunkSize",    // Maximum size for parent (section) chunks
        "MaxChildChunkSize",     // Maximum size for child (paragraph) chunks
        "MinSectionLength",      // Minimum length to create a section chunk
        "CreateSummaryChunks",   // Whether to create summary chunks for sections
        "MaxHierarchyDepth"      // Maximum hierarchy depth (default: 3)
    };

    public async Task<IEnumerable<DocumentChunk>> ChunkAsync(
        DocumentContent content,
        ChunkingOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (string.IsNullOrWhiteSpace(content.Text))
            return Enumerable.Empty<DocumentChunk>();

        // Get strategy options
        var maxParentSize = GetStrategyOption(options, "MaxParentChunkSize", options.MaxChunkSize * 2);
        var maxChildSize = GetStrategyOption(options, "MaxChildChunkSize", options.MaxChunkSize);
        var minSectionLength = GetStrategyOption(options, "MinSectionLength", 100);
        var createSummaryChunks = GetStrategyOption(options, "CreateSummaryChunks", false);
        var maxHierarchyDepth = GetStrategyOption(options, "MaxHierarchyDepth", 3);

        var allChunks = new List<HierarchicalDocumentChunk>();
        var text = content.Text;

        // Parse document structure into sections
        var sections = ParseDocumentSections(text, maxHierarchyDepth);

        // Create hierarchical chunks
        var chunkIndex = 0;
        var globalPosition = 0;
        var mergeGroupId = Guid.NewGuid().ToString();

        foreach (var section in sections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sectionChunks = CreateSectionChunks(
                section,
                content,
                ref chunkIndex,
                ref globalPosition,
                maxParentSize,
                maxChildSize,
                minSectionLength,
                mergeGroupId,
                options);

            allChunks.AddRange(sectionChunks);
        }

        // Build parent-child relationships
        BuildHierarchyRelationships(allChunks);

        // Finalize all chunks
        ChunkingHelper.FinalizeChunks(allChunks.Cast<DocumentChunk>());

        // Add hierarchy-specific Props
        foreach (var chunk in allChunks)
        {
            chunk.Props[ChunkPropsKeys.HierarchyLevel] = chunk.Level;
            chunk.Props[ChunkPropsKeys.HierarchyChunkType] = chunk.Type.ToString();
            if (!string.IsNullOrEmpty(chunk.GroupId))
            {
                chunk.Props[ChunkPropsKeys.MergeGroupId] = chunk.GroupId;
            }
            if (!string.IsNullOrEmpty(chunk.ParentId))
            {
                chunk.Props[ChunkPropsKeys.ParentChunkId] = chunk.ParentId;
            }
            if (chunk.ChildIds.Count > 0)
            {
                chunk.Props[ChunkPropsKeys.ChildChunkIds] = chunk.ChildIds;
            }
        }

        return await Task.FromResult(allChunks.Cast<DocumentChunk>());
    }

    public int EstimateChunkCount(DocumentContent content, ChunkingOptions options)
    {
        if (content == null || string.IsNullOrWhiteSpace(content.Text))
            return 0;

        // Estimate based on sections and paragraphs
        var sections = HeaderRegex.Matches(content.Text).Count;
        var paragraphs = ParagraphRegex.Matches(content.Text).Count;

        // Approximately: sections (as parents) + paragraphs (as children)
        return Math.Max(1, sections + paragraphs);
    }

    /// <summary>
    /// Parse document into hierarchical sections based on headers
    /// </summary>
    private List<DocumentSection> ParseDocumentSections(string text, int maxDepth)
    {
        var sections = new List<DocumentSection>();
        var lines = text.Split('\n');
        var currentSections = new Stack<DocumentSection>();

        var currentContent = new List<string>();
        var currentPosition = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var headerMatch = HeaderRegex.Match(line);

            if (headerMatch.Success)
            {
                // Determine header level
                var headerLevel = GetHeaderLevel(line);

                // Save content to current section
                if (currentContent.Count > 0 && currentSections.Count > 0)
                {
                    currentSections.Peek().Content = string.Join("\n", currentContent);
                    currentContent.Clear();
                }

                // Create new section
                var newSection = new DocumentSection
                {
                    Title = line.TrimStart('#', ' ', '\t'),
                    Level = Math.Min(headerLevel, maxDepth),
                    StartPosition = currentPosition
                };

                // Find parent section
                while (currentSections.Count > 0 && currentSections.Peek().Level >= newSection.Level)
                {
                    var completed = currentSections.Pop();
                    completed.EndPosition = currentPosition;
                }

                if (currentSections.Count > 0 && newSection.Level <= maxDepth)
                {
                    currentSections.Peek().Children.Add(newSection);
                    newSection.Parent = currentSections.Peek();
                }
                else if (newSection.Level <= maxDepth)
                {
                    sections.Add(newSection);
                }

                if (newSection.Level < maxDepth)
                {
                    currentSections.Push(newSection);
                }
            }
            else
            {
                currentContent.Add(line);
            }

            currentPosition += line.Length + 1;
        }

        // Finalize remaining content
        if (currentContent.Count > 0 && currentSections.Count > 0)
        {
            currentSections.Peek().Content = string.Join("\n", currentContent);
        }

        // Close remaining sections
        while (currentSections.Count > 0)
        {
            var section = currentSections.Pop();
            section.EndPosition = currentPosition;
        }

        // If no sections found, create a single root section
        if (sections.Count == 0)
        {
            sections.Add(new DocumentSection
            {
                Title = "Document",
                Level = 0,
                Content = text,
                StartPosition = 0,
                EndPosition = text.Length
            });
        }

        return sections;
    }

    /// <summary>
    /// Create chunks for a section and its children
    /// </summary>
    private List<HierarchicalDocumentChunk> CreateSectionChunks(
        DocumentSection section,
        DocumentContent content,
        ref int chunkIndex,
        ref int globalPosition,
        int maxParentSize,
        int maxChildSize,
        int minSectionLength,
        string mergeGroupId,
        ChunkingOptions options)
    {
        var chunks = new List<HierarchicalDocumentChunk>();

        // Create parent chunk for this section if it has meaningful content
        HierarchicalDocumentChunk? parentChunk = null;
        var sectionText = !string.IsNullOrWhiteSpace(section.Content) ? section.Content : section.Title;

        if (sectionText.Length >= minSectionLength || section.Children.Count > 0)
        {
            parentChunk = CreateHierarchicalChunk(
                section.Title + (section.Content?.Length > 0 ? "\n\n" + TruncateToSize(section.Content, maxParentSize) : ""),
                content,
                chunkIndex++,
                globalPosition,
                section.Level,
                section.Children.Count > 0 ? HierarchyChunkType.Parent : HierarchyChunkType.Leaf,
                mergeGroupId,
                options);

            chunks.Add(parentChunk);
            globalPosition += parentChunk.Content.Length;
        }

        // Create child chunks for section content
        if (!string.IsNullOrWhiteSpace(section.Content) && section.Content.Length > maxChildSize)
        {
            var paragraphs = SplitIntoParagraphs(section.Content, maxChildSize);

            foreach (var paragraph in paragraphs)
            {
                if (string.IsNullOrWhiteSpace(paragraph) || paragraph.Length < 20)
                    continue;

                var childChunk = CreateHierarchicalChunk(
                    paragraph,
                    content,
                    chunkIndex++,
                    globalPosition,
                    section.Level + 1,
                    HierarchyChunkType.Leaf,
                    mergeGroupId,
                    options);

                childChunk.ParentId = parentChunk?.Id.ToString();
                parentChunk?.AddChild(childChunk.Id.ToString());

                chunks.Add(childChunk);
                globalPosition += childChunk.Content.Length;
            }
        }

        // Process child sections recursively
        foreach (var childSection in section.Children)
        {
            var childChunks = CreateSectionChunks(
                childSection,
                content,
                ref chunkIndex,
                ref globalPosition,
                maxParentSize,
                maxChildSize,
                minSectionLength,
                mergeGroupId,
                options);

            // Link to parent
            if (parentChunk != null && childChunks.Count > 0)
            {
                var firstChildChunk = childChunks[0];
                firstChildChunk.ParentId = parentChunk.Id.ToString();
                parentChunk.AddChild(firstChildChunk.Id.ToString());
            }

            chunks.AddRange(childChunks);
        }

        return chunks;
    }

    /// <summary>
    /// Build parent-child relationship links
    /// </summary>
    private void BuildHierarchyRelationships(List<HierarchicalDocumentChunk> chunks)
    {
        // Update Type based on relationships
        foreach (var chunk in chunks)
        {
            if (chunk.IsRoot && chunk.HasChildren)
            {
                chunk.Type = HierarchyChunkType.Parent;
            }
            else if (chunk.IsLeaf && !chunk.IsRoot)
            {
                chunk.Type = HierarchyChunkType.Leaf;
            }
        }
    }

    /// <summary>
    /// Create a hierarchical chunk
    /// </summary>
    private HierarchicalDocumentChunk CreateHierarchicalChunk(
        string content,
        DocumentContent documentContent,
        int index,
        int position,
        int level,
        HierarchyChunkType type,
        string mergeGroupId,
        ChunkingOptions options)
    {
        var chunk = new HierarchicalDocumentChunk
        {
            Content = content.Trim(),
            Index = index,
            Location = new SourceLocation
            {
                StartChar = position,
                EndChar = position + content.Length
            },
            Metadata = documentContent.Metadata,
            Strategy = StrategyName,
            Tokens = EstimateTokenCount(content),
            Level = level,
            Type = type,
            GroupId = mergeGroupId,
            Quality = CalculateQuality(content),
            Importance = CalculateImportance(content, level),
            Density = CalculateDensity(content)
        };

        // Enrich with structural metadata
        ChunkingHelper.EnrichChunk(chunk, documentContent, position, position + content.Length);

        return chunk;
    }

    /// <summary>
    /// Get header level from markdown header line
    /// </summary>
    private int GetHeaderLevel(string line)
    {
        var trimmed = line.TrimStart();
        var level = 0;
        foreach (var c in trimmed)
        {
            if (c == '#') level++;
            else break;
        }
        return Math.Max(1, level);
    }

    /// <summary>
    /// Split text into paragraphs respecting max size
    /// </summary>
    private List<string> SplitIntoParagraphs(string text, int maxSize)
    {
        var paragraphs = ParagraphRegex.Split(text)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        var result = new List<string>();

        foreach (var para in paragraphs)
        {
            if (para.Length <= maxSize)
            {
                result.Add(para);
            }
            else
            {
                // Split long paragraphs
                var sentences = para.Split(new[] { ". ", "! ", "? " }, StringSplitOptions.RemoveEmptyEntries);
                var current = "";

                foreach (var sentence in sentences)
                {
                    if ((current + sentence).Length > maxSize && current.Length > 0)
                    {
                        result.Add(current.Trim());
                        current = sentence;
                    }
                    else
                    {
                        current += (current.Length > 0 ? ". " : "") + sentence;
                    }
                }

                if (current.Length > 0)
                {
                    result.Add(current.Trim());
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Truncate text to maximum size
    /// </summary>
    private string TruncateToSize(string text, int maxSize)
    {
        if (text.Length <= maxSize) return text;
        return text.Substring(0, maxSize - 3) + "...";
    }

    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return (int)(text.Length / 4.0);
    }

    private static double CalculateQuality(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0.0;
        var length = content.Length;
        var wordCount = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Min(1.0, (wordCount / 50.0) * 0.5 + (length > 100 ? 0.5 : length / 200.0));
    }

    private static double CalculateImportance(string content, int level)
    {
        // Higher importance for higher-level sections
        var levelBonus = Math.Max(0, (3 - level) * 0.1);
        var hasHeader = content.StartsWith("#") ? 0.1 : 0;
        return Math.Min(1.0, 0.5 + levelBonus + hasHeader);
    }

    private static double CalculateDensity(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0.0;
        var nonSpaceChars = content.Count(c => !char.IsWhiteSpace(c));
        return Math.Min(1.0, nonSpaceChars / (double)content.Length);
    }

    private static T GetStrategyOption<T>(ChunkingOptions options, string key, T defaultValue)
    {
        if (options.StrategyOptions.TryGetValue(key, out var value) && value is T typedValue)
            return typedValue;
        return defaultValue;
    }

    [GeneratedRegex(@"^#{1,6}\s+.+$", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex MyRegex();

    /// <summary>
    /// Internal class for document section structure
    /// </summary>
    private class DocumentSection
    {
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public int Level { get; set; }
        public int StartPosition { get; set; }
        public int EndPosition { get; set; }
        public DocumentSection? Parent { get; set; }
        public List<DocumentSection> Children { get; } = new();
    }
}
