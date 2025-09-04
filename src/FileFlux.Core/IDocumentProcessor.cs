using FileFlux.Domain;
using System.Runtime.CompilerServices;

namespace FileFlux.Core;

/// <summary>
/// 문서 처리기 인터페이스 - Reader/Parser 분리 아키텍처
/// Reader(텍스트 추출) -> Parser(구조화) -> Chunking 파이프라인
/// </summary>
public interface IDocumentProcessor
{
    /// <summary>
    /// 파일 경로로부터 문서를 스트리밍 처리하여 청크를 하나씩 반환 (진행률 포함)
    /// </summary>
    /// <param name="filePath">문서 파일 경로</param>
    /// <param name="options">청킹 옵션</param>
    /// <param name="parsingOptions">파싱 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>처리 진행률과 청크를 포함한 비동기 스트림</returns>
    IAsyncEnumerable<ProcessingResult<DocumentChunk>> ProcessAsync(
        string filePath,
        ChunkingOptions? options = null,
        DocumentParsingOptions? parsingOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 스트림으로부터 문서를 스트리밍 처리하여 청크를 하나씩 반환 (진행률 포함)
    /// </summary>
    /// <param name="stream">문서 스트림</param>
    /// <param name="fileName">원본 파일명</param>
    /// <param name="options">청킹 옵션</param>
    /// <param name="parsingOptions">파싱 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>처리 진행률과 청크를 포함한 비동기 스트림</returns>
    IAsyncEnumerable<ProcessingResult<DocumentChunk>> ProcessAsync(
        Stream stream,
        string fileName,
        ChunkingOptions? options = null,
        DocumentParsingOptions? parsingOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 텍스트 추출 단계만 실행 (Reader만 사용)
    /// </summary>
    /// <param name="filePath">문서 파일 경로</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>추출된 원시 텍스트</returns>
    Task<RawDocumentContent> ExtractTextAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 문서 추출 (ExtractTextAsync의 간편한 별칭)
    /// </summary>
    /// <param name="filePath">문서 파일 경로</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>추출된 원시 텍스트</returns>
    async Task<RawDocumentContent> ExtractAsync(
        string filePath,
        CancellationToken cancellationToken = default)
        => await ExtractTextAsync(filePath, cancellationToken);

    /// <summary>
    /// 간소화된 문서 처리 - 청크를 직접 스트리밍 반환
    /// </summary>
    /// <param name="filePath">문서 파일 경로</param>
    /// <param name="options">청킹 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>문서 청크 스트림</returns>
    async IAsyncEnumerable<DocumentChunk> ProcessChunksAsync(
        string filePath,
        ChunkingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var result in ProcessAsync(filePath, options, null, cancellationToken))
        {
            if (result.Result != null)
            {
                yield return result.Result;
            }
        }
    }

    /// <summary>
    /// 간소화된 문서 처리 - 추출 결과부터 청크를 직접 스트리밍 반환
    /// </summary>
    /// <param name="extractResult">ExtractAsync 결과</param>
    /// <param name="options">청킹 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>문서 청크 스트림</returns>
    async IAsyncEnumerable<DocumentChunk> ProcessChunksAsync(
        RawDocumentContent extractResult,
        ChunkingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var parsedContent = await ParseAsync(extractResult, null, cancellationToken);
        var chunks = await ChunkAsync(parsedContent, options, cancellationToken);
        
        foreach (var chunk in chunks)
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// 구조화 단계만 실행 (Parser만 사용)
    /// </summary>
    /// <param name="rawContent">Reader가 추출한 원시 텍스트</param>
    /// <param name="parsingOptions">파싱 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>구조화된 문서 내용</returns>
    Task<ParsedDocumentContent> ParseAsync(
        RawDocumentContent rawContent,
        DocumentParsingOptions? parsingOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 청킹 단계만 실행 (기존 ChunkingStrategy 사용)
    /// </summary>
    /// <param name="parsedContent">Parser가 구조화한 내용</param>
    /// <param name="options">청킹 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>문서 청크 배열</returns>
    Task<DocumentChunk[]> ChunkAsync(
        ParsedDocumentContent parsedContent,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 편의 메서드: 모든 청크를 배열로 수집하여 반환
    /// </summary>
    /// <param name="filePath">문서 파일 경로</param>
    /// <param name="options">청킹 옵션</param>
    /// <param name="parsingOptions">파싱 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>처리된 문서 청크 배열</returns>
    async Task<DocumentChunk[]> ProcessToArrayAsync(
        string filePath,
        ChunkingOptions? options = null,
        DocumentParsingOptions? parsingOptions = null,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<DocumentChunk>();
        await foreach (var result in ProcessAsync(filePath, options, parsingOptions, cancellationToken))
        {
            if (result.Result != null)
            {
                chunks.Add(result.Result);
            }
        }
        return chunks.ToArray();
    }

    /// <summary>
    /// 편의 메서드: 스트림에서 모든 청크를 배열로 수집하여 반환
    /// </summary>
    /// <param name="stream">문서 스트림</param>
    /// <param name="fileName">원본 파일명</param>
    /// <param name="options">청킹 옵션</param>
    /// <param name="parsingOptions">파싱 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>처리된 문서 청크 배열</returns>
    async Task<DocumentChunk[]> ProcessToArrayAsync(
        Stream stream,
        string fileName,
        ChunkingOptions? options = null,
        DocumentParsingOptions? parsingOptions = null,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<DocumentChunk>();
        await foreach (var result in ProcessAsync(stream, fileName, options, parsingOptions, cancellationToken))
        {
            if (result.Result != null)
            {
                chunks.Add(result.Result);
            }
        }
        return chunks.ToArray();
    }
}