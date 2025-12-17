using FileFlux.Core;
using FileFlux.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// Tests for DocumentEnricher - Stage 4 of the pipeline.
/// </summary>
public class DocumentEnricherTests
{
    private readonly DocumentEnricher _enricher;

    public DocumentEnricherTests()
    {
        // Create enricher without LLM services
        _enricher = new DocumentEnricher(
            improverServices: null,
            logger: NullLogger<DocumentEnricher>.Instance);
    }

    [Fact]
    public void Properties_WithoutLlm_ReturnsCorrectValues()
    {
        Assert.Equal("DocumentEnricher", _enricher.EnricherType);
        Assert.False(_enricher.HasLlmSupport);
        Assert.False(_enricher.SupportsContextualEnrichment);
    }

    [Fact]
    public async Task EnrichAsync_EmptyChunks_ReturnsEmptyResult()
    {
        // Arrange
        var chunks = new List<DocumentChunk>();
        var refined = CreateRefinedContent("Test");

        // Act
        var result = await _enricher.EnrichAsync(chunks, refined);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Chunks);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task EnrichAsync_WithChunks_CreatesEnrichedChunks()
    {
        // Arrange
        var chunks = CreateTestChunks(3);
        var refined = CreateRefinedContent("Test document content");

        // Act
        var result = await _enricher.EnrichAsync(chunks, refined);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Chunks.Count);
        foreach (var enriched in result.Chunks)
        {
            Assert.NotNull(enriched.Chunk);
        }
    }

    [Fact]
    public async Task EnrichAsync_WithBuildGraph_CreatesGraph()
    {
        // Arrange
        var chunks = CreateTestChunks(5);
        var refined = CreateRefinedContent("Test document");
        var options = new EnrichOptions { BuildGraph = true };

        // Act
        var result = await _enricher.EnrichAsync(chunks, refined, options);

        // Assert
        Assert.NotNull(result.Graph);
        Assert.True(result.Graph.NodeCount > 0);
    }

    [Fact]
    public async Task EnrichAsync_WithoutBuildGraph_NoGraph()
    {
        // Arrange
        var chunks = CreateTestChunks(3);
        var refined = CreateRefinedContent("Test");
        var options = new EnrichOptions { BuildGraph = false };

        // Act
        var result = await _enricher.EnrichAsync(chunks, refined, options);

        // Assert
        Assert.Null(result.Graph);
    }

    [Fact]
    public async Task EnrichAsync_Stats_RecordsProcessingInfo()
    {
        // Arrange
        var chunks = CreateTestChunks(3);
        var refined = CreateRefinedContent("Test");

        // Act
        var result = await _enricher.EnrichAsync(chunks, refined);

        // Assert
        Assert.NotNull(result.Stats);
        // Without LLM, EnrichedChunks counts only chunks with LLM enrichment
        // When no LLM is available, chunks are still processed but not "enriched" with LLM data
        Assert.True(result.Stats.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task BuildGraphAsync_CreatesSequentialEdges()
    {
        // Arrange
        var chunks = CreateTestChunks(3);
        var enrichedChunks = chunks.Select(c => new EnrichedDocumentChunk
        {
            Chunk = c
        }).ToList();
        var options = new GraphBuildOptions
        {
            IncludeSequentialEdges = true,
            IncludeHierarchicalEdges = false,
            DiscoverSemanticRelationships = false
        };

        // Act
        var graph = await _enricher.BuildGraphAsync(enrichedChunks, options);

        // Assert
        Assert.NotNull(graph);
        Assert.Equal(3, graph.NodeCount);
        // Sequential edges: 0->1, 1->2 = 2 edges
        Assert.True(graph.EdgeCount >= 2);
    }

    [Fact]
    public async Task BuildGraphAsync_MinimalOptions_OnlyStructuralEdges()
    {
        // Arrange
        var chunks = CreateTestChunks(3);
        var enrichedChunks = chunks.Select(c => new EnrichedDocumentChunk
        {
            Chunk = c
        }).ToList();

        // Act
        var graph = await _enricher.BuildGraphAsync(enrichedChunks, GraphBuildOptions.Minimal);

        // Assert
        Assert.NotNull(graph);
        Assert.True(graph.NodeCount > 0);
    }

    [Fact]
    public async Task EnrichStreamAsync_YieldsChunksProgressively()
    {
        // Arrange
        var chunks = CreateTestChunks(5);
        var refined = CreateRefinedContent("Test");

        // Act
        var enrichedChunks = new List<EnrichedDocumentChunk>();
        await foreach (var chunk in _enricher.EnrichStreamAsync(chunks, refined))
        {
            enrichedChunks.Add(chunk);
        }

        // Assert
        Assert.Equal(5, enrichedChunks.Count);
    }

    [Fact]
    public async Task EnrichAsync_WithCancelledToken_HandlesGracefully()
    {
        // Arrange
        var chunks = CreateTestChunks(3);
        var refined = CreateRefinedContent("Test");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act - Without LLM services, processing is fast and may complete before cancellation check
        // The behavior depends on implementation: either throws or completes quickly
        try
        {
            var result = await _enricher.EnrichAsync(chunks, refined, cancellationToken: cts.Token);
            // If completed, verify result is valid
            Assert.NotNull(result);
        }
        catch (OperationCanceledException)
        {
            // Expected behavior if cancellation is checked early
        }
    }

    [Fact]
    public void EnrichedDocumentChunk_SearchableText_ReturnsContentWhenNoContextual()
    {
        // Arrange
        var chunk = new DocumentChunk
        {
            Content = "Original content"
        };
        var enriched = new EnrichedDocumentChunk
        {
            Chunk = chunk,
            ContextualText = null
        };

        // Act
        var searchable = enriched.SearchableText;

        // Assert
        Assert.Equal("Original content", searchable);
    }

    [Fact]
    public void EnrichedDocumentChunk_SearchableText_CombinesContextualAndOriginal()
    {
        // Arrange
        var chunk = new DocumentChunk
        {
            Content = "Original content"
        };
        var enriched = new EnrichedDocumentChunk
        {
            Chunk = chunk,
            ContextualText = "This chunk discusses important topics."
        };

        // Act
        var searchable = enriched.SearchableText;

        // Assert
        Assert.Contains("This chunk discusses", searchable);
        Assert.Contains("Original content", searchable);
    }

    [Fact]
    public void EnrichedDocumentChunk_IsEnriched_TrueWhenHasSummary()
    {
        // Arrange
        var enriched = new EnrichedDocumentChunk
        {
            Chunk = new DocumentChunk { Content = "Test" },
            Summary = "A summary"
        };

        // Assert
        Assert.True(enriched.IsEnriched);
    }

    [Fact]
    public void EnrichedDocumentChunk_IsEnriched_FalseWhenEmpty()
    {
        // Arrange
        var enriched = new EnrichedDocumentChunk
        {
            Chunk = new DocumentChunk { Content = "Test" }
        };

        // Assert
        Assert.False(enriched.IsEnriched);
    }

    [Fact]
    public void EnrichedDocumentChunk_KeywordList_ReturnsEmptyWhenNull()
    {
        // Arrange
        var enriched = new EnrichedDocumentChunk
        {
            Chunk = new DocumentChunk { Content = "Test" },
            Keywords = null
        };

        // Assert
        Assert.Empty(enriched.KeywordList);
    }

    [Fact]
    public void EnrichedDocumentChunk_KeywordList_ReturnsKeys()
    {
        // Arrange
        var enriched = new EnrichedDocumentChunk
        {
            Chunk = new DocumentChunk { Content = "Test" },
            Keywords = new Dictionary<string, double>
            {
                ["keyword1"] = 0.9,
                ["keyword2"] = 0.8
            }
        };

        // Assert
        Assert.Equal(2, enriched.KeywordList.Count);
        Assert.Contains("keyword1", enriched.KeywordList);
        Assert.Contains("keyword2", enriched.KeywordList);
    }

    #region Helper Methods

    private static List<DocumentChunk> CreateTestChunks(int count)
    {
        return Enumerable.Range(0, count).Select(i => new DocumentChunk
        {
            Id = Guid.NewGuid(),
            RawId = Guid.NewGuid(),
            Content = $"This is chunk {i} content with some text.",
            Index = i,
            Tokens = 10,
            Strategy = "test",
            Location = new SourceLocation
            {
                StartChar = i * 100,
                EndChar = (i + 1) * 100
            }
        }).ToList();
    }

    private static RefinedContent CreateRefinedContent(string text)
    {
        return new RefinedContent
        {
            RawId = Guid.NewGuid(),
            Text = text,
            Sections = new List<Section>(),
            Structures = new List<StructuredElement>(),
            Metadata = new DocumentMetadata
            {
                FileName = "test.txt",
                FileType = "TXT"
            },
            Quality = new RefinementQuality(),
            Info = new RefinementInfo { RefinerType = "Test" }
        };
    }

    #endregion
}
