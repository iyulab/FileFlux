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
    /// 문서 섹션 계층 구조 (HeadingPath 추출용)
    /// </summary>
    public List<ContentSection> Sections { get; set; } = new();

    /// <summary>
    /// 페이지별 텍스트 범위 정보 (PDF 등)
    /// Key: 페이지 번호 (1-based), Value: (시작 문자 위치, 끝 문자 위치)
    /// </summary>
    public Dictionary<int, (int Start, int End)> PageRanges { get; set; } = new();

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

    /// <summary>
    /// 컬럼 헤더 (컨텍스트 보존용)
    /// </summary>
    public List<string> ColumnHeaders { get; set; } = new();
}

/// <summary>
/// 문서 내 섹션 정보 (HeadingPath 추출용)
/// </summary>
public class ContentSection
{
    /// <summary>
    /// 섹션 제목
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 섹션 레벨 (1 = H1, 2 = H2, etc.)
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// 텍스트 내 시작 위치
    /// </summary>
    public int StartPosition { get; set; }

    /// <summary>
    /// 텍스트 내 끝 위치
    /// </summary>
    public int EndPosition { get; set; }

    /// <summary>
    /// 하위 섹션들
    /// </summary>
    public List<ContentSection> Children { get; set; } = new();
}
