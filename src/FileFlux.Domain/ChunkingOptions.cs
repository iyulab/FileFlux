namespace FileFlux.Domain;

/// <summary>
/// RAG 최적화된 청킹 설정 옵션 (최소 설정으로 최고 품질 달성)
/// </summary>
public class ChunkingOptions
{
    /// <summary>
    /// 청킹 전략 (기본: Intelligent - RAG 최적화)
    /// </summary>
    public string Strategy { get; set; } = ChunkingStrategies.Intelligent;

    /// <summary>
    /// 청크 최대 크기 (기본: 1024토큰 - RAG 최적 크기)
    /// </summary>
    public int MaxChunkSize { get; set; } = 1024;

    /// <summary>
    /// 청크 간 겹침 크기 (기본: 128토큰 - 컨텍스트 보존)
    /// </summary>
    public int OverlapSize { get; set; } = 128;
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
    /// 스마트 청킹 - 문장 경계 기반 완성도 보장 (NEW)
    /// </summary>
    public const string Smart = nameof(Smart);

    /// <summary>
    /// 지원되는 모든 전략 목록
    /// </summary>
    public static readonly string[] All = { Smart, Intelligent, Semantic, FixedSize, Paragraph };
}