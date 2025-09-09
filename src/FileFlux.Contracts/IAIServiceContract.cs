namespace FileFlux;

/// <summary>
/// AI 서비스 계약 인터페이스 - 소비 어플리케이션이 구현해야 할 AI 서비스
/// </summary>
public interface ITextCompletionService
{
    /// <summary>
    /// GPT-5-nano 모델 사용 여부
    /// </summary>
    bool UseGpt5Nano { get; set; }

    /// <summary>
    /// 텍스트 완성 요청
    /// </summary>
    /// <param name="request">완성 요청</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>완성 응답</returns>
    Task<TextCompletionResponse> CompleteAsync(
        TextCompletionRequest request, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 배치 텍스트 완성 요청 (성능 최적화)
    /// </summary>
    /// <param name="requests">완성 요청 목록</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>완성 응답 목록</returns>
    Task<List<TextCompletionResponse>> CompleteBatchAsync(
        List<TextCompletionRequest> requests, 
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 임베딩 서비스 계약 인터페이스
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// 텍스트 임베딩 생성
    /// </summary>
    /// <param name="text">임베딩할 텍스트</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>임베딩 벡터</returns>
    Task<float[]> GenerateEmbeddingAsync(
        string text, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 배치 임베딩 생성
    /// </summary>
    /// <param name="texts">임베딩할 텍스트 목록</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>임베딩 벡터 목록</returns>
    Task<List<float[]>> GenerateEmbeddingsAsync(
        List<string> texts, 
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 텍스트 완성 요청
/// </summary>
public class TextCompletionRequest
{
    /// <summary>
    /// 요청 ID (추적용)
    /// </summary>
    public string RequestId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 시스템 프롬프트
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// 사용자 프롬프트
    /// </summary>
    public string UserPrompt { get; set; } = string.Empty;

    /// <summary>
    /// 최대 토큰 수
    /// </summary>
    public int MaxTokens { get; set; } = 1000;

    /// <summary>
    /// 온도 (창의성 조절, 0.0 ~ 1.0)
    /// </summary>
    public double Temperature { get; set; } = 0.1;

    /// <summary>
    /// GPT-5-nano 모델 사용 여부
    /// </summary>
    public bool UseGpt5Nano { get; set; } = true;

    /// <summary>
    /// 타임아웃 (밀리초)
    /// </summary>
    public int TimeoutMs { get; set; } = 30_000;

    /// <summary>
    /// 추가 매개변수
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// 텍스트 완성 응답
/// </summary>
public class TextCompletionResponse
{
    /// <summary>
    /// 요청 ID
    /// </summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// 성공 여부
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 완성된 텍스트
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 사용된 토큰 수
    /// </summary>
    public int UsedTokens { get; set; }

    /// <summary>
    /// 처리 시간 (밀리초)
    /// </summary>
    public long ProcessingTimeMs { get; set; }

    /// <summary>
    /// 사용된 모델
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 오류 메시지 (실패 시)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 오류 코드
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// 추가 메타데이터
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// AI 서비스 상태 정보
/// </summary>
public class AIServiceStatus
{
    /// <summary>
    /// 서비스 사용 가능 여부
    /// </summary>
    public bool IsAvailable { get; set; }

    /// <summary>
    /// GPT-5-nano 모델 사용 가능 여부
    /// </summary>
    public bool IsGpt5NanoAvailable { get; set; }

    /// <summary>
    /// 현재 사용 중인 모델
    /// </summary>
    public string CurrentModel { get; set; } = string.Empty;

    /// <summary>
    /// API 요청 한도 정보
    /// </summary>
    public RateLimitInfo? RateLimit { get; set; }

    /// <summary>
    /// 서비스 지연 시간 (밀리초)
    /// </summary>
    public double LatencyMs { get; set; }

    /// <summary>
    /// 마지막 상태 확인 시간
    /// </summary>
    public DateTime LastChecked { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// API 요청 한도 정보
/// </summary>
public class RateLimitInfo
{
    /// <summary>
    /// 분당 요청 한도
    /// </summary>
    public int RequestsPerMinute { get; set; }

    /// <summary>
    /// 남은 요청 수
    /// </summary>
    public int RemainingRequests { get; set; }

    /// <summary>
    /// 한도 초기화 시간
    /// </summary>
    public DateTime ResetTime { get; set; }
}