namespace FileFlux;

/// <summary>
/// 텍스트 완성 서비스 추상화 인터페이스
/// FileFlux에서 필수 의존성으로 요구됨
/// 
/// 역할:
/// - 텍스트 생성(<see cref="GenerateAsync(string, GenerationSettings, CancellationToken)"/>)을 위한 LLM 호출 (LLM refine, 메타데이터 enrichment 등)
/// - 소비 애플리케이션에서 구현하여 DI를 통해 주입
/// - 모든 LLM 기반 기능은 이 서비스에 의존
/// </summary>
public interface IDocumentAnalysisService
{
    /// <summary>
    /// 텍스트 완성 서비스 제공업체 정보 (소비 애플리케이션에서 구현)
    /// </summary>
    DocumentAnalysisServiceInfo ProviderInfo { get; }

    /// <summary>
    /// 텍스트 완성 서비스 가용성 확인 (소비 애플리케이션에서 구현)
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>서비스 가용성</returns>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 범용 텍스트 완성 호출 (BasicDocumentParser 호환성을 위한 간단한 인터페이스)
    /// </summary>
    /// <param name="prompt">프롬프트</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>LLM 응답 텍스트</returns>
    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// <see cref="GenerateAsync(string, CancellationToken)"/> with the sampling settings the caller's options
    /// declare (<c>LlmRefineOptions.Temperature</c>/<c>MaxTokens</c>, <c>ParsingOptions.Temperature</c>/<c>MaxTokens</c>).
    /// A null member means the implementation's own default. The default implementation ignores the settings and
    /// calls the two-argument overload: an implementation outside this library must override it for those options
    /// to reach the model; the two implementations this library ships do. Before 0.25.0 every call used the
    /// implementation's literals (0.7 / 1000 tokens) and the four options were read by nothing.
    /// </summary>
    /// <exception cref="GenerationTruncatedException">
    /// The model stopped at the output token limit. An implementation that can observe the completion reason throws it
    /// rather than returning the cut-off text.
    /// </exception>
    Task<string> GenerateAsync(string prompt, GenerationSettings settings, CancellationToken cancellationToken = default)
        => GenerateAsync(prompt, cancellationToken);
}

/// <summary>
/// Sampling settings for one <see cref="IDocumentAnalysisService.GenerateAsync(string, GenerationSettings, CancellationToken)"/>
/// call. A null member leaves that setting at the implementation's default.
/// </summary>
/// <param name="Temperature">Sampling temperature (0.0 to 1.0), or null for the implementation's default.</param>
/// <param name="MaxTokens">Maximum tokens to generate, or null for the implementation's default.</param>
public sealed record GenerationSettings(double? Temperature = null, int? MaxTokens = null)
{
    /// <summary>No preference: the implementation's defaults.</summary>
    public static readonly GenerationSettings Default = new();
}

/// <summary>
/// Thrown by <see cref="IDocumentAnalysisService.GenerateAsync(string, GenerationSettings, CancellationToken)"/> when the
/// model stopped because it reached the output token limit, so the text it produced is cut off. A cut-off rewrite is
/// shorter than its input and would otherwise be indistinguishable from an edit that removed content.
/// </summary>
/// <remarks>
/// Only an implementation that can observe the completion reason can throw it. Both services this library ships do:
/// the OpenAI-compatible one (<c>finish_reason = "length"</c>) and the LMSupply one (the generator's finish reason). It derives from <see cref="Flux.Abstractions.TextCompletionTruncatedException"/>, which services
/// on the shared completion port throw for the same condition — catch the base type to cover both.
/// </remarks>
public sealed class GenerationTruncatedException : Flux.Abstractions.TextCompletionTruncatedException
{
    /// <summary>Creates the exception with the default message.</summary>
    public GenerationTruncatedException() { }

    /// <summary>Creates the exception with a message.</summary>
    public GenerationTruncatedException(string? message) : base(message) { }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    public GenerationTruncatedException(string? message, Exception? innerException) : base(message, innerException) { }

    /// <summary>Creates the exception for a call that asked for <paramref name="maxTokens"/> output tokens.</summary>
    public GenerationTruncatedException(int maxTokens) : base(maxTokens) { }

    /// <summary>Creates the exception for a call that asked for <paramref name="maxTokens"/> output tokens, wrapping the error that reported it.</summary>
    public GenerationTruncatedException(int maxTokens, Exception? innerException) : base(maxTokens, innerException) { }
}

/// <summary>
/// 품질 개선 제안
/// </summary>
public class QualityRecommendation
{
    /// <summary>
    /// 제안 타입
    /// </summary>
    public RecommendationType Type { get; set; }

    /// <summary>
    /// 제안 설명
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 제안된 값 (해당하는 경우)
    /// </summary>
    public string? SuggestedValue { get; set; }

    /// <summary>
    /// 우선순위 (1-10)
    /// </summary>
    public int Priority { get; set; } = 5;

    /// <summary>
    /// Expected improvement in quality score if implemented (0.0-1.0)
    /// </summary>
    public double ExpectedImprovement { get; set; }

    /// <summary>
    /// Specific parameters or settings to adjust
    /// </summary>
    public Dictionary<string, object> SuggestedParameters { get; set; } = new();
}

/// <summary>
/// 제안 타입
/// </summary>
public enum RecommendationType
{
    /// <summary>
    /// 청크 크기 최적화
    /// </summary>
    ChunkSizeOptimization,

    /// <summary>
    /// 제목 개선
    /// </summary>
    TitleImprovement,

    /// <summary>
    /// 설명 보완
    /// </summary>
    DescriptionEnhancement,

    /// <summary>
    /// 컨텍스트 정보 추가
    /// </summary>
    ContextAddition,

    /// <summary>
    /// 구조화 개선
    /// </summary>
    StructureImprovement,

    /// <summary>
    /// 청킹 전략 변경
    /// </summary>
    ChunkingStrategy,

    /// <summary>
    /// 청크 크기 조정
    /// </summary>
    ChunkSize,

    /// <summary>
    /// 오버랩 설정 조정
    /// </summary>
    OverlapConfiguration,

    /// <summary>
    /// 구조 보존 개선
    /// </summary>
    StructurePreservation,

    /// <summary>
    /// 콘텐츠 필터링 적용
    /// </summary>
    ContentFiltering,

    /// <summary>
    /// 경계 감지 개선
    /// </summary>
    BoundaryDetection,

    /// <summary>
    /// 메타데이터 강화
    /// </summary>
    MetadataEnhancement
}

/// <summary>
/// Priority levels for recommendations
/// </summary>
public enum RecommendationPriority
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// 텍스트 완성 서비스 제공업체 정보
/// </summary>
public class DocumentAnalysisServiceInfo
{
    /// <summary>
    /// 제공업체명
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 제공업체 타입
    /// </summary>
    public DocumentAnalysisProviderType Type { get; set; }

    /// <summary>
    /// 지원하는 모델 목록
    /// </summary>
    public string[] SupportedModels { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 최대 컨텍스트 길이
    /// </summary>
    public int MaxContextLength { get; set; }

    /// <summary>
    /// 토큰당 비용 (입력)
    /// </summary>
    public decimal InputTokenCost { get; set; }

    /// <summary>
    /// 토큰당 비용 (출력)
    /// </summary>
    public decimal OutputTokenCost { get; set; }

    /// <summary>
    /// API 버전
    /// </summary>
    public string ApiVersion { get; set; } = string.Empty;
}
