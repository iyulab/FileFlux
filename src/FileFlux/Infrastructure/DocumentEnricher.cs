using System.Diagnostics;
using System.Runtime.CompilerServices;
using FileFlux.Core;
using FileFlux.Infrastructure.Integration;
using FluxImprover;
using FluxImprover.Models;
using FluxImprover.Options;
using Microsoft.Extensions.Logging;

namespace FileFlux.Infrastructure;

/// <summary>
/// Document enricher that integrates with FluxImprover for LLM-powered enrichment.
/// </summary>
public sealed class DocumentEnricher : IDocumentEnricher
{
    private readonly FluxImproverServices? _improverServices;
    private readonly FluxImproverIntegrationHelper _integrationHelper;
    private readonly ILogger<DocumentEnricher>? _logger;

    /// <summary>
    /// Create enricher with optional FluxImprover services.
    /// </summary>
    public DocumentEnricher(
        FluxImproverServices? improverServices = null,
        ILogger<DocumentEnricher>? logger = null)
    {
        _improverServices = improverServices;
        _integrationHelper = new FluxImproverIntegrationHelper();
        _logger = logger;
    }

    /// <inheritdoc />
    public string EnricherType => "DocumentEnricher";

    /// <inheritdoc />
    public bool HasLlmSupport => _improverServices != null;

    /// <inheritdoc />
    public bool SupportsContextualEnrichment =>
        _improverServices?.ContextualEnrichment != null;

