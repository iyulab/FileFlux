using FileFlux.Domain;

namespace FileFlux;

/// <summary>
/// 문서 Reader 인터페이스 - 순수 텍스트 추출에 집중
/// LLM 기능 개입 없이 파일 형식별 텍스트 추출만 담당
/// </summary>
public interface IDocumentReader
{
    /// <summary>
    /// 지원하는 파일 확장자 목록
    /// </summary>
    IEnumerable<string> SupportedExtensions { get; }

    /// <summary>
    /// Reader 고유 식별자 (로깅/디버깅용)
    /// </summary>
    string ReaderType { get; }

    /// <summary>
    /// 해당 파일을 읽을 수 있는지 확인
    /// </summary>
    /// <param name="fileName">파일명</param>
    /// <returns>읽기 가능 여부</returns>
    bool CanRead(string fileName);

    /// <summary>
    /// 파일에서 순수 텍스트 추출
    /// </summary>
    /// <param name="filePath">파일 경로</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>추출된 원시 텍스트 내용</returns>
    Task<RawContent> ExtractAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 스트림에서 순수 텍스트 추출
    /// </summary>
    /// <param name="stream">문서 스트림</param>
    /// <param name="fileName">원본 파일명</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>추출된 원시 텍스트 내용</returns>
    Task<RawContent> ExtractAsync(Stream stream, string fileName, CancellationToken cancellationToken = default);
}