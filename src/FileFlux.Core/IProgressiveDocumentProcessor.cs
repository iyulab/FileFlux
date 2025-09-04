using FileFlux.Domain;

namespace FileFlux.Core;

/// <summary>
/// 진행률 추적이 가능한 문서 처리기 인터페이스
/// IAsyncEnumerable을 활용하여 실시간 진행률을 제공합니다
/// </summary>
public interface IProgressiveDocumentProcessor
{
    /// <summary>
    /// 문서를 비동기 스트림으로 처리하며 진행률을 실시간으로 보고합니다
    /// </summary>
    /// <param name="filePath">처리할 파일 경로</param>
    /// <param name="chunkingOptions">청킹 옵션</param>
    /// <param name="parsingOptions">파싱 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>진행률과 함께 최종 결과를 반환하는 비동기 스트림</returns>
    IAsyncEnumerable<ProcessingResult<DocumentChunk[]>> ProcessWithProgressAsync(
        string filePath,
        ChunkingOptions? chunkingOptions = null,
        DocumentParsingOptions? parsingOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 스트림을 비동기 스트림으로 처리하며 진행률을 실시간으로 보고합니다
    /// </summary>
    /// <param name="stream">문서 스트림</param>
    /// <param name="fileName">원본 파일명</param>
    /// <param name="chunkingOptions">청킹 옵션</param>
    /// <param name="parsingOptions">파싱 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>진행률과 함께 최종 결과를 반환하는 비동기 스트림</returns>
    IAsyncEnumerable<ProcessingResult<DocumentChunk[]>> ProcessWithProgressAsync(
        Stream stream,
        string fileName,
        ChunkingOptions? chunkingOptions = null,
        DocumentParsingOptions? parsingOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 개별 처리 단계를 비동기 스트림으로 실행하며 진행률을 보고합니다
    /// </summary>
    /// <param name="filePath">처리할 파일 경로</param>
    /// <param name="chunkingOptions">청킹 옵션</param>
    /// <param name="parsingOptions">파싱 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>각 단계별 결과를 반환하는 비동기 스트림</returns>
    IAsyncEnumerable<ProcessingStepResult> ProcessStepsAsync(
        string filePath,
        ChunkingOptions? chunkingOptions = null,
        DocumentParsingOptions? parsingOptions = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 처리 단계별 결과를 나타내는 클래스
/// </summary>
public class ProcessingStepResult
{
    /// <summary>
    /// 단계 유형
    /// </summary>
    public ProcessingStage Stage { get; set; }

    /// <summary>
    /// 진행률 정보
    /// </summary>
    public ProcessingProgress Progress { get; set; } = new();

    /// <summary>
    /// 단계별 결과 데이터
    /// </summary>
    public object? Data { get; set; }

    /// <summary>
    /// 성공 여부
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 오류 정보
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 진행률 추적이 가능한 청킹 전략 인터페이스
/// </summary>
public interface IProgressiveChunkingStrategy : IChunkingStrategy
{
    /// <summary>
    /// 진행률을 추적하면서 청킹을 수행합니다
    /// </summary>
    /// <param name="content">문서 내용</param>
    /// <param name="options">청킹 옵션</param>
    /// <param name="progressCallback">진행률 콜백</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>생성된 청크들</returns>
    IAsyncEnumerable<DocumentChunk> ChunkWithProgressAsync(
        ParsedDocumentContent content,
        ChunkingOptions options,
        Action<ProcessingProgress>? progressCallback = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 진행률 추적이 가능한 문서 파서 인터페이스
/// </summary>
public interface IProgressiveDocumentParser : IDocumentParser
{
    /// <summary>
    /// 진행률을 추적하면서 파싱을 수행합니다
    /// </summary>
    /// <param name="rawContent">원본 문서 내용</param>
    /// <param name="options">파싱 옵션</param>
    /// <param name="progressCallback">진행률 콜백</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>파싱된 문서 내용</returns>
    Task<ParsedDocumentContent> ParseWithProgressAsync(
        RawDocumentContent rawContent,
        DocumentParsingOptions options,
        Action<ProcessingProgress>? progressCallback = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 진행률 추적이 가능한 문서 리더 인터페이스
/// </summary>
public interface IProgressiveDocumentReader : IDocumentReader
{
    /// <summary>
    /// 진행률을 추적하면서 문서를 읽습니다
    /// </summary>
    /// <param name="filePath">파일 경로</param>
    /// <param name="progressCallback">진행률 콜백</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>원본 문서 내용</returns>
    Task<RawDocumentContent> ExtractWithProgressAsync(
        string filePath,
        Action<ProcessingProgress>? progressCallback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 진행률을 추적하면서 스트림에서 문서를 읽습니다
    /// </summary>
    /// <param name="stream">문서 스트림</param>
    /// <param name="fileName">파일명</param>
    /// <param name="progressCallback">진행률 콜백</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>원본 문서 내용</returns>
    Task<RawDocumentContent> ExtractWithProgressAsync(
        Stream stream,
        string fileName,
        Action<ProcessingProgress>? progressCallback = null,
        CancellationToken cancellationToken = default);
}