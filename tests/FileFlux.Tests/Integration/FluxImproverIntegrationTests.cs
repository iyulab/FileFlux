using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure.Integration;
using Xunit;
using Xunit.Abstractions;

namespace FileFlux.Tests.Integration;

/// <summary>
/// Tests for FluxImprover integration helpers and extension methods.
/// </summary>
public class FluxImproverIntegrationTests
{
    private readonly ITestOutputHelper _output;
    private readonly FluxImproverIntegrationHelper _helper;

    public FluxImproverIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _helper = new FluxImproverIntegrationHelper();
    }

    #region ToFluxImproverChunk Tests

    [Fact]
    public void ToFluxImproverChunk_ConvertsBasicChunk()
    {
        // Arrange
        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid(),
            Content = "Test content for conversion.",
            Index = 5,
            Strategy = ChunkingStrategies.Auto,
            Quality = 0.85,
            Tokens = 10
        };
        chunk.Location.StartPage = 1;
        chunk.Location.EndPage = 2;
        chunk.Location.HeadingPath.Add("Chapter 1");
        chunk.Props["CustomKey"] = "CustomValue";

        // Act
        var result = _helper.ToFluxImproverChunk(chunk);

        // Assert
        Assert.Equal(chunk.Id.ToString(), result.Id);
        Assert.Equal(chunk.Content, result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Equal(1, result.Metadata!["StartPage"]);
        Assert.Equal(2, result.Metadata["EndPage"]);
        Assert.Equal(5, result.Metadata["Index"]);
        Assert.Equal(ChunkingStrategies.Auto, result.Metadata["Strategy"]);
        Assert.Equal(0.85, result.Metadata["Quality"]);
        Assert.Equal(10, result.Metadata["TokenCount"]);
        Assert.Equal("CustomValue", result.Metadata["CustomKey"]);

        _output.WriteLine($"Converted chunk ID: {result.Id}");
        _output.WriteLine($"Metadata keys: {string.Join(", ", result.Metadata.Keys)}");
    }

    [Fact]
    public void ToFluxImproverChunk_ThrowsOnNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _helper.ToFluxImproverChunk(null!));
    }

    [Fact]
    public void ToFluxImproverChunk_ExtensionMethod_Works()
    {
        // Arrange
        var chunk = new DocumentChunk
        {
            Content = "Extension method test.",
            Index = 0
        };

        // Act
        var result = chunk.ToFluxImproverChunk();

        // Assert
        Assert.Equal(chunk.Content, result.Content);
    }

    #endregion

    #region ToFluxImproverChunkWithContext Tests

    [Fact]
    public void ToFluxImproverChunkWithContext_ConvertsWithHierarchy()
    {
        // Arrange
        var chunk = new HierarchicalDocumentChunk
        {
            Id = Guid.NewGuid(),
            Content = "Child chunk content.",
            Index = 1,
            Level = 2,
            ParentId = "parent-123",
            Type = HierarchyChunkType.Leaf
        };
        chunk.Location.HeadingPath.Add("Chapter 1");
        chunk.Location.HeadingPath.Add("Section 1.1");

        // Act
        var result = _helper.ToFluxImproverChunkWithContext(chunk);

        // Assert
        Assert.Equal(chunk.Id.ToString(), result.Id);
        Assert.Equal(chunk.Content, result.Content);
        Assert.NotNull(result.ParentContext);
        Assert.Equal("parent-123", result.ParentContext!.ParentId);
        Assert.Equal("Chapter 1 > Section 1.1", result.ParentContext.ParentHeadingPath);
        Assert.Equal(2, result.ParentContext.HierarchyLevel);

        _output.WriteLine($"Parent context: {result.ParentContext.ParentHeadingPath}");
    }

    [Fact]
    public void ToFluxImproverChunkWithContext_RootChunk_NoParentContext()
    {
        // Arrange - IsRoot is computed from Level == 0 && ParentId == null
        var chunk = new HierarchicalDocumentChunk
        {
            Content = "Root chunk content.",
            Index = 0,
            Level = 0,
            ParentId = null,
            Type = HierarchyChunkType.Root
        };

        // Act
        var result = _helper.ToFluxImproverChunkWithContext(chunk);

        // Assert
        Assert.True(chunk.IsRoot); // Verify IsRoot computed correctly
        Assert.Null(result.ParentContext);
    }

    [Fact]
    public void ToFluxImproverChunkWithContext_WithParentEnrichmentData_UsesProvided()
    {
        // Arrange
        var chunk = new HierarchicalDocumentChunk
        {
            Content = "Child content.",
            Index = 1,
            Level = 1,
            ParentId = "parent-456"
        };

        var parentData = new ParentEnrichmentData
        {
            ParentId = "parent-456",
            ParentSummary = "This is the parent summary.",
            ParentKeywords = new List<string> { "keyword1", "keyword2" },
            ParentHeadingPath = "Custom Path",
            HierarchyLevel = 1
        };

        // Act
        var result = _helper.ToFluxImproverChunkWithContext(chunk, parentData);

        // Assert
        Assert.Equal(parentData, result.ParentContext);
        Assert.Equal("This is the parent summary.", result.ParentContext!.ParentSummary);
    }

    #endregion

    #region PrepareForContextualEnrichment Tests

    [Fact]
    public void PrepareForContextualEnrichment_PreparesCorrectly()
    {
        // Arrange
        var chunk = new DocumentChunk
        {
            Content = "A specific chunk from the document.",
            Index = 3,
            SourceInfo = new SourceMetadataInfo
            {
                Title = "Test Document",
                SourceType = "PDF",
                Language = "en",
                ChunkCount = 10
            }
        };
        var fullDocumentText = "This is the full document text. A specific chunk from the document. More text follows.";

        // Act
        var result = _helper.PrepareForContextualEnrichment(chunk, fullDocumentText);

        // Assert
        Assert.NotNull(result.Chunk);
        Assert.Equal(chunk.Content, result.Chunk.Content);
        Assert.Equal(fullDocumentText, result.FullDocumentText);
        Assert.NotNull(result.DocumentMetadata);
        Assert.Equal("Test Document", result.DocumentMetadata!.Title);
        Assert.Equal("PDF", result.DocumentMetadata.DocumentType);
        Assert.Equal("en", result.DocumentMetadata.Language);
        Assert.Equal(10, result.DocumentMetadata.TotalChunks);

        _output.WriteLine($"Document title: {result.DocumentMetadata.Title}");
    }

    [Fact]
    public void PrepareForContextualEnrichment_ExtensionMethod_Works()
    {
        // Arrange
        var chunk = new DocumentChunk { Content = "Chunk text.", Index = 0 };
        var fullText = "Full document text.";

        // Act
        var result = chunk.PrepareForContextualEnrichment(fullText);

        // Assert
        Assert.Equal(fullText, result.FullDocumentText);
    }

    #endregion

    #region OrderForHierarchicalEnrichment Tests

    [Fact]
    public void OrderForHierarchicalEnrichment_OrdersByLevelThenIndex()
    {
        // Arrange
        var chunks = new List<HierarchicalDocumentChunk>
        {
            new() { Level = 2, Index = 1 },
            new() { Level = 1, Index = 2 },
            new() { Level = 0, Index = 0 },
            new() { Level = 1, Index = 1 },
            new() { Level = 2, Index = 0 }
        };

        // Act
        var ordered = FluxImproverIntegrationHelper.OrderForHierarchicalEnrichment(chunks).ToList();

        // Assert
        Assert.Equal(0, ordered[0].Level);
        Assert.Equal(1, ordered[1].Level);
        Assert.Equal(1, ordered[1].Index);
        Assert.Equal(1, ordered[2].Level);
        Assert.Equal(2, ordered[2].Index);
        Assert.Equal(2, ordered[3].Level);
        Assert.Equal(0, ordered[3].Index);
        Assert.Equal(2, ordered[4].Level);
        Assert.Equal(1, ordered[4].Index);

        _output.WriteLine("Ordered levels: " + string.Join(", ", ordered.Select(c => $"L{c.Level}I{c.Index}")));
    }

    [Fact]
    public void OrderForHierarchicalEnrichment_ExtensionMethod_Works()
    {
        // Arrange
        var chunks = new List<HierarchicalDocumentChunk>
        {
            new() { Level = 1, Index = 0 },
            new() { Level = 0, Index = 0 }
        };

        // Act
        var ordered = chunks.OrderForHierarchicalEnrichment().ToList();

        // Assert
        Assert.Equal(0, ordered[0].Level);
        Assert.Equal(1, ordered[1].Level);
    }

    #endregion

    #region BuildHierarchyMap Tests

    [Fact]
    public void BuildHierarchyMap_GroupsByParentId()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var chunks = new List<HierarchicalDocumentChunk>
        {
            new() { ParentId = null, Content = "Root 1" },
            new() { ParentId = null, Content = "Root 2" },
            new() { ParentId = parentId, Content = "Child 1" },
            new() { ParentId = parentId, Content = "Child 2" },
            new() { ParentId = "other-parent", Content = "Other Child" }
        };

        // Act
        var map = FluxImproverIntegrationHelper.BuildHierarchyMap(chunks);

        // Assert
        Assert.Equal(3, map.Count);
        Assert.True(map.ContainsKey(string.Empty)); // Root chunks (null parent)
        Assert.Equal(2, map[string.Empty].Count);
        Assert.True(map.ContainsKey(parentId));
        Assert.Equal(2, map[parentId].Count);
        Assert.True(map.ContainsKey("other-parent"));
        Assert.Single(map["other-parent"]);

        _output.WriteLine($"Map has {map.Count} parent groups");
        foreach (var kvp in map)
        {
            _output.WriteLine($"  Parent '{kvp.Key}': {kvp.Value.Count} children");
        }
    }

    [Fact]
    public void BuildHierarchyMap_ExtensionMethod_Works()
    {
        // Arrange
        var chunks = new List<HierarchicalDocumentChunk>
        {
            new() { ParentId = null },
            new() { ParentId = "parent-1" }
        };

        // Act
        var map = chunks.BuildHierarchyMap();

        // Assert
        Assert.Equal(2, map.Count);
    }

    #endregion

    #region Batch Operations Tests

    [Fact]
    public void ToFluxImproverChunks_ConvertsBatch()
    {
        // Arrange
        var chunks = new List<DocumentChunk>
        {
            new() { Content = "Chunk 1", Index = 0 },
            new() { Content = "Chunk 2", Index = 1 },
            new() { Content = "Chunk 3", Index = 2 }
        };

        // Act
        var result = _helper.ToFluxImproverChunks(chunks);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("Chunk 1", result[0].Content);
        Assert.Equal("Chunk 2", result[1].Content);
        Assert.Equal("Chunk 3", result[2].Content);
    }

    [Fact]
    public void ToFluxImproverChunks_ExtensionMethod_Works()
    {
        // Arrange
        var chunks = new List<DocumentChunk>
        {
            new() { Content = "Test", Index = 0 }
        };

        // Act
        var result = chunks.ToFluxImproverChunks();

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public void PrepareForContextualEnrichmentBatch_PreparesBatch()
    {
        // Arrange
        var chunks = new List<DocumentChunk>
        {
            new() { Content = "Chunk 1", Index = 0 },
            new() { Content = "Chunk 2", Index = 1 }
        };
        var fullText = "Full document text.";

        // Act
        var result = _helper.PrepareForContextualEnrichmentBatch(chunks, fullText);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal(fullText, r.FullDocumentText));
    }

    #endregion

    #region BuildParentContext Extension Tests

    [Fact]
    public void BuildParentContext_CreatesCorrectContext()
    {
        // Arrange
        var parentChunk = new HierarchicalDocumentChunk
        {
            Id = Guid.NewGuid(),
            Content = "Parent content.",
            Level = 1
        };
        parentChunk.Location.HeadingPath.Add("Chapter 1");
        parentChunk.Location.HeadingPath.Add("Section 1.1");

        var summary = "This is a summary of the parent chunk.";
        var keywords = new List<string> { "keyword1", "keyword2", "keyword3" };

        // Act
        var context = parentChunk.BuildParentContext(summary, keywords);

        // Assert
        Assert.Equal(parentChunk.Id.ToString(), context.ParentId);
        Assert.Equal(summary, context.ParentSummary);
        Assert.Equal(keywords, context.ParentKeywords);
        Assert.Equal("Chapter 1 > Section 1.1", context.ParentHeadingPath);
        Assert.Equal(1, context.HierarchyLevel);

        _output.WriteLine($"Parent context: {context.ParentHeadingPath}");
        _output.WriteLine($"Summary: {context.ParentSummary}");
        _output.WriteLine($"Keywords: {string.Join(", ", context.ParentKeywords ?? [])}");
    }

    [Fact]
    public void BuildParentContext_WithoutOptionalParams_Works()
    {
        // Arrange
        var parentChunk = new HierarchicalDocumentChunk
        {
            Id = Guid.NewGuid(),
            Level = 0
        };

        // Act
        var context = parentChunk.BuildParentContext();

        // Assert
        Assert.Equal(parentChunk.Id.ToString(), context.ParentId);
        Assert.Null(context.ParentSummary);
        Assert.Null(context.ParentKeywords);
    }

    #endregion
}
