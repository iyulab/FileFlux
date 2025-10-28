using System.Collections.Generic;

namespace FileFlux.Core;

/// <summary>
/// 청킹 전략의 메타데이터를 정의하는 인터페이스
/// 자동 전략 선택을 위한 정보 제공
/// </summary>
public interface IChunkingStrategyMetadata
{
    /// <summary>
    /// 전략 이름
    /// </summary>
    string StrategyName { get; }

    /// <summary>
    /// 전략에 대한 상세 설명
    /// LLM이 이 설명을 기반으로 적절한 전략을 선택함
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 이 전략이 최적인 문서 타입들
    /// </summary>
    IEnumerable<string> OptimalForDocumentTypes { get; }

    /// <summary>
    /// 이 전략의 특장점
    /// </summary>
    IEnumerable<string> Strengths { get; }

    /// <summary>
    /// 이 전략의 약점이나 제한사항
    /// </summary>
    IEnumerable<string> Limitations { get; }

    /// <summary>
    /// 권장 사용 시나리오
    /// </summary>
    IEnumerable<string> RecommendedScenarios { get; }

    /// <summary>
    /// 이 전략을 선택하기 위한 키워드나 패턴
    /// </summary>
    IEnumerable<string> SelectionHints { get; }

    /// <summary>
    /// 우선순위 점수 (0-100, 높을수록 우선 고려)
    /// </summary>
    int PriorityScore { get; }

    /// <summary>
    /// 성능 특성
    /// </summary>
    PerformanceCharacteristics Performance { get; }
}

/// <summary>
/// 전략의 성능 특성
/// </summary>
public class PerformanceCharacteristics
{
    /// <summary>
    /// 처리 속도 (1-5, 5가 가장 빠름)
    /// </summary>
    public int Speed { get; set; }

    /// <summary>
    /// 품질 수준 (1-5, 5가 가장 높은 품질)
    /// </summary>
    public int Quality { get; set; }

    /// <summary>
    /// 메모리 효율성 (1-5, 5가 가장 효율적)
    /// </summary>
    public int MemoryEfficiency { get; set; }

    /// <summary>
    /// LLM API 호출 필요 여부
    /// </summary>
    public bool RequiresLLM { get; set; }
}

/// <summary>
/// 확장된 청킹 전략 인터페이스 (메타데이터 포함)
/// </summary>
public interface IChunkingStrategyWithMetadata : IChunkingStrategy
{
    /// <summary>
    /// 전략 메타데이터
    /// </summary>
    IChunkingStrategyMetadata Metadata { get; }
}
