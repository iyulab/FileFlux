namespace FileFlux.Core;

/// <summary>
/// 청킹 전략 팩토리 인터페이스
/// </summary>
public interface IChunkingStrategyFactory
{
    /// <summary>
    /// 전략 이름으로 청킹 전략 생성
    /// </summary>
    /// <param name="strategyName">전략 이름</param>
    /// <returns>해당 전략 구현체, 없으면 null</returns>
    IChunkingStrategy? CreateStrategy(string strategyName);

    /// <summary>
    /// 전략 이름으로 청킹 전략 가져오기 (CreateStrategy 별칭)
    /// </summary>
    /// <param name="strategyName">전략 이름</param>
    /// <returns>해당 전략 구현체, 없으면 null</returns>
    IChunkingStrategy? GetStrategy(string strategyName);

    /// <summary>
    /// 등록된 모든 전략 목록
    /// </summary>
    IEnumerable<IChunkingStrategy> GetAllStrategies();

    /// <summary>
    /// 사용 가능한 모든 전략 이름
    /// </summary>
    IEnumerable<string> AvailableStrategyNames { get; }
}