using FileFlux;
using FileFlux.Infrastructure.Strategies;

namespace FileFlux.Infrastructure.Factories;

/// <summary>
/// 청킹 전략 팩토리 기본 구현
/// </summary>
public class ChunkingStrategyFactory : IChunkingStrategyFactory
{
    private readonly Dictionary<string, Func<IChunkingStrategy>> _strategyFactories;

    public ChunkingStrategyFactory()
    {
        _strategyFactories = new Dictionary<string, Func<IChunkingStrategy>>(StringComparer.OrdinalIgnoreCase);
        
        // Register default strategies
        RegisterDefaultStrategies();
    }
    
    private void RegisterDefaultStrategies()
    {
        RegisterStrategy(() => new IntelligentChunkingStrategy());
        RegisterStrategy(() => new SemanticChunkingStrategy());
        RegisterStrategy(() => new FixedSizeChunkingStrategy());
        RegisterStrategy(() => new ParagraphChunkingStrategy());
    }

    /// <summary>
    /// 전략 등록
    /// </summary>
    /// <param name="strategyFactory">전략 생성 함수</param>
    public void RegisterStrategy(Func<IChunkingStrategy> strategyFactory)
    {
        var strategy = strategyFactory();
        _strategyFactories[strategy.StrategyName] = strategyFactory;
    }

    public IChunkingStrategy? CreateStrategy(string strategyName)
    {
        return _strategyFactories.TryGetValue(strategyName, out var factory)
            ? factory()
            : null;
    }

    public IChunkingStrategy? GetStrategy(string strategyName)
    {
        return CreateStrategy(strategyName);
    }

    public IEnumerable<IChunkingStrategy> GetAllStrategies()
    {
        return _strategyFactories.Values
            .Select(factory => factory());
    }

    public IEnumerable<string> AvailableStrategyNames => _strategyFactories.Keys;
}