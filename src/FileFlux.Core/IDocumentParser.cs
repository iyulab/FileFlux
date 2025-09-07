using FileFlux.Domain;

namespace FileFlux;

/// <summary>
/// LLM을 활용한 문서 구조화 Parser 인터페이스
/// 순수 텍스트를 받아 일관된 구조로 분석, 강화, 메타정보 추출
/// </summary>
public interface IDocumentParser
{
    /// <summary>
    /// 지원하는 문서 유형 (Technical, Business, Legal 등)
    /// </summary>
    IEnumerable<string> SupportedDocumentTypes { get; }

    /// <summary>
    /// Parser 고유 식별자
    /// </summary>
    string ParserType { get; }

    /// <summary>
    /// 해당 문서 타입을 파싱할 수 있는지 확인
    /// </summary>
    /// <param name="rawContent">Reader가 추출한 원시 텍스트</param>
    /// <returns>파싱 가능 여부</returns>
    bool CanParse(RawDocumentContent rawContent);

    /// <summary>
    /// 원시 텍스트를 구조화된 문서로 파싱
    /// </summary>
    /// <param name="rawContent">Reader가 추출한 원시 텍스트</param>
    /// <param name="options">파싱 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>구조화된 문서 내용</returns>
    Task<ParsedDocumentContent> ParseAsync(
        RawDocumentContent rawContent,
        DocumentParsingOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 문서 파싱 옵션
/// </summary>
public class DocumentParsingOptions
{
    /// <summary>
    /// LLM 파싱 모드 사용 여부 (false일 경우 기본 규칙 기반 파싱)
    /// Note: LLM 사용은 consumer application의 책임이며, 이 옵션은 파싱 수준을 제어합니다
    /// </summary>
    public bool UseLlmParsing { get; set; } = true;

    /// <summary>
    /// 문서 유형 힌트 (자동 감지하지 않고 직접 지정)
    /// </summary>
    public string? DocumentTypeHint { get; set; }

    /// <summary>
    /// 구조화 세밀도 (Low, Medium, High)
    /// </summary>
    public StructuringLevel StructuringLevel { get; set; } = StructuringLevel.Medium;

    /// <summary>
    /// 메타데이터 추출 여부
    /// </summary>
    public bool ExtractMetadata { get; set; } = true;

    /// <summary>
    /// 언어별 처리 옵션
    /// </summary>
    public string Language { get; set; } = "ko";

    /// <summary>
    /// 커스텀 파싱 설정
    /// </summary>
    public Dictionary<string, object> CustomSettings { get; set; } = new();
}

/// <summary>
/// 구조화 세밀도 레벨
/// </summary>
public enum StructuringLevel
{
    /// <summary>
    /// 기본적인 섹션 분할만
    /// </summary>
    Low,

    /// <summary>
    /// 중간 수준의 구조화 (제목, 단락, 목록)
    /// </summary>
    Medium,

    /// <summary>
    /// 고도의 구조화 (의미적 분석, 관계 추출)
    /// </summary>
    High
}