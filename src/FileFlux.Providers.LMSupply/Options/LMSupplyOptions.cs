namespace FileFlux.Providers.LMSupply;

/// <summary>
/// Configuration options for LMSupply services.
/// </summary>
public class LMSupplyOptions
{
    /// <summary>
    /// Model alias for English-optimized embedding.
    /// </summary>
    public const string DefaultEmbeddingModel = "default";

    /// <summary>
    /// Gets or sets whether to use GPU acceleration if available. When <see langword="true"/> the provider is
    /// LMSupply's <c>Auto</c> -- it picks CUDA, CoreML or CPU for ONNX sessions and Vulkan for llama-server on
    /// AMD/Intel GPUs; <see langword="false"/> pins CPU. No provider is chosen here.
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
    /// Use "multilingual" for Korean and other non-English languages. This is the one embedding model the provider
    /// loads: the per-document language switch (<c>AutoSelectMultilingualModel</c>) was never wired and was removed
    /// in 0.25.0 — one index, one model, one vector space.
    /// </summary>
    public string EmbeddingModel { get; set; } = "default";

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
    /// Gets or sets the maximum sequence length for embeddings. <see langword="null"/> (the default)
    /// lets the model decide: what it declares in <c>sentence_bert_config.json</c>, then LMSupply's
    /// catalog entry, then 512. A value is used as given. This used to default to 512, which silently
    /// truncated models declaring a longer length (nomic-embed-text: 8192) to 512.
    /// </summary>
    public int? MaxSequenceLength { get; set; }

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
}
