using System.Threading;
using System.Threading.Tasks;
using FileFlux.Domain;

namespace FileFlux.Core;

/// <summary>
/// 문서 분석을 통한 적응형 전략 선택 인터페이스
/// </summary>
public interface IAdaptiveStrategySelector
{
    /// <summary>
    /// 문서 분석을 통해 최적의 청킹 전략을 선택합니다
    /// </summary>
    /// <param name="filePath">분석할 파일 경로</param>
    /// <param name="extractedContent">추출된 문서 콘텐츠 (선택사항)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>전략 선택 결과</returns>
    Task<StrategySelectionResult> SelectOptimalStrategyAsync(
        string filePath,
        DocumentContent? extractedContent = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 전략 선택 결과
/// </summary>
public class StrategySelectionResult
{
    /// <summary>
    /// 선택된 전략 이름
    /// </summary>
    public string StrategyName { get; set; } = "";
    
    /// <summary>
    /// 선택 이유 설명
    /// </summary>
    public string Reasoning { get; set; } = "";
    
    /// <summary>
    /// 선택 신뢰도 (0.0 ~ 1.0)
    /// </summary>
    public double Confidence { get; set; }
    
    /// <summary>
    /// LLM 사용 여부
    /// </summary>
    public bool UsedLLM { get; set; }
}