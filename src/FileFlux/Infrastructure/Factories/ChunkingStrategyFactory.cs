using System;
using System.Collections.Generic;
using System.Linq;
using FileFlux.Core;
using FileFlux.Infrastructure.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace FileFlux.Infrastructure.Factories;

/// <summary>
/// DI를 지원하는 청킹 전략 팩토리
/// Auto 전략 및 커스텀 전략 지원
/// </summary>
public class ChunkingStrategyFactory : IChunkingStrategyFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, Func<IChunkingStrategy>> _strategyFactories;
    private readonly Dictionary<string, IChunkingStrategyMetadata> _strategyMetadata;

    /// <summary>
    /// 테스트용 매개변수 없는 생성자 (Auto 전략 지원 안함)
    /// </summary>
    public ChunkingStrategyFactory() : this(new EmptyServiceProvider())
    {
    }

    public ChunkingStrategyFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _strategyFactories = new Dictionary<string, Func<IChunkingStrategy>>(StringComparer.OrdinalIgnoreCase);
        _strategyMetadata = new Dictionary<string, IChunkingStrategyMetadata>(StringComparer.OrdinalIgnoreCase);

        RegisterDefaultStrategies();
    }

    private void RegisterDefaultStrategies()
    {
        // 기본 전략들 등록 (DI 없이 생성 가능한 전략들)
        RegisterStrategy("Intelligent",
            () => new IntelligentChunkingStrategy(),
            new ChunkingStrategyMetadata
            {
                StrategyName = "Intelligent",
                Description = "Structure-aware chunking that preserves document formatting",
                OptimalForDocumentTypes = new[] { "Technical", "Markdown", "Code" },
                Strengths = new[] { "Structure preservation", "Code block integrity" },
                Limitations = new[] { "May break sentences" },
                PriorityScore = 85,
                Performance = new PerformanceCharacteristics { Speed = 3, Quality = 4, MemoryEfficiency = 4, RequiresLLM = true }
            });

        // Phase 10: 메모리 최적화된 Intelligent 전략
        RegisterStrategy("MemoryOptimizedIntelligent",
            () => new MemoryOptimizedIntelligentChunkingStrategy(),
            new ChunkingStrategyMetadata
            {
                StrategyName = "MemoryOptimizedIntelligent",
                Description = "Memory-optimized intelligent chunking with 50% reduced memory usage",
                OptimalForDocumentTypes = new[] { "Large Documents", "Technical", "Markdown" },
                Strengths = new[] { "Low memory footprint", "Structure preservation", "Object pooling" },
                Limitations = new[] { "Slightly reduced feature set" },
                PriorityScore = 88,
                Performance = new PerformanceCharacteristics { Speed = 4, Quality = 4, MemoryEfficiency = 5, RequiresLLM = false }
            });

        RegisterStrategy("Smart",
            () => new SmartChunkingStrategy(),
            new ChunkingStrategyMetadata
            {
                StrategyName = "Smart",
                Description = "Sentence-boundary aware chunking with 70% completeness guarantee",
                OptimalForDocumentTypes = new[] { "Legal", "Medical", "Academic" },
                Strengths = new[] { "Sentence integrity", "High completeness" },
                Limitations = new[] { "Slightly slower" },
                PriorityScore = 90,
                Performance = new PerformanceCharacteristics { Speed = 4, Quality = 5, MemoryEfficiency = 4, RequiresLLM = false }
            });

        RegisterStrategy("Semantic",
            () => new SemanticChunkingStrategy(),
            new ChunkingStrategyMetadata
            {
                StrategyName = "Semantic",
                Description = "Meaning-based chunking that groups related concepts",
                OptimalForDocumentTypes = new[] { "Narrative", "Essay", "Article" },
                Strengths = new[] { "Semantic coherence", "Natural boundaries" },
                Limitations = new[] { "Variable chunk sizes" },
                PriorityScore = 75,
                Performance = new PerformanceCharacteristics { Speed = 3, Quality = 4, MemoryEfficiency = 3, RequiresLLM = false }
            });

        RegisterStrategy("Hierarchical",
            () => new HierarchicalChunkingStrategy(),
            new ChunkingStrategyMetadata
            {
                StrategyName = "Hierarchical",
                Description = "Multi-level parent-child chunking for RAG retrieval with context",
                OptimalForDocumentTypes = new[] { "Technical Documentation", "Academic", "Structured Reports" },
                Strengths = new[] { "Parent-child relationships", "Context preservation", "Multi-level granularity" },
                Limitations = new[] { "Requires structured documents", "More chunks generated" },
                PriorityScore = 82,
                Performance = new PerformanceCharacteristics { Speed = 3, Quality = 5, MemoryEfficiency = 3, RequiresLLM = false }
            });

        RegisterStrategy("Paragraph",
            () => new ParagraphChunkingStrategy(),
            new ChunkingStrategyMetadata
            {
                StrategyName = "Paragraph",
                Description = "Simple paragraph-based chunking",
                OptimalForDocumentTypes = new[] { "Book", "Novel", "Simple Text" },
                Strengths = new[] { "Fast processing", "Natural paragraphs" },
                Limitations = new[] { "No semantic awareness" },
                PriorityScore = 60,
                Performance = new PerformanceCharacteristics { Speed = 5, Quality = 3, MemoryEfficiency = 5, RequiresLLM = false }
            });

        RegisterStrategy("FixedSize",
            () => new FixedSizeChunkingStrategy(),
            new ChunkingStrategyMetadata
            {
                StrategyName = "FixedSize",
                Description = "Token-based fixed size chunking",
                OptimalForDocumentTypes = new[] { "Log", "Data", "Uniform" },
                Strengths = new[] { "Predictable sizes", "Fast" },
                Limitations = new[] { "Breaks semantic units" },
                PriorityScore = 50,
                Performance = new PerformanceCharacteristics { Speed = 5, Quality = 2, MemoryEfficiency = 5, RequiresLLM = false }
            });

        // Auto 전략 등록 (DI 필요)
        RegisterStrategy("Auto",
            () =>
            {
                var strategySelector = _serviceProvider.GetRequiredService<IAdaptiveStrategySelector>();
                return new AutoChunkingStrategy(strategySelector, this, _serviceProvider);
            },
            new ChunkingStrategyMetadata
            {
                StrategyName = "Auto",
                Description = "Adaptive strategy that automatically selects the best chunking approach based on document analysis",
                OptimalForDocumentTypes = new[] { "Any" },
                Strengths = new[] { "Automatic optimization", "No manual selection needed", "Adapts to content" },
                Limitations = new[] { "Requires LLM for analysis", "Slightly slower startup" },
                PriorityScore = 100, // Highest priority as default
                Performance = new PerformanceCharacteristics { Speed = 3, Quality = 5, MemoryEfficiency = 3, RequiresLLM = true }
            });
    }

    /// <summary>
    /// 전략 등록 (메타데이터 포함)
    /// </summary>
    public void RegisterStrategy(string name, Func<IChunkingStrategy> factory, IChunkingStrategyMetadata metadata)
    {
        _strategyFactories[name] = factory;
        _strategyMetadata[name] = metadata;
    }

    /// <summary>
    /// 전략 등록 (메타데이터 없이)
    /// </summary>
    public void RegisterStrategy(Func<IChunkingStrategy> strategyFactory)
    {
        var strategy = strategyFactory();
        var name = strategy.StrategyName;
        _strategyFactories[name] = strategyFactory;

        // 기본 메타데이터 생성
        _strategyMetadata[name] = new ChunkingStrategyMetadata
        {
            StrategyName = name,
            Description = $"Custom strategy: {name}",
            OptimalForDocumentTypes = new[] { "General" },
            Strengths = new[] { "Custom implementation" },
            Limitations = new[] { "No metadata provided" },
            PriorityScore = 70,
            Performance = new PerformanceCharacteristics { Speed = 3, Quality = 3, MemoryEfficiency = 3, RequiresLLM = false }
        };
    }

    /// <summary>
    /// 전략 생성
    /// </summary>
    public IChunkingStrategy? CreateStrategy(string strategyName)
    {
        // Auto가 기본값
        if (string.IsNullOrEmpty(strategyName) || strategyName.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            strategyName = "Auto";
        }

        return _strategyFactories.TryGetValue(strategyName, out var factory)
            ? factory()
            : null;
    }

    /// <summary>
    /// 전략 가져오기 (CreateStrategy와 동일)
    /// </summary>
    public IChunkingStrategy? GetStrategy(string strategyName)
    {
        return CreateStrategy(strategyName);
    }

    /// <summary>
    /// 모든 전략 가져오기
    /// </summary>
    public IEnumerable<IChunkingStrategy> GetAllStrategies()
    {
        return _strategyFactories.Values.Select(factory => factory());
    }

    /// <summary>
    /// 사용 가능한 전략 이름들
    /// </summary>
    public IEnumerable<string> AvailableStrategyNames => _strategyFactories.Keys;

    /// <summary>
    /// 전략 메타데이터 가져오기
    /// </summary>
    public IChunkingStrategyMetadata? GetStrategyMetadata(string strategyName)
    {
        return _strategyMetadata.TryGetValue(strategyName, out var metadata) ? metadata : null;
    }

    /// <summary>
    /// 모든 전략 메타데이터 가져오기
    /// </summary>
    public IEnumerable<IChunkingStrategyMetadata> GetAllMetadata()
    {
        return _strategyMetadata.Values;
    }

    /// <summary>
    /// 커스텀 전략 등록 (사용자 제공)
    /// </summary>
    public void RegisterCustomStrategy<TStrategy>(string? customName = null)
        where TStrategy : IChunkingStrategy
    {
        RegisterStrategy(() =>
        {
            var strategy = ActivatorUtilities.CreateInstance<TStrategy>(_serviceProvider);
            return strategy;
        });
    }
}

/// <summary>
/// 간단한 메타데이터 구현
/// </summary>
internal class ChunkingStrategyMetadata : IChunkingStrategyMetadata
{
    public string StrategyName { get; set; } = "";
    public string Description { get; set; } = "";
    public IEnumerable<string> OptimalForDocumentTypes { get; set; } = new List<string>();
    public IEnumerable<string> Strengths { get; set; } = new List<string>();
    public IEnumerable<string> Limitations { get; set; } = new List<string>();
    public IEnumerable<string> RecommendedScenarios { get; set; } = new List<string>();
    public IEnumerable<string> SelectionHints { get; set; } = new List<string>();
    public int PriorityScore { get; set; }
    public PerformanceCharacteristics Performance { get; set; } = new();
}

/// <summary>
/// 테스트용 빈 ServiceProvider
/// </summary>
internal class EmptyServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType) => null;
}
