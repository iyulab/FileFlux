namespace FileFlux;

/// <summary>
/// 텍스트 완성 서비스 제공업체 타입
/// </summary>
public enum TextCompletionProviderType
{
    /// <summary>
    /// OpenAI
    /// </summary>
    OpenAI,

    /// <summary>
    /// Anthropic Claude
    /// </summary>
    Anthropic,

    /// <summary>
    /// Google Gemini
    /// </summary>
    Google,

    /// <summary>
    /// Azure OpenAI
    /// </summary>
    AzureOpenAI,

    /// <summary>
    /// 로컬 모델
    /// </summary>
    Local,

    /// <summary>
    /// 기타 사용자 정의
    /// </summary>
    Custom
}
