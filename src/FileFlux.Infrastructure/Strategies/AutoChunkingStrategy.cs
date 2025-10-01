﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileFlux.Core;
using FileFlux.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// 자동 적응형 청킹 전략
/// LLM이 문서를 분석하여 최적의 전략을 자동으로 선택하고 적용
/// </summary>
public class AutoChunkingStrategy : IChunkingStrategy
{
    private readonly IAdaptiveStrategySelector _strategySelector;
    private readonly IChunkingStrategyFactory _strategyFactory;
    private readonly IServiceProvider? _serviceProvider;
    private IChunkingStrategy? _selectedStrategy;
    private string _selectedStrategyName = "";
    private string _selectionReasoning = "";

    public string StrategyName => "Auto";

    public IEnumerable<string> SupportedOptions => new[]
    {
        "ForceStrategy",        // 특정 전략 강제 (테스트용)
        "ConfidenceThreshold",  // 최소 신뢰도 임계값 (기본 0.6)
        "EnableCache",          // 전략 선택 캐싱 활성화
        "MaxAnalysisTime",      // 최대 분석 시간 (초)
        "PreferSpeed",          // 속도 우선 모드
        "PreferQuality"         // 품질 우선 모드
    };

    public AutoChunkingStrategy(
        IAdaptiveStrategySelector strategySelector,
        IChunkingStrategyFactory strategyFactory,
        IServiceProvider? serviceProvider = null)
    {
        _strategySelector = strategySelector ?? throw new ArgumentNullException(nameof(strategySelector));
        _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 문서를 분석하여 최적 전략을 선택한 후 청킹 수행
    /// </summary>
    public async Task<IEnumerable<DocumentChunk>> ChunkAsync(
        DocumentContent content,
        ChunkingOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);

        // 1. 강제 전략이 지정되었는지 확인
        var forceStrategy = GetStrategyOption<string>(options, "ForceStrategy", null);
        if (!string.IsNullOrEmpty(forceStrategy))
        {
            _selectedStrategy = _strategyFactory.CreateStrategy(forceStrategy);
            _selectedStrategyName = forceStrategy;
            _selectionReasoning = $"Forced strategy: {forceStrategy}";
        }
        else
        {
            // 2. 자동 전략 선택
            await SelectOptimalStrategyAsync(content, options, cancellationToken);
        }

        // 3. 선택된 전략으로 청킹 수행
        if (_selectedStrategy == null)
        {
            // Fallback to Smart strategy
            _selectedStrategy = _strategyFactory.CreateStrategy("Smart");
            _selectedStrategyName = "Smart";
            _selectionReasoning = "Fallback to Smart strategy due to selection failure";
        }

        // 4. 메타데이터에 선택 정보 추가
        if (_selectedStrategy == null)
        {
            throw new InvalidOperationException($"Failed to select or create strategy: {_selectedStrategyName}");
        }
        
        var chunks = await _selectedStrategy.ChunkAsync(content, options, cancellationToken);
        
        // 각 청크에 Auto 전략 메타데이터 추가
        return chunks.Select(chunk =>
        {
            chunk.Strategy = $"Auto({_selectedStrategyName})";
            chunk.Props["AutoSelectedStrategy"] = _selectedStrategyName;
            chunk.Props["SelectionReasoning"] = _selectionReasoning;
            chunk.Props["SelectionConfidence"] = GetSelectionConfidence();
            return chunk;
        });
    }

    /// <summary>
    /// 청크 수 추정
    /// </summary>
    public int EstimateChunkCount(DocumentContent content, ChunkingOptions options)
    {
        // Quick estimation without full analysis
        // Use Smart strategy as default for estimation
        if (_selectedStrategy == null)
        {
            var smartStrategy = _strategyFactory.CreateStrategy("Smart");
            return smartStrategy?.EstimateChunkCount(content, options) ?? 0;
        }
        
        return _selectedStrategy.EstimateChunkCount(content, options);
    }

    /// <summary>
    /// 최적 전략 선택
    /// </summary>
    private async Task SelectOptimalStrategyAsync(
        DocumentContent content,
        ChunkingOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            // 파일 경로가 메타데이터에 있는지 확인
            var filePath = content.Metadata?.FileName ?? "unknown.txt";
            
            // 타임아웃 설정
            var maxAnalysisTime = GetStrategyOption(options, "MaxAnalysisTime", 10);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(maxAnalysisTime));
            
            // 전략 선택
            var selectionResult = await _strategySelector.SelectOptimalStrategyAsync(
                filePath,
                content,
                cts.Token);
            
            // 신뢰도 확인
            var confidenceThreshold = GetStrategyOption(options, "ConfidenceThreshold", 0.6);
            
