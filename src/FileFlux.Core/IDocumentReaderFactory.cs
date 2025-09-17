using FileFlux.Domain;

namespace FileFlux;

/// <summary>
/// 파일 형식에 적합한 Document Reader를 생성하는 팩토리 인터페이스
/// </summary>
public interface IDocumentReaderFactory
{
    /// <summary>
    /// 등록된 모든 Reader 목록
    /// </summary>
    IEnumerable<IDocumentReader> GetAvailableReaders();

    /// <summary>
    /// 파일명을 기반으로 적합한 Reader를 찾아 반환
    /// </summary>
    /// <param name="fileName">파일명</param>
    /// <returns>적합한 Reader, 없으면 null</returns>
    IDocumentReader? GetReader(string fileName);

    /// <summary>
    /// 파일 내용을 기반으로 적합한 Reader를 찾아 반환
    /// </summary>
    /// <param name="rawContent">추출된 원시 내용</param>
    /// <returns>적합한 Reader, 없으면 null</returns>
    IDocumentReader? GetReader(RawDocumentContent rawContent);

    /// <summary>
    /// 새로운 Reader를 팩토리에 등록
    /// </summary>
    /// <param name="reader">등록할 Reader</param>
    void RegisterReader(IDocumentReader reader);

    /// <summary>
    /// Reader 등록 해제
    /// </summary>
    /// <param name="readerType">해제할 Reader 타입</param>
    bool UnregisterReader(string readerType);

    /// <summary>
    /// 지원하는 모든 파일 확장자 목록 조회
    /// </summary>
    /// <returns>지원하는 확장자 목록 (.pdf, .docx 등)</returns>
    IEnumerable<string> GetSupportedExtensions();

    /// <summary>
    /// 특정 확장자가 지원되는지 확인
    /// </summary>
    /// <param name="extension">확장자 (.pdf, .docx 등)</param>
    /// <returns>지원 여부</returns>
    bool IsExtensionSupported(string extension);

    /// <summary>
    /// 확장자별로 어떤 Reader가 처리하는지 매핑 정보 제공
    /// </summary>
    /// <returns>확장자-Reader 매핑 정보</returns>
    IReadOnlyDictionary<string, string> GetExtensionReaderMapping();
}