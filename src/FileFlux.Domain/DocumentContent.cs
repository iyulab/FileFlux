namespace FileFlux.Domain;

/// <summary>
/// 파싱된 문서 내용을 담는 도메인 모델
/// </summary>
public class DocumentContent
{
    /// <summary>
    /// 추출된 텍스트 내용
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 문서 메타데이터
    /// </summary>
    public DocumentMetadata Metadata { get; set; } = new();

    /// <summary>
    /// 문서 구조 정보 (제목, 섹션, 페이지 등)
    /// </summary>
    public Dictionary<string, object> StructureInfo { get; set; } = new();

    /// <summary>
    /// 추출된 이미지 정보 (있는 경우)
    /// </summary>
    public List<ImageInfo> Images { get; set; } = new();

    /// <summary>
    /// 추출된 테이블 정보 (있는 경우)
    /// </summary>
    public List<TableInfo> Tables { get; set; } = new();
}

/// <summary>
/// 이미지 정보
/// </summary>
public class ImageInfo
{
    /// <summary>
    /// 이미지 식별자
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 이미지 설명/캡션
    /// </summary>
    public string? Caption { get; set; }

    /// <summary>
    /// 이미지 위치 (페이지 번호 등)
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// 이미지 크기 정보
    /// </summary>
    public Dictionary<string, object> Properties { get; } = new();
}

/// <summary>
/// 테이블 정보
/// </summary>
public class TableInfo
{
    /// <summary>
    /// 테이블 식별자
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 테이블 제목/캡션
    /// </summary>
    public string? Caption { get; set; }

    /// <summary>
    /// 테이블 위치 (페이지 번호 등)
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// 테이블 행 수
    /// </summary>
    public int RowCount { get; set; }

    /// <summary>
    /// 테이블 열 수
    /// </summary>
    public int ColumnCount { get; set; }

    /// <summary>
    /// 테이블 데이터 (CSV 형식 또는 구조화된 데이터)
    /// </summary>
    public string Data { get; set; } = string.Empty;
}