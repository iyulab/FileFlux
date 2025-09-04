using FileFlux.Domain;

namespace FileFlux.Core;

/// <summary>
/// 청킹 전략 인터페이스 - 문서를 청크로 분할하는 다양한 방법 정의
/// </summary>
public interface IChunkingStrategy
{
    /// <summary>
    /// 전략 이름
    /// </summary>
    string StrategyName { get; }

    /// <summary>
    /// 문서 내용을 청크로 분할
    /// </summary>
    /// <param name="content">문서 내용</param>
    /// <param name="options">청킹 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>생성된 청크 목록</returns>
    Task<IEnumerable<DocumentChunk>> ChunkAsync(
        DocumentContent content,
        ChunkingOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 예상 청크 개수 계산 (성능 최적화용)
    /// </summary>
    /// <param name="content">문서 내용</param>
    /// <param name="options">청킹 옵션</param>
    /// <returns>예상 청크 개수</returns>
    int EstimateChunkCount(DocumentContent content, ChunkingOptions options);

    /// <summary>
    /// 전략에서 지원하는 옵션 키 목록
    /// </summary>
    IEnumerable<string> SupportedOptions { get; }
}