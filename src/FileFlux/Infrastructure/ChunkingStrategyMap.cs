using FileFlux.Core;
using FluxCuratorStrategy = FluxCurator.Core.Domain.ChunkingStrategy;

namespace FileFlux.Infrastructure;

/// <summary>
/// Maps a <see cref="ChunkingOptions.Strategy"/> name (<see cref="ChunkingStrategies"/>) to the FluxCurator strategy that
/// runs it. One map for every processor, so they cannot disagree about which names exist.
/// </summary>
internal static class ChunkingStrategyMap
{
    /// <summary>
    /// The FluxCurator strategy for <paramref name="strategy"/> (case-insensitive). An unset strategy is
    /// <see cref="ChunkingStrategies.Auto"/>; a name that is not a strategy throws — it used to run as Auto without a
    /// word, so a typo or a name from an older release chunked the document some other way than the caller asked.
    /// </summary>
    /// <exception cref="ArgumentException">The name is not one of <see cref="ChunkingStrategies.All"/>.</exception>
    public static FluxCuratorStrategy ToFluxCurator(string? strategy)
    {
        if (string.IsNullOrWhiteSpace(strategy))
            return FluxCuratorStrategy.Auto;

        return strategy.Trim().ToLowerInvariant() switch
        {
            "auto" => FluxCuratorStrategy.Auto,
            "sentence" => FluxCuratorStrategy.Sentence,
            "paragraph" => FluxCuratorStrategy.Paragraph,
            "token" => FluxCuratorStrategy.Token,
            "semantic" => FluxCuratorStrategy.Semantic,
            "hierarchical" => FluxCuratorStrategy.Hierarchical,
            _ => throw new ArgumentException(
                $"Unknown chunking strategy '{strategy}'. Use one of: {string.Join(", ", ChunkingStrategies.All)}.",
                nameof(strategy)),
        };
    }
}
