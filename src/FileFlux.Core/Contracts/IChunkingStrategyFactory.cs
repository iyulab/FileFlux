namespace FileFlux.Core;

/// <summary>
/// Factory interface for creating chunking strategies
/// </summary>
public interface IChunkingStrategyFactory
{
    /// <summary>
    /// Get a chunking strategy by name
    /// </summary>
    /// <param name="strategyName">Strategy name</param>
    /// <returns>Chunking strategy instance, or null if not found</returns>
    IChunkingStrategy? GetStrategy(string strategyName);

    /// <summary>
    /// Get all available strategies
    /// </summary>
    /// <returns>Collection of all registered strategies</returns>
    IEnumerable<IChunkingStrategy> GetAllStrategies();

    /// <summary>
    /// Get available strategy names
    /// </summary>
    /// <returns>List of strategy names</returns>
    IEnumerable<string> GetAvailableStrategyNames();

    /// <summary>
    /// Check if a strategy is available
    /// </summary>
    /// <param name="strategyName">Strategy name</param>
    /// <returns>True if strategy exists</returns>
    bool HasStrategy(string strategyName);
}
