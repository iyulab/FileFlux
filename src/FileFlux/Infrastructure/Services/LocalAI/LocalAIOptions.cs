namespace FileFlux.Infrastructure.Services.LocalAI;

/// <summary>
/// Configuration options for LocalAI services.
/// </summary>
public class LocalAIOptions
{
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
    public bool WarmupOnInit { get; set; } = false;
}
