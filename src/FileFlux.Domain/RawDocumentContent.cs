namespace FileFlux.Domain;

/// <summary>
/// Reader가 추출한 순수 텍스트 내용 - LLM 처리 전 원시 데이터
/// </summary>
public class RawDocumentContent
{
    /// <summary>
    /// 추출된 순수 텍스트 내용
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 기본 파일 메타데이터 (파일명, 크기, 생성일시 등)
    /// </summary>
    public FileMetadata FileInfo { get; set; } = new();

    /// <summary>
    /// Reader에서 감지한 원시 구조 힌트 (제목 후보, 섹션 경계 등)
    /// LLM이 없어도 기본적인 구조화가 가능한 단순한 힌트만 포함
    /// </summary>
    public Dictionary<string, object> StructuralHints { get; set; } = new();

    /// <summary>
    /// 원시 추출 과정에서 발생한 경고나 이슈
    /// </summary>
    public List<string> ExtractionWarnings { get; set; } = new();
}

/// <summary>
/// 파일 기본 메타데이터
/// </summary>
public class FileMetadata
{
    /// <summary>
    /// 파일명
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 파일 확장자
    /// </summary>
    public string FileExtension { get; set; } = string.Empty;

    /// <summary>
    /// 파일 크기 (바이트)
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// 파일 생성일시
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 파일 수정일시
    /// </summary>
    public DateTime ModifiedAt { get; set; }

    /// <summary>
    /// 텍스트 추출 완료 시간
    /// </summary>
    public DateTime ExtractedAt { get; set; }

    /// <summary>
    /// 추출에 사용된 Reader 타입
    /// </summary>
    public string ReaderType { get; set; } = string.Empty;
}