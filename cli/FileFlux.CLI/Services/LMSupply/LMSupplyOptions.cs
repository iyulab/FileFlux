namespace FileFlux.CLI.Services.LMSupply;

/// <summary>
/// Configuration options for LMSupply services.
/// </summary>
public class LMSupplyOptions
{
    /// <summary>
    /// Model alias for multilingual embedding support.
    /// Provides cross-lingual semantic understanding for 50+ languages including Korean.
    /// </summary>
    public const string MultilingualEmbeddingModel = "multilingual";

    /// <summary>
    /// Model alias for English-optimized embedding.
    /// </summary>
    public const string DefaultEmbeddingModel = "default";

    /// <summary>
    /// Gets or sets whether to use GPU acceleration if available.
    /// </summary>
    public bool UseGpuAcceleration { get; set; } = true;

    /// <summary>
    /// Gets or sets the cache directory for downloaded models.
    /// If null, uses the default cache directory.
    /// </summary>
    public string? CacheDirectory { get; set; }

    /// <summary>
    /// Gets or sets the embedding model identifier.
    /// Default: "default" (BGE Small English v1.5)
    /// Use "multilingual" for Korean and other non-English languages.
    /// </summary>
    public string EmbeddingModel { get; set; } = "default";

    /// <summary>
    /// Gets or sets whether to auto-select multilingual model for detected CJK content.
    /// When true, uses "multilingual" model if Korean, Japanese, or Chinese is detected.
    /// Default: false (explicit model selection)
    /// </summary>
    public bool AutoSelectMultilingualModel { get; set; }

    /// <summary>
    /// Gets or sets the text generator model identifier.
    /// Default: "microsoft/Phi-4-mini-instruct-onnx"
    /// </summary>
    public string GeneratorModel { get; set; } = "microsoft/Phi-4-mini-instruct-onnx";

    /// <summary>
    /// Gets or sets the captioning model identifier.
    /// Default: "default" (ViT-GPT2)
    /// </summary>
    public string CaptionerModel { get; set; } = "default";

    /// <summary>
    /// Gets or sets the OCR detection model identifier.
    /// Default: "default" (DBNet v3)
    /// </summary>
    public string OcrDetectionModel { get; set; } = "default";

    /// <summary>
    /// Gets or sets the OCR recognition model identifier.
    /// If null, auto-selects based on language hint.
    /// </summary>
    public string? OcrRecognitionModel { get; set; }

    /// <summary>
    /// Gets or sets the default language hint for OCR.
    /// Default: "en" (English)
    /// </summary>
    public string OcrLanguageHint { get; set; } = "en";

    /// <summary>
    /// Gets or sets the maximum sequence length for embeddings.
    /// Default: 512
    /// </summary>
    public int MaxSequenceLength { get; set; } = 512;

    /// <summary>
    /// Gets or sets the maximum tokens for text generation.
    /// Default: 1024
    /// </summary>
    public int MaxGenerationTokens { get; set; } = 1024;

    /// <summary>
    /// Gets or sets whether to warm up models on initialization.
    /// Default: false
    /// </summary>
    public bool WarmupOnInit { get; set; }

    /// <summary>
    /// Gets the recommended embedding model based on detected language.
    /// Returns "multilingual" for CJK languages (Korean, Japanese, Chinese),
    /// otherwise returns the configured EmbeddingModel.
    /// </summary>
    /// <param name="languageCode">ISO 639-1 language code (e.g., "ko", "ja", "zh", "en")</param>
    /// <returns>The recommended embedding model alias</returns>
    public string GetEmbeddingModelForLanguage(string languageCode)
    {
        // If auto-selection is enabled and language is CJK, use multilingual
        if (AutoSelectMultilingualModel && IsCjkLanguage(languageCode))
        {
            return MultilingualEmbeddingModel;
        }

        return EmbeddingModel;
    }

    /// <summary>
    /// Gets the recommended OCR recognition model based on language.
    /// </summary>
    /// <param name="languageCode">ISO 639-1 language code</param>
    /// <returns>The recommended OCR recognition model alias</returns>
    public static string GetOcrModelForLanguage(string languageCode)
    {
        return languageCode?.ToLowerInvariant() switch
        {
            "ko" => "crnn-korean-v3",
            "ja" => "crnn-japan-v3",
            "zh" => "crnn-chinese-v3",
            _ => "default" // English and other Latin-based languages
        };
    }

    /// <summary>
    /// Checks if the language code represents a CJK (Chinese, Japanese, Korean) language.
    /// </summary>
    private static bool IsCjkLanguage(string? languageCode)
    {
        return languageCode?.ToLowerInvariant() switch
        {
            "ko" or "ja" or "zh" => true,
            _ => false
        };
    }
}
