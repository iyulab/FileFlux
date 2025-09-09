namespace FileFlux;

/// <summary>
/// 문서 처리기 인터페이스 - 소비 어플리케이션용 계약
/// </summary>
public interface IDocumentProcessor
{
    /// <summary>
    /// 파일 경로로부터 문서를 처리하여 청크를 생성
    /// </summary>
    /// <param name="filePath">처리할 문서 파일 경로</param>
    /// <param name="options">처리 옵션 (null이면 기본값 사용)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>처리된 문서 정보</returns>
    Task<ProcessedDocument> ProcessDocumentAsync(
        string filePath, 
        DocumentProcessingOptions? options = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 스트림으로부터 문서를 처리하여 청크를 생성
    /// </summary>
    /// <param name="stream">문서 스트림</param>
    /// <param name="fileName">파일명 (확장자 포함)</param>
    /// <param name="options">처리 옵션 (null이면 기본값 사용)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>처리된 문서 정보</returns>
    Task<ProcessedDocument> ProcessDocumentAsync(
        Stream stream, 
        string fileName, 
        DocumentProcessingOptions? options = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 문서를 스트리밍 방식으로 처리 (대용량 문서용)
    /// </summary>
    /// <param name="filePath">처리할 문서 파일 경로</param>
    /// <param name="options">처리 옵션 (null이면 기본값 사용)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>청크 스트림</returns>
    IAsyncEnumerable<ProcessedChunk> ProcessDocumentStreamAsync(
        string filePath, 
        DocumentProcessingOptions? options = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 지원되는 파일 형식 확인
    /// </summary>
    /// <param name="fileName">파일명 또는 확장자</param>
    /// <returns>지원 여부</returns>
    bool IsFileSupported(string fileName);

    /// <summary>
    /// 사용 가능한 청킹 전략 목록 조회
    /// </summary>
    /// <returns>전략 이름 목록</returns>
    IEnumerable<string> GetAvailableStrategies();

    /// <summary>
    /// 전략별 권장 설정 조회
    /// </summary>
    /// <param name="strategy">청킹 전략명</param>
    /// <returns>권장 설정</returns>
    DocumentProcessingOptions GetRecommendedOptions(string strategy);
}

/// <summary>
/// 문서 처리 결과
/// </summary>
public class DocumentProcessingResult
{
    /// <summary>
    /// 처리 성공 여부
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 처리된 문서 (성공 시)
    /// </summary>
    public ProcessedDocument? Document { get; set; }

    /// <summary>
    /// 오류 메시지 (실패 시)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 오류 코드
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// 상세 오류 정보
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// 부분 처리 결과 (일부 성공 시)
    /// </summary>
    public List<ProcessedChunk>? PartialChunks { get; set; }
}