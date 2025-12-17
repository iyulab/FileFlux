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
using FileFlux.Infrastructure.Services.LMSupply;
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

        // === Document Refiner ===
        services.AddScoped<IDocumentRefiner>(provider =>
        {
            var markdownConverter = provider.GetService<IMarkdownConverter>();
            var loggerFactory = provider.GetService<ILoggerFactory>();
            var logger = loggerFactory?.CreateLogger<DocumentRefiner>();
            return new DocumentRefiner(markdownConverter, logger);
        });

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

        // === Document Enricher ===
        services.AddScoped<IDocumentEnricher>(provider =>
        {
            var improverServices = provider.GetService<FluxImproverServices>();
            var loggerFactory = provider.GetService<ILoggerFactory>();
            var logger = loggerFactory?.CreateLogger<DocumentEnricher>();
            return new DocumentEnricher(improverServices, logger);
        });

        // === Main Document Processor Factory ===
        // Stateful pattern: use factory to create per-document processors
        services.AddScoped<IDocumentProcessorFactory>(provider =>
        {
            var readerFactory = provider.GetRequiredService<IDocumentReaderFactory>();
            var chunkerFactory = provider.GetRequiredService<IChunkerFactory>();
            var documentRefiner = provider.GetService<IDocumentRefiner>();
            var documentEnricher = provider.GetService<IDocumentEnricher>();
            var improverServices = provider.GetService<FluxImproverServices>();
            var markdownConverter = provider.GetService<IMarkdownConverter>();
            var imageToTextService = provider.GetService<IImageToTextService>();
            var loggerFactory = provider.GetService<ILoggerFactory>();

            return new DocumentProcessorFactory(
                readerFactory,
                chunkerFactory,
                documentRefiner,
                documentEnricher,
                improverServices,
                markdownConverter,
                imageToTextService,
                loggerFactory);
        });

        // Legacy processor for backward compatibility (CLI commands)
        services.AddScoped<FluxDocumentProcessor>();

        // === Optional Services ===

        // Memory cache for metadata
        services.AddMemoryCache(options => options.SizeLimit = 100);

        // Note: IEmbeddingService is not registered by default.
        // Use AddFileFluxWithLMSupply() to enable Locally running AI features (embedding, generation, captioning, OCR).

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
    // LMSupply Integration Extensions
    // =========================================================================

    /// <summary>
    /// Adds FileFlux with LMSupply services for locally running AI features.
    /// Includes embedding, text generation, image captioning, and OCR.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    /// <remarks>
    /// This method registers LMSupply service factories. Services are created lazily
    /// on first use because they require async model loading.
    /// Use <see cref="LMSupplyServiceFactory"/> to create service instances.
    /// </remarks>
    public static IServiceCollection AddFileFluxWithLMSupply(this IServiceCollection services)
    {
        return AddFileFluxWithLMSupply(services, _ => { });
    }

    /// <summary>
    /// Adds FileFlux with LMSupply services using custom configuration.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configure">Configuration action for LMSupplyOptions</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddFileFluxWithLMSupply(
        this IServiceCollection services,
        Action<LMSupplyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        // Register options
        var options = new LMSupplyOptions();
        configure(options);
        services.AddSingleton(options);

        // Register service factory for lazy initialization
        services.AddSingleton<LMSupplyServiceFactory>();

        // Register individual service accessors
        services.AddLMSupplyEmbedder();
        services.AddLMSupplyGenerator();
        services.AddLMSupplyCaptioner();
        services.AddLMSupplyOcr();

        return AddFileFlux(services);
    }

    /// <summary>
    /// Adds LMSupply embedding service.
    /// </summary>
    public static IServiceCollection AddLMSupplyEmbedder(this IServiceCollection services)
    {
        services.TryAddSingleton<IEmbeddingService>(provider =>
        {
            var factory = provider.GetRequiredService<LMSupplyServiceFactory>();
            return factory.GetEmbedderAsync().GetAwaiter().GetResult();
        });
        return services;
    }

    /// <summary>
    /// Adds LMSupply text generation service.
    /// </summary>
    public static IServiceCollection AddLMSupplyGenerator(this IServiceCollection services)
    {
        services.TryAddSingleton<ITextCompletionService>(provider =>
        {
            var factory = provider.GetRequiredService<LMSupplyServiceFactory>();
            return factory.GetGeneratorAsync().GetAwaiter().GetResult();
        });
        return services;
    }

    /// <summary>
    /// Adds LMSupply image captioning service.
    /// </summary>
    public static IServiceCollection AddLMSupplyCaptioner(this IServiceCollection services)
    {
        services.TryAddSingleton<IImageToTextService>(provider =>
        {
            var factory = provider.GetRequiredService<LMSupplyServiceFactory>();
            return factory.GetCaptionerAsync().GetAwaiter().GetResult();
        });
        return services;
    }

    /// <summary>
    /// Adds LMSupply OCR service for text extraction from images.
    /// </summary>
    public static IServiceCollection AddLMSupplyOcr(this IServiceCollection services)
    {
        // OCR is registered as a named service since IImageToTextService is also used by Captioner
        services.TryAddKeyedSingleton<IImageToTextService>("ocr", (provider, _) =>
        {
            var factory = provider.GetRequiredService<LMSupplyServiceFactory>();
            return factory.GetOcrAsync().GetAwaiter().GetResult();
        });
        return services;
    }
}
