namespace FileFlux.Infrastructure.Adapters;

using FileFlux.Domain;
using FluxCuratorChunk = FluxCurator.Core.Domain.DocumentChunk;
using FluxCuratorMetadata = FluxCurator.Core.Domain.ChunkMetadata;
using FluxCuratorLocation = FluxCurator.Core.Domain.ChunkLocation;

/// <summary>
/// Adapter for converting between FluxCurator and FileFlux DocumentChunk types.
/// Enables seamless integration between the two libraries.
/// </summary>
public static class FluxCuratorChunkAdapter
{
    /// <summary>
    /// Converts a FluxCurator DocumentChunk to a FileFlux DocumentChunk.
    /// </summary>
    /// <param name="source">The FluxCurator chunk to convert.</param>
    /// <param name="parsedId">Optional parsed document ID for traceability.</param>
    /// <param name="rawId">Optional raw document ID for traceability.</param>
    /// <returns>A FileFlux DocumentChunk with equivalent data.</returns>
    public static DocumentChunk ToFileFluxChunk(
        this FluxCuratorChunk source,
        Guid? parsedId = null,
        Guid? rawId = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var chunk = new DocumentChunk
        {
            Id = Guid.TryParse(source.Id, out var id) ? id : Guid.NewGuid(),
            ParsedId = parsedId ?? Guid.Empty,
            RawId = rawId ?? Guid.Empty,
            Content = source.Content,
            Index = source.Index,
            Location = ConvertLocation(source.Location),
            Quality = source.Metadata.QualityScore,
            Density = source.Metadata.DensityScore,
            Importance = CalculateImportance(source),
            Strategy = source.Metadata.Strategy.ToString(),
            Tokens = source.Metadata.EstimatedTokenCount,
            ContextDependency = CalculateContextDependency(source)
        };

        // Copy custom properties
        if (source.Metadata.Custom != null)
        {
            foreach (var kvp in source.Metadata.Custom)
            {
                chunk.Props[kvp.Key] = kvp.Value;
            }
        }

        // Set language info in source metadata
        if (!string.IsNullOrEmpty(source.Metadata.LanguageCode))
        {
            chunk.SourceInfo.Language = source.Metadata.LanguageCode;
            chunk.SourceInfo.LanguageConfidence = 1.0; // FluxCurator provides detected language
        }

        return chunk;
    }

    /// <summary>
    /// Converts a FileFlux DocumentChunk to a FluxCurator DocumentChunk.
    /// </summary>
    /// <param name="source">The FileFlux chunk to convert.</param>
    /// <returns>A FluxCurator DocumentChunk with equivalent data.</returns>
    public static FluxCuratorChunk ToFluxCuratorChunk(this DocumentChunk source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var chunk = new FluxCuratorChunk
        {
            Id = source.Id.ToString("N"),
            Content = source.Content,
            Index = source.Index,
            TotalChunks = source.SourceInfo.ChunkCount,
            Location = ConvertLocation(source.Location),
            Metadata = new FluxCuratorMetadata
            {
                LanguageCode = source.SourceInfo.Language,
                EstimatedTokenCount = source.Tokens,
                Strategy = ParseStrategy(source.Strategy),
                QualityScore = (float)source.Quality,
                DensityScore = (float)source.Density,
                ContainsSectionHeader = source.Location.HeadingPath.Count > 0,
                CreatedAt = new DateTimeOffset(source.CreatedAt, TimeSpan.Zero)
            }
        };

        // Copy custom properties
        if (source.Props.Count > 0)
        {
            chunk.Metadata.Custom = new Dictionary<string, object>(source.Props);
        }

        return chunk;
    }

    /// <summary>
    /// Converts a collection of FluxCurator chunks to FileFlux chunks.
    /// </summary>
    public static IReadOnlyList<DocumentChunk> ToFileFluxChunks(
        this IEnumerable<FluxCuratorChunk> sources,
        Guid? parsedId = null,
        Guid? rawId = null)
    {
        return sources.Select(s => s.ToFileFluxChunk(parsedId, rawId)).ToList();
    }

    /// <summary>
    /// Converts a collection of FileFlux chunks to FluxCurator chunks.
    /// </summary>
    public static IReadOnlyList<FluxCuratorChunk> ToFluxCuratorChunks(
        this IEnumerable<DocumentChunk> sources)
    {
        return sources.Select(s => s.ToFluxCuratorChunk()).ToList();
    }

    private static SourceLocation ConvertLocation(FluxCuratorLocation source)
    {
        var location = new SourceLocation
        {
            StartChar = source.StartPosition,
            EndChar = source.EndPosition,
            Section = source.SectionPath
        };

        // Parse section path into heading path
        if (!string.IsNullOrEmpty(source.SectionPath))
        {
            location.HeadingPath = source.SectionPath
                .Split(" > ", StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        return location;
    }

    private static FluxCuratorLocation ConvertLocation(SourceLocation source)
    {
        return new FluxCuratorLocation
        {
            StartPosition = source.StartChar,
            EndPosition = source.EndChar,
            StartLine = source.StartPage ?? 1,
            EndLine = source.EndPage ?? 1,
            SectionPath = source.HeadingPath.Count > 0
                ? string.Join(" > ", source.HeadingPath)
                : source.Section
        };
    }

    private static FluxCurator.Core.Domain.ChunkingStrategy ParseStrategy(string strategy)
    {
        if (string.IsNullOrEmpty(strategy))
            return FluxCurator.Core.Domain.ChunkingStrategy.Auto;

        return strategy.ToLowerInvariant() switch
        {
            "sentence" => FluxCurator.Core.Domain.ChunkingStrategy.Sentence,
            "paragraph" => FluxCurator.Core.Domain.ChunkingStrategy.Paragraph,
            "token" => FluxCurator.Core.Domain.ChunkingStrategy.Token,
            "semantic" => FluxCurator.Core.Domain.ChunkingStrategy.Semantic,
            "hierarchical" => FluxCurator.Core.Domain.ChunkingStrategy.Hierarchical,
            _ => FluxCurator.Core.Domain.ChunkingStrategy.Auto
        };
    }

    private static double CalculateImportance(FluxCuratorChunk chunk)
    {
        // Calculate importance based on hierarchy level if available
        if (chunk.Metadata.Custom?.TryGetValue("HierarchyLevel", out var levelObj) == true &&
            levelObj is int level)
        {
            // Lower level = higher importance (root level 0 is most important)
            return Math.Max(0.5, 1.0 - (level * 0.1));
        }

        // Default to quality score if no hierarchy info
        return chunk.Metadata.QualityScore;
    }

    private static double CalculateContextDependency(FluxCuratorChunk chunk)
    {
        // Higher context dependency if chunk has overlap from previous
        if (!string.IsNullOrEmpty(chunk.Metadata.OverlapFromPrevious))
        {
            var overlapRatio = (double)chunk.Metadata.OverlapFromPrevious.Length / chunk.Content.Length;
            return Math.Min(1.0, overlapRatio * 2); // Scale overlap to context dependency
        }

        // Lower context dependency if starts at sentence boundary
        if (chunk.Metadata.StartsAtSentenceBoundary && chunk.Metadata.EndsAtSentenceBoundary)
        {
            return 0.1;
        }

        return 0.3; // Default moderate context dependency
    }
}
