namespace FileFlux.Domain;

/// <summary>
/// 청킹 전략 설정 옵션
/// </summary>
public class ChunkingOptions
{
    /// <summary>
    /// 사용할 청킹 전략 이름
    /// </summary>
    public string Strategy { get; set; } = ChunkingStrategies.FixedSize;

    /// <summary>
    /// 청크 최대 크기 (토큰 수)
    /// </summary>
    public int MaxChunkSize { get; set; } = 512;

    /// <summary>
    /// 청크 간 겹침 크기 (토큰 수)
    /// </summary>
    public int OverlapSize { get; set; } = 64;

    /// <summary>
    /// 문서 구조 보존 여부
    /// </summary>
    public bool PreserveStructure { get; set; } = true;

    /// <summary>
    /// 메타데이터 포함 여부
    /// </summary>
    public bool IncludeMetadata { get; set; } = true;

    /// <summary>
    /// 최소 청크 크기 (토큰 수)
    /// </summary>
    public int MinChunkSize { get; set; } = 64;

    /// <summary>
    /// 중요도 기반 필터링 임계값 (0.0 ~ 1.0)
    /// </summary>
    public double ImportanceThreshold { get; set; } = 0.0;

    /// <summary>
    /// 전략별 세부 옵션
    /// </summary>
    public Dictionary<string, object> StrategyOptions { get; set; } = new();

    /// <summary>
    /// 언어별 처리 옵션
    /// </summary>
    public Dictionary<string, object> LanguageOptions { get; set; } = new();
}

/// <summary>
/// 지원되는 청킹 전략 이름들
/// </summary>
public static class ChunkingStrategies
{
    /// <summary>
    /// 지능형 청킹 - RAG 최적화된 의미 단위 분할
    /// </summary>
    public const string Intelligent = nameof(Intelligent);

    /// <summary>
    /// 의미적 청킹 - 문장/문단 경계 기반
    /// </summary>
    public const string Semantic = nameof(Semantic);

    /// <summary>
    /// 고정 크기 청킹 - 일정한 토큰 수로 분할
    /// </summary>
    public const string FixedSize = nameof(FixedSize);

    /// <summary>
    /// 문단 기반 청킹 - 자연스러운 문단 경계
    /// </summary>
    public const string Paragraph = nameof(Paragraph);

    /// <summary>
    /// 지원되는 모든 전략 목록
    /// </summary>
    public static readonly string[] All = { Intelligent, Semantic, FixedSize, Paragraph };
}