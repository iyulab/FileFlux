namespace FileFlux.Domain;

/// <summary>
/// 문서 타입 열거형
/// </summary>
public enum DocumentType
{
    /// <summary>
    /// 기본 텍스트 문서
    /// </summary>
    Text,
    
    /// <summary>
    /// PDF 문서
    /// </summary>
    Pdf,
    
    /// <summary>
    /// Word 문서
    /// </summary>
    Word,
    
    /// <summary>
    /// Excel 문서
    /// </summary>
    Excel,
    
    /// <summary>
    /// PowerPoint 문서
    /// </summary>
    PowerPoint,
    
    /// <summary>
    /// Markdown 문서
    /// </summary>
    Markdown,
    
    /// <summary>
    /// JSON 문서
    /// </summary>
    Json,
    
    /// <summary>
    /// CSV 문서
    /// </summary>
    Csv,
    
    /// <summary>
    /// 기타 문서
    /// </summary>
    Other
}

/// <summary>
/// 섹션 타입 열거형
/// </summary>
public enum SectionType
{
    /// <summary>
    /// 헤더 섹션
    /// </summary>
    Header,
    
    /// <summary>
    /// 단락 섹션
    /// </summary>
    Paragraph,
    
    /// <summary>
    /// 리스트 섹션
    /// </summary>
    List,
    
    /// <summary>
    /// 테이블 섹션
    /// </summary>
    Table,
    
    /// <summary>
    /// 코드 블록 섹션
    /// </summary>
    Code,
    
    /// <summary>
    /// 인용문 섹션
    /// </summary>
    Quote,
    
    /// <summary>
    /// 이미지 섹션
    /// </summary>
    Image,
    
    /// <summary>
    /// 기타 섹션
    /// </summary>
    Other,
    
    // 상세한 섹션 타입들
    HEADING_L1,
    HEADING_L2,
    HEADING_L3,
    PARAGRAPH,
    CODE_BLOCK,
    LIST,
    TABLE,
    IMAGE,
    API_ENDPOINT,
    CLASS,
    METHOD,
    EXAMPLE,
    COMMENT,
    NAVIGATION,
    ARTICLE,
    ASIDE,
    FOOTER
}