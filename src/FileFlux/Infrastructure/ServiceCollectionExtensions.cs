using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using FileFlux.Core;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Adapters;
using FileFlux.Infrastructure.Factories;
using FileFlux.Infrastructure.Languages;
using FileFlux.Core.Infrastructure.Readers;
using FileFlux.Infrastructure.Readers;
using FileFlux.Infrastructure.Parsers;
using FileFlux.Infrastructure.Services;
using FileFlux.Infrastructure.Services.LocalAI;
using FluxCurator;
using FluxCurator.Core.Core;
using FluxImprover;
using FluxImprover.Services;

namespace FileFlux;

/// <summary>
/// FileFlux service registration extensions.
/// Integrates FileFlux.Core (extraction), FluxCurator (chunking), and FluxImprover (enhancement).
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds FileFlux services with FluxCurator integration for chunking.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddFileFlux(this IServiceCollection services)
    {
        // === FileFlux.Core: Document Readers ===
        services.AddTransient<IDocumentReader, TextDocumentReader>();
        services.AddTransient<IDocumentReader, MarkdownDocumentReader>();
        services.AddTransient<IDocumentReader, HtmlDocumentReader>();
        services.AddTransient<IDocumentReader, MultiModalPdfDocumentReader>();
        services.AddTransient<IDocumentReader, MultiModalPowerPointDocumentReader>();
        services.AddTransient<IDocumentReader, MultiModalWordDocumentReader>();
        services.AddTransient<IDocumentReader, MultiModalExcelDocumentReader>();

        // Language profile for multilingual support
        services.AddSingleton<ILanguageProfileProvider, DefaultLanguageProfileProvider>();

        // Reader factory
        services.AddScoped<IDocumentReaderFactory>(provider =>
            new DocumentReaderFactory(provider.GetServices<IDocumentReader>()));

        // Parser factory
        services.AddSingleton<IDocumentParserFactory>(provider =>
            new DocumentParserFactory(provider.GetService<ITextCompletionService>()));

        // Basic parser
        services.AddTransient<IDocumentParser>(provider =>
            new BasicDocumentParser(provider.GetService<ITextCompletionService>()));

        // === FluxCurator: Chunking ===
        services.AddFluxCurator();

        // === FluxImprover: Enhancement (optional) ===
        // FluxImproverServices is registered if ITextCompletionService is available
        services.AddScoped<FluxImproverServices?>(provider =>
        {
            var completionService = provider.GetService<ITextCompletionService>();
            if (completionService == null)
                return null;

            // Adapt FileFlux's ITextCompletionService to FluxImprover's interface
            var adapter = new FluxImproverTextCompletionAdapter(completionService);
            return new FluxImproverBuilder()
                .WithCompletionService(adapter)
                .Build();
        });

        // === Main Document Processor ===
        services.AddScoped<IDocumentProcessor, FluxDocumentProcessor>();

        // === Optional Services ===

        // Memory cache for metadata
        services.AddMemoryCache(options => options.SizeLimit = 100);

        // Note: IEmbeddingService is not registered by default.
        // Use AddFileFluxWithLocalAI() to enable local AI features (embedding, generation, captioning, OCR).

        return services;
    }

    /// <summary>
    /// Adds FileFlux with a specific text completion service for AI features.
    /// </summary>
    public static IServiceCollection AddFileFlux(
        this IServiceCollection services,
        ITextCompletionService textCompletionService)
    {
        ArgumentNullException.ThrowIfNull(textCompletionService);
        services.AddSingleton(textCompletionService);
        return AddFileFlux(services);
    }

    /// <summary>
    /// Adds FileFlux with text completion and image-to-text services.
    /// </summary>
    public static IServiceCollection AddFileFlux(
        this IServiceCollection services,
        ITextCompletionService textCompletionService,
        IImageToTextService? imageToTextService)
    {
        ArgumentNullException.ThrowIfNull(textCompletionService);
        services.AddSingleton(textCompletionService);

        if (imageToTextService != null)
            services.AddSingleton(imageToTextService);

        return AddFileFlux(services);
    }

    /// <summary>
    /// Adds a custom document reader.
    /// </summary>
    public static IServiceCollection AddDocumentReader<T>(this IServiceCollection services)
        where T : class, IDocumentReader
    {
        services.AddTransient<IDocumentReader, T>();
        return services;
    }

    /// <summary>
    /// Adds a custom document parser.
    /// </summary>
    public static IServiceCollection AddDocumentParser<T>(this IServiceCollection services)
        where T : class, IDocumentParser
    {
        services.AddTransient<IDocumentParser, T>();
        return services;
    }

