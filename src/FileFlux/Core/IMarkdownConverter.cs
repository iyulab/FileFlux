namespace FileFlux;

/// <summary>
/// Raw content를 구조화된 Markdown으로 변환하는 서비스 인터페이스
/// RAG 파이프라인에서 청킹 품질 향상을 위해 문서 구조를 보존합니다.
/// </summary>
public interface IMarkdownConverter
{
    /// <summary>
    /// RawContent를 Markdown으로 변환합니다.
    /// </summary>
    /// <param name="rawContent">변환할 원본 컨텐츠</param>
    /// <param name="options">변환 옵션 (null인 경우 기본값 사용)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>구조화된 Markdown 문자열</returns>
    Task<MarkdownConversionResult> ConvertAsync(
        FileFlux.Core.RawContent rawContent,
        MarkdownConversionOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Markdown 변환 옵션
/// </summary>
public class MarkdownConversionOptions
{
    /// <summary>
    /// 감지된 헤딩 계층 구조를 보존합니다.
    /// </summary>
    public bool PreserveHeadings { get; set; } = true;

    /// <summary>
    /// 감지된 테이블을 Markdown 테이블로 변환합니다.
    /// </summary>
    public bool ConvertTables { get; set; } = true;

    /// <summary>
    /// 글머리 기호/번호 목록을 보존합니다.
    /// </summary>
    public bool PreserveLists { get; set; } = true;

    /// <summary>
    /// 이미지 플레이스홀더를 포함합니다.
    /// 형식: ![alt](embedded:img_000)
    /// </summary>
    public bool IncludeImagePlaceholders { get; set; } = true;

    /// <summary>
    /// 휴리스틱 실패 시 LLM을 사용하여 구조를 추론합니다.
    /// IDocumentAnalysisService가 DI로 제공된 경우에만 작동합니다.
    /// </summary>
    public bool UseLLMInference { get; set; }

    /// <summary>
    /// 코드 블록을 감지하고 언어 힌트를 추가합니다.
    /// </summary>
    public bool DetectCodeBlocks { get; set; } = true;

    /// <summary>
    /// 빈 줄을 정규화하여 가독성을 향상시킵니다.
    /// </summary>
    public bool NormalizeWhitespace { get; set; } = true;

    /// <summary>
    /// 최소 헤딩 레벨 (1-6). 이보다 낮은 레벨은 이 레벨로 조정됩니다.
    /// </summary>
    public int MinHeadingLevel { get; set; } = 1;

    /// <summary>
    /// 최대 헤딩 레벨 (1-6). 이보다 높은 레벨은 이 레벨로 조정됩니다.
    /// </summary>
    public int MaxHeadingLevel { get; set; } = 6;
}

/// <summary>
/// Markdown 변환 결과
/// </summary>
public class MarkdownConversionResult
{
    /// <summary>
    /// 변환된 Markdown 텍스트
    /// </summary>
    public string Markdown { get; set; } = string.Empty;

    /// <summary>
    /// 변환 성공 여부
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 변환 방법 (Heuristic, LLM, Mixed)
    /// </summary>
    public ConversionMethod Method { get; set; }

    /// <summary>
    /// 감지된 구조 요소 통계
    /// </summary>
    public StructureStatistics Statistics { get; set; } = new();

    /// <summary>
    /// 변환 중 발생한 경고
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// 원본 텍스트 길이
    /// </summary>
    public int OriginalLength { get; set; }

    /// <summary>
    /// 변환된 Markdown 길이
    /// </summary>
    public int MarkdownLength => Markdown.Length;
}

/// <summary>
/// 변환 방법
/// </summary>
public enum ConversionMethod
{
    /// <summary>
    /// 휴리스틱 기반 변환
    /// </summary>
    Heuristic,

    /// <summary>
    /// LLM 기반 변환
    /// </summary>
    LLM,

    /// <summary>
    /// 휴리스틱 + LLM 혼합
    /// </summary>
    Mixed
}

/// <summary>
/// 구조 요소 통계
/// </summary>
public class StructureStatistics
{
    /// <summary>
    /// 감지된 헤딩 수
    /// </summary>
    public int HeadingCount { get; set; }

    /// <summary>
    /// 감지된 테이블 수
    /// </summary>
    public int TableCount { get; set; }

    /// <summary>
    /// 감지된 리스트 수
    /// </summary>
    public int ListCount { get; set; }

    /// <summary>
    /// 감지된 코드 블록 수
    /// </summary>
    public int CodeBlockCount { get; set; }

    /// <summary>
    /// 감지된 이미지 플레이스홀더 수
    /// </summary>
    public int ImagePlaceholderCount { get; set; }

    /// <summary>
    /// 헤딩 레벨별 분포
    /// </summary>
    public Dictionary<int, int> HeadingLevelDistribution { get; set; } = new();
}
