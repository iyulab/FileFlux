using FileFlux.Domain;

namespace FileFlux.Core;

/// <summary>
/// 문서 유형에 적합한 Document Parser를 생성하는 팩토리 인터페이스
/// </summary>
public interface IDocumentParserFactory
{
    /// <summary>
    /// 등록된 모든 Parser 목록
    /// </summary>
    IEnumerable<IDocumentParser> GetAvailableParsers();

    /// <summary>
    /// 원시 문서 내용을 기반으로 적합한 Parser를 찾아 반환
    /// </summary>
    /// <param name="rawContent">Reader가 추출한 원시 텍스트</param>
    /// <returns>적합한 Parser, 없으면 기본 Parser</returns>
    IDocumentParser GetParser(RawDocumentContent rawContent);

    /// <summary>
    /// 문서 유형을 지정하여 특정 Parser를 반환
    /// </summary>
    /// <param name="documentType">문서 유형 (Technical, Business, Academic 등)</param>
    /// <returns>적합한 Parser, 없으면 기본 Parser</returns>
    IDocumentParser GetParser(string documentType);

    /// <summary>
    /// 새로운 Parser를 팩토리에 등록
    /// </summary>
    /// <param name="parser">등록할 Parser</param>
    void RegisterParser(IDocumentParser parser);

    /// <summary>
    /// Parser 등록 해제
    /// </summary>
    /// <param name="parserType">해제할 Parser 타입</param>
    bool UnregisterParser(string parserType);
}