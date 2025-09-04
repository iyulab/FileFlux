using FileFlux.Domain;
using FileFlux.SampleApp.Models;

namespace FileFlux.SampleApp.Services;

/// <summary>
/// 벡터 스토어 서비스 인터페이스
/// </summary>
public interface IVectorStoreService
{
    /// <summary>
    /// 문서 청크들을 저장하고 임베딩 생성
    /// </summary>
    Task<DocumentRecord> StoreDocumentAsync(
        string filePath,
        IEnumerable<DocumentChunk> chunks,
        string strategy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 쿼리에 대한 유사한 청크 검색
    /// </summary>
    Task<IEnumerable<ChunkRecord>> SearchSimilarChunksAsync(
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// RAG 쿼리 실행 및 응답 생성
    /// </summary>
    Task<QueryRecord> ExecuteRagQueryAsync(
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 저장된 문서 목록 조회
    /// </summary>
    Task<IEnumerable<DocumentRecord>> GetDocumentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 쿼리 기록 조회
    /// </summary>
    Task<IEnumerable<QueryRecord>> GetQueryHistoryAsync(int limit = 50, CancellationToken cancellationToken = default);
}