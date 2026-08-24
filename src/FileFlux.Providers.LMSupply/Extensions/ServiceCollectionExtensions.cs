using FileFlux.Core;
using FileFlux.Providers.LMSupply.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FileFlux.Providers.LMSupply.Extensions;

/// <summary>
/// Extension methods for registering LMSupply-based AI services with dependency injection.
/// All services use local ONNX inference — no API key required.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IDocumentAnalysisService"/> backed by a local LMSupply ONNX generator model.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="modelId">LMSupply generator model ID or alias. Defaults to "microsoft/Phi-4-mini-instruct-onnx".</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLMSupplyDocumentAnalysis(
        this IServiceCollection services,
        string modelId = "microsoft/Phi-4-mini-instruct-onnx")
    {
        services.AddSingleton<IDocumentAnalysisService>(sp =>
            LMSupplyGeneratorService.CreateAsync(new LMSupplyOptions { GeneratorModel = modelId })
                .GetAwaiter().GetResult());
        return services;
    }

    /// <summary>
    /// Registers an <see cref="IEmbeddingService"/> backed by a local LMSupply ONNX embedding model.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="modelId">LMSupply catalog alias (e.g., "default", "multilingual") or model ID.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLMSupplyEmbedding(
        this IServiceCollection services,
        string modelId = "default")
    {
        services.AddSingleton<IEmbeddingService>(sp =>
            LMSupplyEmbedderService.CreateAsync(new LMSupplyOptions { EmbeddingModel = modelId })
                .GetAwaiter().GetResult());
        return services;
    }

    /// <summary>
    /// Registers an <see cref="IImageToTextService"/> backed by a local LMSupply ONNX OCR pipeline —
    /// best for scanned documents and text-bearing images. For photo/visual descriptions, see
    /// <see cref="AddLMSupplyCaptioner"/>. FileFlux's <c>IImageToTextService</c> is a single-registration
    /// interface, so pick whichever capability matches your image content (both cannot be registered
    /// at once through this method pair).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="languageHint">Default OCR recognition language hint (ISO 639-1, e.g. "en", "ko"). Defaults to "en".</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLMSupplyOcr(
        this IServiceCollection services,
        string languageHint = "en")
    {
        services.AddSingleton<IImageToTextService>(sp =>
            LMSupplyOcrService.CreateAsync(new LMSupplyOptions { OcrLanguageHint = languageHint })
                .GetAwaiter().GetResult());
        return services;
    }

    /// <summary>
    /// Registers an <see cref="IImageToTextService"/> backed by a local LMSupply ONNX image captioning
    /// model — best for photos and visual content description. For scanned documents/text-bearing
    /// images, see <see cref="AddLMSupplyOcr"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="modelId">Captioner model ID or "default".</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLMSupplyCaptioner(
        this IServiceCollection services,
        string modelId = "default")
    {
        services.AddSingleton<IImageToTextService>(sp =>
            LMSupplyCaptionerService.CreateAsync(new LMSupplyOptions { CaptionerModel = modelId })
                .GetAwaiter().GetResult());
        return services;
    }
}
