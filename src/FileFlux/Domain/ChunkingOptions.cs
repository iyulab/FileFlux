namespace FileFlux.Domain;

/// <summary>
/// RAG 최적화된 청킹 설정 옵션 (최소 설정으로 최고 품질 달성)
/// </summary>
public class ChunkingOptions
{
    /// <summary>
    /// 청킹 전략 (기본: Auto - 문서 분석 후 자동 선택)
    /// </summary>
    public string Strategy { get; set; } = ChunkingStrategies.Auto;

    /// <summary>
    /// 청크 최대 크기 (기본: 1024토큰 - RAG 최적 크기)
    /// </summary>
    public int MaxChunkSize { get; set; } = 1024;

    /// <summary>
    /// 청크 간 겹침 크기 (기본: 128토큰 - 컨텍스트 보존)
    /// </summary>
    public int OverlapSize { get; set; } = 128;

    /// <summary>
    /// 문단 경계 보존 여부 (시맨틱 청킹용)
    /// </summary>
    public bool PreserveParagraphs { get; set; } = true;

    /// <summary>
    /// 문장 경계 보존 여부 (시맨틱 청킹용)
    /// </summary>
    public bool PreserveSentences { get; set; } = true;

    /// <summary>
    /// 계층적 청킹 시 최대 헤딩 레벨 (1-6)
    /// </summary>
    public int MaxHeadingLevel { get; set; } = 3;

    /// <summary>
    /// 사용자 정의 속성들 (Phase 10: 고급 설정)
    /// </summary>
    public Dictionary<string, object> CustomProperties { get; } = new();

    /// <summary>
    /// 전략별 옵션 설정 (Auto 전략에서 사용)
    /// - ForceStrategy: 특정 전략 강제 (테스트용)
    /// - ConfidenceThreshold: 최소 신뢰도 임계값 (기본 0.6)
    /// - EnableCache: 전략 선택 캐싱 활성화
    /// - MaxAnalysisTime: 최대 분석 시간 (초, 기본 300)
    /// - PreferSpeed: 속도 우선 모드
    /// - PreferQuality: 품질 우선 모드
    /// </summary>
    public Dictionary<string, object> StrategyOptions { get; } = new();
}

/// <summary>
/// 지원되는 청킹 전략 이름들
/// </summary>
public static class ChunkingStrategies
{
    /// <summary>
    /// 자동 적응형 청킹 - LLM이 문서를 분석하여 최적 전략 선택 (기본값)
    /// </summary>
    public const string Auto = nameof(Auto);

    /// <summary>
    /// 스마트 청킹 - 문장 경계 기반 완성도 70% 보장
    /// </summary>
    public const string Smart = nameof(Smart);

    /// <summary>
    /// 지능형 청킹 - 구조 인식 RAG 최적화
    /// </summary>
    public const string Intelligent = nameof(Intelligent);

    /// <summary>
    /// 의미적 청킹 - 문장/문단 경계 기반
    /// </summary>
    public const string Semantic = nameof(Semantic);

    /// <summary>
    /// 문단 기반 청킹 - 자연스러운 문단 경계
    /// </summary>
    public const string Paragraph = nameof(Paragraph);

    /// <summary>
    /// 고정 크기 청킹 - 일정한 토큰 수로 분할
    /// </summary>
    public const string FixedSize = nameof(FixedSize);

    /// <summary>
    /// 계층적 청킹 - 문서 구조(섹션/챕터) 기반
    /// </summary>
    public const string Hierarchical = nameof(Hierarchical);

    /// <summary>
    /// 페이지 단위 청킹 - PDF 등 페이지 기반 문서용
    /// </summary>
    public const string PageLevel = nameof(PageLevel);

    /// <summary>
    /// 지원되는 모든 전략 목록 (우선순위 순)
    /// </summary>
    public static readonly string[] All = { Auto, Smart, Intelligent, Semantic, Paragraph, FixedSize, Hierarchical, PageLevel };
}
