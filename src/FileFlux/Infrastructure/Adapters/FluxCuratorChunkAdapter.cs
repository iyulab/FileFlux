namespace FileFlux.Infrastructure.Adapters;

using System.Globalization;
using System.Text.RegularExpressions;
using FileFlux.Core;
using FluxCurator.Core.Infrastructure.Refining;
using FluxCuratorChunk = FluxCurator.Core.Domain.DocumentChunk;
using FluxCuratorMetadata = FluxCurator.Core.Domain.ChunkMetadata;
using FluxCuratorLocation = FluxCurator.Core.Domain.ChunkLocation;

/// <summary>
/// Adapter for converting between FluxCurator and FileFlux DocumentChunk types.
/// Enables seamless integration between the two libraries.
/// </summary>
public static partial class FluxCuratorChunkAdapter
{
    // Internal structural/boundary markers (e.g. <!-- HEADING_START:H2 -->, <!-- TABLE_START -->,
    // <!-- DOCUMENT_IMAGES_START -->) are emitted by FileFlux readers as chunking signals and consumed
    // by boundary detection. They must never leak into the final chunk content surfaced to consumers
    // (search display, embedding input, LLM context). This strips every HTML comment FileFlux may emit
    // so downstream consumers no longer need their own marker-stripping regex.
    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex InternalMarkerRegex();

    // First markdown heading marker in the chunk, capturing its level (1-6).
    [GeneratedRegex(@"<!--\s*HEADING_START:H(\d+)\s*-->", RegexOptions.Singleline)]
    private static partial Regex HeadingLevelRegex();

    // Three or more consecutive newlines left behind after marker removal.
    [GeneratedRegex(@"(\r?\n){3,}")]
    private static partial Regex ExcessBlankLinesRegex();

    /// <summary>
    /// Removes internal structural markers from chunk content. Returns the cleaned text with
    /// collapsed blank lines and trimmed edges. Safe to call on already-clean content (no-op).
    /// </summary>
    internal static string StripInternalMarkers(string content) => StripInternalMarkers(content, cutFence: false);

    /// <summary>
    /// <see cref="StripInternalMarkers(string)"/> for a chunk whose position in the text it was cut from is known:
    /// <paramref name="cutFence"/> is true when the chunk is part of a fenced block that a chunk boundary cut - which the
    /// text alone cannot tell for a piece from the middle of a block (it holds no fence line at all).
    /// </summary>
    internal static string StripInternalMarkers(string content, bool cutFence)
    {
        if (string.IsNullOrEmpty(content) || !content.Contains("<!--", StringComparison.Ordinal))
        {
            return content;
        }

        // A fenced code block is the code's own: an HTML comment in it (a sample page) and its blank lines stay.
        // A block larger than a chunk is cut across two of them, and then neither part is a closed fence to hide (the head has
        // an unclosed one, the tail starts with a closing fence that reads as an opening one): such a chunk loses only
        // FileFlux's own structural markers - any other comment and the blank lines may be the code's.
        if (cutFence || HasCutFence(content))
        {
            // A marker on a line of its own goes with its line break; a leading space is the code's indentation.
            var withoutMarkers = StructuralMarkerRegex().Replace(StructuralMarkerLineRegex().Replace(content, string.Empty), string.Empty);
            return withoutMarkers.TrimEnd().TrimStart('\r', '\n');
        }

        var stripped = MarkdownFences.Shielded(
            content,
            text => ExcessBlankLinesRegex().Replace(InternalMarkerRegex().Replace(text, string.Empty), "\n\n"));
        return stripped.Trim();
    }

    /// <summary>The fenced blocks of the text the chunks were cut from (empty when there is none), for <see cref="IsPartOfCutFence"/>.</summary>
    internal static IReadOnlyList<(int Start, int End)> FenceRanges(string? text) =>
        string.IsNullOrEmpty(text) ? [] : MarkdownFences.FindRanges(text, includeUnclosed: true);

