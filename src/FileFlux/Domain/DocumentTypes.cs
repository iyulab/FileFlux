namespace FileFlux.Domain;

// DocumentType is now in FileFlux.Core

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
    HeadingL1,
    HeadingL2,
    HeadingL3,
    CodeBlock,
    ApiEndpoint,
    Class,
    Method,
    Example,
    Comment,
    Navigation,
    Article,
    Aside,
    Footer
}