    /// <inheritdoc />
    public async Task<EnrichmentResult> EnrichAsync(
        IReadOnlyList<DocumentChunk> chunks,
        RefinedContent refined,
        EnrichOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= EnrichOptions.Default;
        var sw = Stopwatch.StartNew();
        var stats = new EnrichmentStatsBuilder();

        _logger?.LogInformation(
            "Starting enrichment of {ChunkCount} chunks with options: " +
            "Summaries={Summaries}, Keywords={Keywords}, ContextualText={Contextual}, Graph={Graph}",
            chunks.Count, options.GenerateSummaries, options.ExtractKeywords,
            options.AddContextualText, options.BuildGraph);

        // Enrich chunks
        var enrichedChunks = new List<EnrichedDocumentChunk>(chunks.Count);

        if (_improverServices != null)
        {
            enrichedChunks = await EnrichWithLlmAsync(
                chunks, refined, options, stats, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // No LLM - create basic enriched chunks without LLM features
            enrichedChunks = chunks.Select(c => new EnrichedDocumentChunk { Chunk = c }).ToList();
            _logger?.LogDebug("No LLM services available, skipping LLM enrichment");
        }

        stats.EnrichedChunks = enrichedChunks.Count(e => e.IsEnriched);

        // Build graph if requested
        DocumentGraph? graph = null;
        if (options.BuildGraph)
        {
            graph = await BuildGraphAsync(
                enrichedChunks,
                new GraphBuildOptions
                {
                    DiscoverSemanticRelationships = _improverServices != null
                },
                cancellationToken).ConfigureAwait(false);

            stats.GraphNodes = graph.NodeCount;
            stats.GraphEdges = graph.EdgeCount;
        }

        sw.Stop();

        return new EnrichmentResult
        {
            Chunks = enrichedChunks,
            Graph = graph,
            Stats = stats.Build(sw.Elapsed)
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<EnrichedDocumentChunk> EnrichStreamAsync(
        IReadOnlyList<DocumentChunk> chunks,
        RefinedContent refined,
        EnrichOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= EnrichOptions.Default;
        var fullText = refined.Text;

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var enriched = await EnrichSingleChunkAsync(
                chunk, fullText, options, cancellationToken).ConfigureAwait(false);

            yield return enriched;
        }
    }

    /// <inheritdoc />
    public async Task<DocumentGraph> BuildGraphAsync(
        IReadOnlyList<EnrichedDocumentChunk> chunks,
        GraphBuildOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= GraphBuildOptions.Default;

        var documentId = chunks.FirstOrDefault()?.Chunk.RawId ?? Guid.NewGuid();
        var nodes = new List<ChunkNode>(chunks.Count);
        var edges = new List<ChunkEdge>();

        _logger?.LogDebug("Building graph for {ChunkCount} chunks", chunks.Count);

        // Create nodes
        for (int i = 0; i < chunks.Count; i++)
        {
            var enriched = chunks[i];
            var chunk = enriched.Chunk;

            nodes.Add(new ChunkNode
            {
                ChunkId = chunk.Id,
                Index = chunk.Index,
                Summary = enriched.Summary,
                Keywords = enriched.KeywordList,
                StructureIds = GetStructureIds(chunk),
                SectionPath = chunk.Location.HeadingPath,
                Position = new ChunkPosition
                {
                    Sequence = i,
                    PreviousId = i > 0 ? chunks[i - 1].Chunk.Id : null,
                    NextId = i < chunks.Count - 1 ? chunks[i + 1].Chunk.Id : null,
                    Depth = chunk.Location.HeadingPath.Count
                }
            });
        }

        // Add sequential edges
        if (options.IncludeSequentialEdges)
        {
            for (int i = 0; i < chunks.Count - 1; i++)
            {
                edges.Add(new ChunkEdge
                {
                    SourceId = chunks[i].Chunk.Id,
                    TargetId = chunks[i + 1].Chunk.Id,
                    Type = EdgeType.Sequential,
                    Weight = 1.0,
                    Label = "follows"
                });
            }
        }

        // Add hierarchical edges based on section structure
        if (options.IncludeHierarchicalEdges)
        {
            edges.AddRange(BuildHierarchicalEdges(chunks));
        }

        // Add shared entity edges
        if (options.IncludeSharedEntityEdges && chunks.Any(e => e.Entities?.Count > 0))
        {
            edges.AddRange(BuildSharedEntityEdges(chunks, options));
        }

        // Discover semantic relationships via LLM
        if (options.DiscoverSemanticRelationships && _improverServices?.ChunkRelationship != null)
        {
            var semanticEdges = await DiscoverSemanticRelationshipsAsync(
                chunks, options, cancellationToken).ConfigureAwait(false);
            edges.AddRange(semanticEdges);
        }

        // Enforce max edges per chunk
        var edgesBySource = edges
            .GroupBy(e => e.SourceId)
            .SelectMany(g => g.Take(options.MaxEdgesPerChunk))
            .ToList();

        return new DocumentGraph
        {
            DocumentId = documentId,
            Nodes = nodes,
            Edges = edgesBySource,
            BuiltAt = DateTime.UtcNow
        };
    }

    private async Task<List<EnrichedDocumentChunk>> EnrichWithLlmAsync(
        IReadOnlyList<DocumentChunk> chunks,
        RefinedContent refined,
        EnrichOptions options,
        EnrichmentStatsBuilder stats,
        CancellationToken cancellationToken)
    {
        var fullText = refined.Text;
        var results = new EnrichedDocumentChunk[chunks.Count];
        var semaphore = new SemaphoreSlim(options.MaxConcurrency);

        var tasks = chunks.Select(async (chunk, index) =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                results[index] = await EnrichSingleChunkAsync(
                    chunk, fullText, options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to enrich chunk {Index}", index);
                results[index] = new EnrichedDocumentChunk { Chunk = chunk };
                Interlocked.Increment(ref stats.FailedChunks);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Count stats
        foreach (var r in results)
        {
            if (r.Summary != null) Interlocked.Increment(ref stats.SummariesGenerated);
            if (r.Keywords != null) Interlocked.Increment(ref stats.KeywordsExtracted);
            if (r.ContextualText != null) Interlocked.Increment(ref stats.ContextualTextsAdded);
        }

        return results.ToList();
    }

    private async Task<EnrichedDocumentChunk> EnrichSingleChunkAsync(
        DocumentChunk chunk,
        string fullDocumentText,
        EnrichOptions options,
        CancellationToken cancellationToken)
    {
        string? summary = null;
        IReadOnlyDictionary<string, double>? keywords = null;
        string? contextualText = null;

        if (_improverServices == null)
        {
            return new EnrichedDocumentChunk { Chunk = chunk };
        }

        // Generate summary
        if (options.GenerateSummaries)
        {
            try
            {
                summary = await _improverServices.Summarization
                    .SummarizeAsync(chunk.Content, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to generate summary for chunk {Id}", chunk.Id);
            }
        }

        // Extract keywords
        if (options.ExtractKeywords)
        {
            try
            {
                keywords = await _improverServices.KeywordExtraction
                    .ExtractKeywordsWithScoresAsync(chunk.Content, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to extract keywords for chunk {Id}", chunk.Id);
            }
        }

        // Add contextual text (Anthropic's Contextual Retrieval pattern)
        if (options.AddContextualText && _improverServices.ContextualEnrichment != null)
        {
            try
            {
                var fluxChunk = new Chunk
                {
                    Id = chunk.Id.ToString(),
                    Content = chunk.Content,
                    Metadata = chunk.Props.ToDictionary(p => p.Key, p => p.Value)
                };

                var enrichedResult = await _improverServices.ContextualEnrichment
                    .EnrichAsync(fluxChunk, fullDocumentText, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                contextualText = enrichedResult.ContextSummary;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to generate contextual text for chunk {Id}", chunk.Id);
            }
        }

        return new EnrichedDocumentChunk
        {
            Chunk = chunk,
            Summary = summary,
            Keywords = keywords,
            ContextualText = contextualText
        };
    }

    private IEnumerable<ChunkEdge> BuildHierarchicalEdges(IReadOnlyList<EnrichedDocumentChunk> chunks)
    {
        var edges = new List<ChunkEdge>();

        // Group chunks by section path depth
        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i].Chunk;
            var path = chunk.Location.HeadingPath;

            if (path.Count == 0)
                continue;

            // Find parent (chunk with shorter path that shares prefix)
            for (int j = i - 1; j >= 0; j--)
            {
                var other = chunks[j].Chunk;
                var otherPath = other.Location.HeadingPath;

                if (otherPath.Count < path.Count && IsPathPrefix(otherPath, path))
                {
                    edges.Add(new ChunkEdge
                    {
                        SourceId = other.Id,
                        TargetId = chunk.Id,
                        Type = EdgeType.Hierarchical,
                        Weight = 0.8,
                        Label = "contains"
                    });
                    break;
                }
            }
        }

        return edges;
    }

    private IEnumerable<ChunkEdge> BuildSharedEntityEdges(
        IReadOnlyList<EnrichedDocumentChunk> chunks,
        GraphBuildOptions options)
    {
        var edges = new List<ChunkEdge>();
        var entityIndex = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        // Build entity index
        for (int i = 0; i < chunks.Count; i++)
        {
            var entities = chunks[i].Entities;
            if (entities == null) continue;

            foreach (var entity in entities)
            {
                if (!entityIndex.TryGetValue(entity, out var list))
                {
                    list = new List<int>();
                    entityIndex[entity] = list;
                }
                list.Add(i);
            }
        }

        // Create edges between chunks sharing entities
        foreach (var (entity, indices) in entityIndex)
        {
            for (int i = 0; i < indices.Count; i++)
            {
                for (int j = i + 1; j < indices.Count; j++)
                {
                    var sourceIdx = indices[i];
                    var targetIdx = indices[j];

                    // Only add if within reasonable distance
                    if (Math.Abs(sourceIdx - targetIdx) > 10)
                        continue;

                    edges.Add(new ChunkEdge
                    {
                        SourceId = chunks[sourceIdx].Chunk.Id,
                        TargetId = chunks[targetIdx].Chunk.Id,
                        Type = EdgeType.SharedEntity,
                        Weight = 0.6,
                        Label = $"shares: {entity}",
                        Properties = new Dictionary<string, object>
                        {
                            ["entity"] = entity
                        }
                    });
                }
            }
        }

        return edges;
    }

    private async Task<IEnumerable<ChunkEdge>> DiscoverSemanticRelationshipsAsync(
        IReadOnlyList<EnrichedDocumentChunk> chunks,
        GraphBuildOptions options,
        CancellationToken cancellationToken)
    {
        var edges = new List<ChunkEdge>();

        if (_improverServices?.ChunkRelationship == null || chunks.Count < 2)
            return edges;

        try
        {
            // Convert to FluxImprover chunks
            var fluxChunks = chunks.Select(e => new Chunk
            {
                Id = e.Chunk.Id.ToString(),
                Content = e.Chunk.Content,
                Metadata = e.Chunk.Props.ToDictionary(p => p.Key, p => p.Value)
            }).ToList();

            var discoveredRelationships = await _improverServices.ChunkRelationship
                .DiscoverAllRelationshipsAsync(
                    fluxChunks,
                    new ChunkRelationshipOptions
                    {
                        MinConfidence = (float)options.MinRelationshipConfidence
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            // Convert to FileFlux edges
            foreach (var rel in discoveredRelationships)
            {
                if (rel.Confidence < options.MinRelationshipConfidence)
                    continue;

                var sourceId = Guid.Parse(rel.SourceChunkId);
                var targetId = Guid.Parse(rel.TargetChunkId);

                edges.Add(new ChunkEdge
                {
                    SourceId = sourceId,
                    TargetId = targetId,
                    Type = MapRelationshipType(rel.RelationshipType),
                    Weight = rel.Confidence,
                    Label = rel.Explanation,
                    Properties = new Dictionary<string, object>
                    {
                        ["fluxImproverType"] = rel.RelationshipType.ToString(),
                        ["bidirectional"] = rel.IsBidirectional
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to discover semantic relationships");
        }

        return edges;
    }

    private static EdgeType MapRelationshipType(ChunkRelationshipType type) => type switch
    {
        ChunkRelationshipType.SameTopic => EdgeType.Semantic,
        ChunkRelationshipType.References => EdgeType.Reference,
        ChunkRelationshipType.Complementary => EdgeType.Semantic,
        ChunkRelationshipType.Contradicts => EdgeType.Contrast,
        ChunkRelationshipType.Prerequisite => EdgeType.Sequential,
        ChunkRelationshipType.Elaborates => EdgeType.Continuation,
        ChunkRelationshipType.Summarizes => EdgeType.Semantic,
        ChunkRelationshipType.ExampleOf => EdgeType.Example,
        ChunkRelationshipType.CauseEffect => EdgeType.Semantic,
        ChunkRelationshipType.Temporal => EdgeType.Sequential,
        _ => EdgeType.Semantic
    };

    private static bool IsPathPrefix(IReadOnlyList<string> prefix, IReadOnlyList<string> path)
    {
        if (prefix.Count >= path.Count)
            return false;

        for (int i = 0; i < prefix.Count; i++)
        {
            if (!string.Equals(prefix[i], path[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static IReadOnlyList<string> GetStructureIds(DocumentChunk chunk)
    {
        if (chunk.Props.TryGetValue("StructureIds", out var value) && value is IReadOnlyList<string> ids)
            return ids;
        return [];
    }

    /// <summary>
    /// Mutable builder for enrichment stats.
    /// </summary>
    private class EnrichmentStatsBuilder
    {
        public int EnrichedChunks;
        public int FailedChunks;
        public int SummariesGenerated;
        public int KeywordsExtracted;
        public int ContextualTextsAdded;
        public int GraphNodes;
        public int GraphEdges;
        public int LlmCalls;
        public int TokensUsed;

        public EnrichmentStats Build(TimeSpan duration) => new()
        {
            EnrichedChunks = EnrichedChunks,
            FailedChunks = FailedChunks,
            SummariesGenerated = SummariesGenerated,
            KeywordsExtracted = KeywordsExtracted,
            ContextualTextsAdded = ContextualTextsAdded,
            GraphNodes = GraphNodes,
            GraphEdges = GraphEdges,
            LlmCalls = LlmCalls,
            TokensUsed = TokensUsed,
            Duration = duration
        };
    }
}