    /// <summary>
    /// Whether the chunk (its own span, and the overlap taken from the previous chunk in front of it) touches a fenced block
    /// without containing it whole: it is the head, the tail or a middle piece of a block a chunk boundary cut.
    /// </summary>
    internal static bool IsPartOfCutFence(IReadOnlyList<(int Start, int End)> fenceRanges, FluxCuratorChunk chunk)
    {
        if (fenceRanges.Count == 0)
            return false;

        var start = Math.Max(0, chunk.Location.StartPosition - (chunk.Metadata.OverlapFromPrevious?.Length ?? 0));
        var end = chunk.Location.EndPosition;
        foreach (var (fenceStart, fenceEnd) in fenceRanges)
        {
            if (fenceStart < end && start < fenceEnd && !(start <= fenceStart && fenceEnd <= end))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the chunk text may hold part of a fenced block cut by a chunk boundary: a fence that is never closed, or an odd
    /// number of fence lines (a closing fence at the start pairs with the next fence line and leaves one open or pairs with
    /// the closing fence of the next block). It errs towards true - the cost of a false positive is a kept comment.
    /// </summary>
    private static bool HasCutFence(string content)
    {
        var all = MarkdownFences.FindRanges(content, includeUnclosed: true);
        var closed = MarkdownFences.FindRanges(content, includeUnclosed: false);
        return all.Count != closed.Count || FenceLine().Count(content) % 2 == 1;
    }

    // FileFlux's own structural markers, the closed set the readers and the normalizer write: NAME_START / NAME_END (optionally
    // :ARG) for the names CODE, HEADING, LIST, TABLE, IMAGE and the three image sections of the Office readers, the three
    // table notes of the normalizer, and the source-span marker. A comment of a user's own that has the same shape
    // (<!-- FOOTER_END -->, <!-- table: users -->) is not one of them.
    private const string StructuralMarker =
        @"<!--\s*(?:(?:CODE|HEADING|LIST|TABLE|IMAGE|DOCUMENT_IMAGES|PRESENTATION_IMAGES|SPREADSHEET_IMAGES)_(?:START|END)(?::[A-Za-z0-9_]+)?"
        + @"|table:\s*(?:No data rows in table|Column count varies[^\r\n]*?|Complex table structure[^\r\n]*?)"
        + @"|ffspan:\d+)\s*-->";

    [GeneratedRegex(StructuralMarker)]
    private static partial Regex StructuralMarkerRegex();

    // The same marker alone on its line, with the line break that ends the line.
    [GeneratedRegex(@"^[ \t]*" + StructuralMarker + @"[ \t]*(?:\r?\n|$)", RegexOptions.Multiline)]
    private static partial Regex StructuralMarkerLineRegex();

    // A line that opens or closes a fence: three or more backticks or tildes after at most the indentation.
    [GeneratedRegex(@"^[ \t]*(?:`{3,}|~{3,})", RegexOptions.Multiline)]
    private static partial Regex FenceLine();

    /// <summary>
    /// Extracts the markdown heading level (1-6) from the chunk's first HEADING_START marker,
    /// or null when the chunk contains no heading marker.
    /// </summary>
    internal static int? TryExtractHeadingLevel(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        var match = HeadingLevelRegex().Match(content);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level)
            ? level
            : null;
    }

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
        Guid? rawId = null) => ToFileFluxChunk(source, parsedId, rawId, fenceRanges: []);

    /// <summary>
    /// <see cref="ToFileFluxChunk(FluxCuratorChunk, Guid?, Guid?)"/> with the fenced blocks of the text the chunk was cut
    /// from (<see cref="FenceRanges"/>), so a piece of a block cut across chunks keeps its code as it is.
    /// </summary>
    internal static DocumentChunk ToFileFluxChunk(
        FluxCuratorChunk source,
        Guid? parsedId,
        Guid? rawId,
        IReadOnlyList<(int Start, int End)> fenceRanges)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Lift the heading level from the structural marker before it is stripped (no info loss),
        // then remove all internal markers so consumers receive clean content.
        var headingLevel = TryExtractHeadingLevel(source.Content);
        var cleanContent = StripInternalMarkers(source.Content, IsPartOfCutFence(fenceRanges, source));

        var chunk = new DocumentChunk
        {
            Id = Guid.TryParse(source.Id, out var id) ? id : Guid.NewGuid(),
            ParsedId = parsedId ?? Guid.Empty,
            RawId = rawId ?? Guid.Empty,
            Content = cleanContent,
            ChunkIndex = source.ChunkIndex,
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

        // Preserve the structural heading level lifted before marker stripping.
        if (headingLevel is int level)
        {
            chunk.Props[ChunkPropsKeys.HierarchyHeadingLevel] = level;
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
            ChunkIndex = source.ChunkIndex,
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

    /// <summary><see cref="ToFileFluxChunks(IEnumerable{FluxCuratorChunk}, Guid?, Guid?)"/> for chunks cut from <paramref name="sourceText"/>.</summary>
    internal static IReadOnlyList<DocumentChunk> ToFileFluxChunks(
        IEnumerable<FluxCuratorChunk> sources,
        Guid? parsedId,
        Guid? rawId,
        string sourceText)
    {
        var fenceRanges = FenceRanges(sourceText);
        return sources.Select(s => ToFileFluxChunk(s, parsedId, rawId, fenceRanges)).ToList();
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