            if (selectionResult.Confidence >= confidenceThreshold)
            {
                _selectedStrategyName = selectionResult.StrategyName;
                _selectionReasoning = selectionResult.Reasoning;
                
                // 속도/품질 우선 모드 확인
                if (GetStrategyOption(options, "PreferSpeed", false))
                {
                    _selectedStrategyName = OptimizeForSpeed(_selectedStrategyName, null);
                    _selectionReasoning += " [Speed optimized]";
                }
                else if (GetStrategyOption(options, "PreferQuality", false))
                {
                    _selectedStrategyName = OptimizeForQuality(_selectedStrategyName, null);
                    _selectionReasoning += " [Quality optimized]";
                }
            }
            else
            {
                // 신뢰도가 낮으면 기본 전략 사용
                _selectedStrategyName = DetermineDefaultStrategy(null);
                _selectionReasoning = $"Low confidence ({selectionResult.Confidence:P0}), using default strategy";
            }
            
            // 전략 인스턴스 생성
            _selectedStrategy = _strategyFactory.CreateStrategy(_selectedStrategyName);
        }
        catch (OperationCanceledException)
        {
            // 타임아웃 발생 - 빠른 기본 전략 선택
            _selectedStrategyName = "Smart";
            _selectionReasoning = "Selection timeout, using Smart strategy as default";
            _selectedStrategy = _strategyFactory.CreateStrategy(_selectedStrategyName);
        }
        catch (Exception ex)
        {
            // 오류 발생 - 안전한 기본 전략
            _selectedStrategyName = "Smart";
            _selectionReasoning = $"Selection error: {ex.Message}. Using Smart strategy as fallback";
            _selectedStrategy = _strategyFactory.CreateStrategy(_selectedStrategyName);
        }
    }

    /// <summary>
    /// 속도 최적화 전략 선택
    /// </summary>
    private string OptimizeForSpeed(string currentStrategy, List<AlternativeStrategy>? alternatives)
    {
        // 속도 우선순위: FixedSize > Paragraph > Semantic > Intelligent > Smart
        var speedPriority = new[] { "FixedSize", "Paragraph", "Semantic", "Intelligent", "Smart" };
        
        var allStrategies = new List<string> { currentStrategy };
        if (alternatives != null)
            allStrategies.AddRange(alternatives.Select(a => a.StrategyName));
        
        foreach (var fast in speedPriority)
        {
            if (allStrategies.Contains(fast))
                return fast;
        }
        
        return currentStrategy;
    }

    /// <summary>
    /// 품질 최적화 전략 선택
    /// </summary>
    private string OptimizeForQuality(string currentStrategy, List<AlternativeStrategy>? alternatives)
    {
        // 품질 우선순위: Smart > Intelligent > Semantic > Paragraph > FixedSize
        var qualityPriority = new[] { "Smart", "Intelligent", "Semantic", "Paragraph", "FixedSize" };
        
        var allStrategies = new List<string> { currentStrategy };
        if (alternatives != null)
            allStrategies.AddRange(alternatives.Select(a => a.StrategyName));
        
        foreach (var quality in qualityPriority)
        {
            if (allStrategies.Contains(quality))
                return quality;
        }
        
        return currentStrategy;
    }

    /// <summary>
    /// 문서 특성에 따른 기본 전략 결정
    /// </summary>
    private string DetermineDefaultStrategy(DocumentCharacteristics? characteristics)
    {
        // characteristics가 null이면 기본값 반환
        if (characteristics == null)
            return "Smart";
        
        // 간단한 규칙 기반 기본 전략 선택
        if (characteristics.HasCodeBlocks && characteristics.HasMarkdownHeaders)
            return "Intelligent";
        
        if (characteristics.Domain == "Legal" || characteristics.Domain == "Medical")
            return "Smart";
        
        if (characteristics.StructureComplexity > 5)
            return "Intelligent";
        
        if (characteristics.AverageSentenceLength > 25)
            return "Semantic";
        
        if (characteristics.StructureComplexity < 3 && characteristics.ParagraphCount > 5)
            return "Paragraph";
        
        // 기본값
        return "Smart";
    }

    /// <summary>
    /// 선택 신뢰도 반환
    /// </summary>
    private double GetSelectionConfidence()
    {
        // 실제 신뢰도는 선택 과정에서 저장되어야 함
        // 현재는 간단한 휴리스틱 사용
        return _selectedStrategyName switch
        {
            "Smart" => 0.85,
            "Intelligent" => 0.80,
            "Semantic" => 0.75,
            "Paragraph" => 0.70,
            "FixedSize" => 0.65,
            _ => 0.60
        };
    }

    /// <summary>
    /// 전략 옵션 가져오기
    /// </summary>
    private T GetStrategyOption<T>(ChunkingOptions options, string key, T defaultValue)
    {
        // ChunkingOptions에서 커스텀 옵션 처리
        // 실제 구현에서는 options.StrategyOptions 딕셔너리를 사용
        return defaultValue;
    }

    /// <summary>
    /// 선택된 전략 정보 반환 (디버깅/로깅용)
    /// </summary>
    public StrategySelectionInfo GetSelectionInfo()
    {
        return new StrategySelectionInfo
        {
            SelectedStrategy = _selectedStrategyName,
            Reasoning = _selectionReasoning,
            Confidence = GetSelectionConfidence()
        };
    }
}

/// <summary>
/// 전략 선택 정보
/// </summary>
public class StrategySelectionInfo
{
    public string SelectedStrategy { get; set; } = "";
    public string Reasoning { get; set; } = "";
    public double Confidence { get; set; }
}