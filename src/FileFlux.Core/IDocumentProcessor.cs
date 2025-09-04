using FileFlux.Domain;
using System.Runtime.CompilerServices;

namespace FileFlux.Core;

/// <summary>
/// 문서 처리기 인터페이스 - 간결한 핵심 API만 제공
/// </summary>
public interface IDocumentProcessor
{
    /// <summary>
    /// 문서를 처리하여 청크를 스트리밍 반환 (메인 API)
    /// </summary>
    /// <param name="filePath">문서 파일 경로</param>
    /// <param name="options">청킹 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>문서 청크 스트림</returns>
    IAsyncEnumerable<DocumentChunk> ProcessAsync(
        string filePath,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 스트림으로부터 문서를 처리하여 청크를 스트리밍 반환
    /// </summary>
    /// <param name="stream">문서 스트림</param>
    /// <param name="fileName">원본 파일명</param>
    /// <param name="options">청킹 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>문서 청크 스트림</returns>
    IAsyncEnumerable<DocumentChunk> ProcessAsync(
        Stream stream,
        string fileName,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 추출된 원시 문서부터 처리하여 청크를 스트리밍 반환
    /// </summary>
    /// <param name="rawContent">추출된 원시 문서 내용</param>
    /// <param name="options">청킹 옵션</param>
    /// <param name="parsingOptions">파싱 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>문서 청크 스트림</returns>
    IAsyncEnumerable<DocumentChunk> ProcessAsync(
        RawDocumentContent rawContent,
        ChunkingOptions? options = null,
        DocumentParsingOptions? parsingOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 구조화 단계 실행 (고급 사용자용)
    /// </summary>
    /// <param name="rawContent">추출된 원시 텍스트</param>
    /// <param name="parsingOptions">파싱 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>구조화된 문서 내용</returns>
    Task<ParsedDocumentContent> ParseAsync(
        RawDocumentContent rawContent,
        DocumentParsingOptions? parsingOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 청킹 단계 실행 (고급 사용자용)
    /// </summary>
    /// <param name="parsedContent">구조화된 문서 내용</param>
    /// <param name="options">청킹 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>문서 청크 배열</returns>
    Task<DocumentChunk[]> ChunkAsync(
        ParsedDocumentContent parsedContent,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 텍스트 추출 단계 실행 (고급 사용자용)
    /// </summary>
    /// <param name="filePath">문서 파일 경로</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>추출된 원시 텍스트</returns>
    Task<RawDocumentContent> ExtractAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}