#if DEBUG
    /// <summary>
    /// Adds FileFlux with mock services for testing (DEBUG only).
    /// </summary>
    public static IServiceCollection AddFileFluxWithMocks(
        this IServiceCollection services,
        bool useMockServices = true)
    {
        if (useMockServices)
        {
            services.AddSingleton<IImageToTextService, MockImageToTextService>();
            services.AddSingleton<IEmbeddingService, MockEmbeddingService>();
        }
        return AddFileFlux(services);
    }
#endif

    // =========================================================================
    // LocalAI Integration Extensions
    // =========================================================================

    /// <summary>
    /// Adds FileFlux with LocalAI services for local AI processing.
    /// Includes embedding, text generation, image captioning, and OCR.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    /// <remarks>
    /// This method registers LocalAI service factories. Services are created lazily
    /// on first use because they require async model loading.
    /// Use <see cref="LocalAIServiceFactory"/> to create service instances.
    /// </remarks>
    public static IServiceCollection AddFileFluxWithLocalAI(this IServiceCollection services)
    {
        return AddFileFluxWithLocalAI(services, _ => { });
    }

    /// <summary>
    /// Adds FileFlux with LocalAI services using custom configuration.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configure">Configuration action for LocalAIOptions</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddFileFluxWithLocalAI(
        this IServiceCollection services,
        Action<LocalAIOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        // Register options
        var options = new LocalAIOptions();
        configure(options);
        services.AddSingleton(options);

        // Register service factory for lazy initialization
        services.AddSingleton<LocalAIServiceFactory>();

        // Register individual service accessors
        services.AddLocalAIEmbedder();
        services.AddLocalAIGenerator();
        services.AddLocalAICaptioner();
        services.AddLocalAIOcr();

        return AddFileFlux(services);
    }

    /// <summary>
    /// Adds LocalAI embedding service.
    /// </summary>
    public static IServiceCollection AddLocalAIEmbedder(this IServiceCollection services)
    {
        services.TryAddSingleton<IEmbeddingService>(provider =>
        {
            var factory = provider.GetRequiredService<LocalAIServiceFactory>();
            return factory.GetEmbedderAsync().GetAwaiter().GetResult();
        });
        return services;
    }

    /// <summary>
    /// Adds LocalAI text generation service.
    /// </summary>
    public static IServiceCollection AddLocalAIGenerator(this IServiceCollection services)
    {
        services.TryAddSingleton<ITextCompletionService>(provider =>
        {
            var factory = provider.GetRequiredService<LocalAIServiceFactory>();
            return factory.GetGeneratorAsync().GetAwaiter().GetResult();
        });
        return services;
    }

    /// <summary>
    /// Adds LocalAI image captioning service.
    /// </summary>
    public static IServiceCollection AddLocalAICaptioner(this IServiceCollection services)
    {
        services.TryAddSingleton<IImageToTextService>(provider =>
        {
            var factory = provider.GetRequiredService<LocalAIServiceFactory>();
            return factory.GetCaptionerAsync().GetAwaiter().GetResult();
        });
        return services;
    }

    /// <summary>
    /// Adds LocalAI OCR service for text extraction from images.
    /// </summary>
    public static IServiceCollection AddLocalAIOcr(this IServiceCollection services)
    {
        // OCR is registered as a named service since IImageToTextService is also used by Captioner
        services.TryAddKeyedSingleton<IImageToTextService>("ocr", (provider, _) =>
        {
            var factory = provider.GetRequiredService<LocalAIServiceFactory>();
            return factory.GetOcrAsync().GetAwaiter().GetResult();
        });
        return services;
    }
}
