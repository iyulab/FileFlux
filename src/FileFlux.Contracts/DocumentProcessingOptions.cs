namespace FileFlux;

/// <summary>
/// 문서 처리 옵션 - 소비 어플리케이션용 입력 모델
/// </summary>
public class DocumentProcessingOptions
{
    /// <summary>
    /// 청킹 전략 (기본: Smart - RAG 최적화)
    /// </summary>
    public string Strategy { get; set; } = "Smart";

    /// <summary>
    /// 청크 최대 크기 (기본: 1024토큰 - RAG 최적 크기)
    /// </summary>
    public int MaxChunkSize { get; set; } = 1024;

    /// <summary>
    /// 청크 간 겹침 크기 (기본: 128토큰 - 컨텍스트 보존)
    /// </summary>
    public int OverlapSize { get; set; } = 128;

    /// <summary>
    /// 문서 구조 보존 여부 (기본: true)
    /// </summary>
    public bool PreserveStructure { get; set; } = true;

    /// <summary>
    /// 메타데이터 포함 여부 (기본: true)
    /// </summary>
    public bool IncludeMetadata { get; set; } = true;

    /// <summary>
    /// 품질 점수 계산 여부 (기본: true)
    /// </summary>
    public bool CalculateQualityScores { get; set; } = true;

    /// <summary>
    /// LLM 파싱 사용 여부 (기본: false - 성능 우선)
    /// </summary>
    public bool UseLlmParsing { get; set; } = false;

    /// <summary>
    /// GPT-5-nano 모델 사용 여부 (기본: true - 최신 모델 우선)
    /// </summary>
    public bool UseGpt5Nano { get; set; } = true;

    /// <summary>
    /// 전략별 사용자 정의 옵션
    /// </summary>
    public Dictionary<string, object> StrategyOptions { get; set; } = new();

    /// <summary>
    /// 처리 타임아웃 (밀리초, 기본: 30초)
    /// </summary>
    public int TimeoutMs { get; set; } = 30_000;
}

/// <summary>
/// 지원되는 청킹 전략 이름들
/// </summary>
public static class ChunkingStrategies
{
    /// <summary>
    /// 스마트 청킹 - 문장 경계 기반 완성도 보장 (70% 이상)
    /// </summary>
    public const string Smart = nameof(Smart);

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
    public static readonly string[] All = { Smart, Intelligent, Semantic, FixedSize, Paragraph };
}