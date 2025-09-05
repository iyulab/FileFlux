using FileFlux.Domain;
using System.Runtime.CompilerServices;

namespace FileFlux;

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

    // RAG Quality Analysis Methods - Phase 6.5 Enhancement
    
    /// <summary>
    /// 문서 처리 품질을 분석하여 RAG 시스템 최적화를 위한 리포트 생성
    /// 내부 벤치마킹과 동일한 로직을 사용하여 일관성 보장
    /// </summary>
    /// <param name="filePath">분석할 문서 파일 경로</param>
    /// <param name="options">청킹 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>종합적인 품질 분석 리포트</returns>
    Task<DocumentQualityReport> AnalyzeQualityAsync(
        string filePath, 
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 문서 기반 QA 벤치마크 데이터셋 생성
    /// RAG 시스템 성능 측정 및 청크 답변 가능성 평가에 필수
    /// </summary>
    /// <param name="filePath">문서 파일 경로</param>
    /// <param name="questionCount">생성할 질문 수 (기본값: 20)</param>
    /// <param name="existingQA">기존 QA 데이터셋 (병합용, 선택사항)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>생성된 질문과 검증 메트릭이 포함된 QA 벤치마크</returns>
    Task<QABenchmark> GenerateQAAsync(
        string filePath, 
        int questionCount = 20, 
        QABenchmark? existingQA = null,
        CancellationToken cancellationToken = default);
}