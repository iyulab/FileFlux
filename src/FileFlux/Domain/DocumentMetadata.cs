namespace FileFlux.Domain;

/// <summary>
/// 문서의 메타데이터 정보를 담는 도메인 모델
/// </summary>
public class DocumentMetadata
{
    /// <summary>
    /// 파일명
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 파일 형식 (PDF, DOCX, etc.)
    /// </summary>
    public string FileType { get; set; } = string.Empty;

    /// <summary>
    /// 파일 크기 (bytes)
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// 문서 제목
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// 문서 작성자
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// 문서 생성 일시
    /// </summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// 문서 수정 일시
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// 문서 처리 일시
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 문서 언어
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// 총 페이지 수
    /// </summary>
    public int PageCount { get; set; }

    /// <summary>
    /// 사용자 정의 속성들
    /// </summary>
    public Dictionary<string, object> CustomProperties { get; } = new();
}
