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
using FileFlux.Infrastructure.Conversion;
using FileFlux.Infrastructure.Services;
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
    /// Uses Scoped lifetime by default (suitable for web applications with per-request scope).
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddFileFlux(this IServiceCollection services)
        => AddFileFlux(services, ServiceLifetime.Scoped);

    /// <summary>
    /// Adds FileFlux services with specified service lifetime.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="lifetime">
    /// Service lifetime for FileFlux services.
    /// Use <see cref="ServiceLifetime.Scoped"/> (default) for web applications.
    /// Use <see cref="ServiceLifetime.Singleton"/> when consumed by Singleton services
    /// (e.g., background services, hosted services) that resolve from root provider.
    /// </param>
    /// <returns>Service collection for chaining</returns>
    /// <remarks>
    /// Document readers are always registered as Transient (stateless).
    /// Converters and normalizers are always registered as Singleton (thread-safe, stateless).
    /// Only factory and processor services respect the lifetime parameter.
    /// </remarks>
    public static IServiceCollection AddFileFlux(
        this IServiceCollection services,
        ServiceLifetime lifetime)
    {
        // === FileFlux.Core: Document Readers (always Transient - stateless) ===
        services.AddTransient<IDocumentReader, TextDocumentReader>();
        services.AddTransient<IDocumentReader, MarkdownDocumentReader>();
        services.AddTransient<IDocumentReader, HtmlDocumentReader>();
        services.AddTransient<IDocumentReader, MultiModalPdfDocumentReader>();
        services.AddTransient<IDocumentReader, MultiModalPowerPointDocumentReader>();
        services.AddTransient<IDocumentReader, MultiModalWordDocumentReader>();
        services.AddTransient<IDocumentReader, MultiModalExcelDocumentReader>();
        services.AddTransient<IDocumentReader, HwpDocumentReader>();

        // Language profile for multilingual support (always Singleton - thread-safe)
        services.AddSingleton<ILanguageProfileProvider, DefaultLanguageProfileProvider>();

        // Reader factory (configurable lifetime)
        services.Add(new ServiceDescriptor(
            typeof(IDocumentReaderFactory),
            provider => new DocumentReaderFactory(provider.GetServices<IDocumentReader>()),
            lifetime));

        // Parser factory (always Singleton - thread-safe, stateless)
        services.AddSingleton<IDocumentParserFactory>(provider =>
            new DocumentParserFactory(provider.GetService<ITextCompletionService>()));

        // Basic parser (always Transient - may hold state)
        services.AddTransient<IDocumentParser>(provider =>
            new BasicDocumentParser(provider.GetService<ITextCompletionService>()));

        // === Markdown Converter (always Singleton - thread-safe) ===
        services.AddSingleton<IMarkdownConverter>(provider =>
            new MarkdownConverter(provider.GetService<ITextCompletionService>()));

        // === Markdown Normalizer (always Singleton - thread-safe) ===
        services.AddSingleton<IMarkdownNormalizer, MarkdownNormalizer>();

        // === Document Refiner (configurable lifetime) ===
        services.Add(new ServiceDescriptor(
            typeof(IDocumentRefiner),
            provider =>
            {
                var markdownConverter = provider.GetService<IMarkdownConverter>();
                var markdownNormalizer = provider.GetService<IMarkdownNormalizer>();
                var loggerFactory = provider.GetService<ILoggerFactory>();
                var logger = loggerFactory?.CreateLogger<DocumentRefiner>();
                return new DocumentRefiner(markdownConverter, markdownNormalizer, logger);
            },
            lifetime));

        // === LLM Refiner (configurable lifetime) ===
        services.Add(new ServiceDescriptor(
            typeof(ILlmRefiner),
            provider =>
            {
                var textCompletionService = provider.GetService<ITextCompletionService>();
                var loggerFactory = provider.GetService<ILoggerFactory>();
                var logger = loggerFactory?.CreateLogger<LlmRefiner>();
                return new LlmRefiner(textCompletionService, logger);
            },
            lifetime));

        // === FluxCurator: Chunking ===
        services.AddFluxCurator();

        // === FluxImprover: Enhancement (optional, configurable lifetime) ===
        // FluxImproverServices is registered if ITextCompletionService is available
        services.Add(new ServiceDescriptor(
            typeof(FluxImproverServices),
            provider =>
            {
                var completionService = provider.GetService<ITextCompletionService>();
                if (completionService == null)
                    return null!;

                // Adapt FileFlux's ITextCompletionService to FluxImprover's interface
                var adapter = new FluxImproverTextCompletionAdapter(completionService);
                return new FluxImproverBuilder()
                    .WithCompletionService(adapter)
                    .Build();
            },
            lifetime));

        // === Document Enricher (configurable lifetime) ===
        services.Add(new ServiceDescriptor(
            typeof(IDocumentEnricher),
            provider =>
            {
                var improverServices = provider.GetService<FluxImproverServices>();
                var loggerFactory = provider.GetService<ILoggerFactory>();
                var logger = loggerFactory?.CreateLogger<DocumentEnricher>();
                return new DocumentEnricher(improverServices, logger);
            },
            lifetime));

        // === Main Document Processor Factory (configurable lifetime) ===
        // Stateful pattern: use factory to create per-document processors
        services.Add(new ServiceDescriptor(
            typeof(IDocumentProcessorFactory),
            provider =>
            {
                var readerFactory = provider.GetRequiredService<IDocumentReaderFactory>();
                var chunkerFactory = provider.GetRequiredService<IChunkerFactory>();
                var documentRefiner = provider.GetService<IDocumentRefiner>();
                var llmRefiner = provider.GetService<ILlmRefiner>();
                var documentEnricher = provider.GetService<IDocumentEnricher>();
                var improverServices = provider.GetService<FluxImproverServices>();
                var markdownConverter = provider.GetService<IMarkdownConverter>();
                var imageToTextService = provider.GetService<IImageToTextService>();
                var loggerFactory = provider.GetService<ILoggerFactory>();

                return new DocumentProcessorFactory(
                    readerFactory,
                    chunkerFactory,
                    documentRefiner,
                    llmRefiner,
                    documentEnricher,
                    improverServices,
                    markdownConverter,
                    imageToTextService,
                    loggerFactory);
            },
            lifetime));

        // Legacy processor for backward compatibility (CLI commands, configurable lifetime)
        services.Add(new ServiceDescriptor(typeof(FluxDocumentProcessor), typeof(FluxDocumentProcessor), lifetime));

        // === Optional Services ===

        // Memory cache for metadata
        services.AddMemoryCache(options => options.SizeLimit = 100);

        // Note: IEmbeddingService and ITextCompletionService are not registered by default.
        // Consumer applications should inject their own implementations via DI.

        return services;
    }

    /// <summary>
    /// Adds FileFlux with a specific text completion service for AI features.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="textCompletionService">Text completion service for AI-powered features</param>
    /// <param name="lifetime">Service lifetime (default: Scoped)</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddFileFlux(
        this IServiceCollection services,
        ITextCompletionService textCompletionService,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(textCompletionService);
        services.AddSingleton(textCompletionService);
        return AddFileFlux(services, lifetime);
    }

    /// <summary>
    /// Adds FileFlux with text completion and image-to-text services.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="textCompletionService">Text completion service for AI-powered features</param>
    /// <param name="imageToTextService">Image-to-text service for vision features (optional)</param>
    /// <param name="lifetime">Service lifetime (default: Scoped)</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddFileFlux(
        this IServiceCollection services,
        ITextCompletionService textCompletionService,
        IImageToTextService? imageToTextService,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(textCompletionService);
        services.AddSingleton(textCompletionService);

        if (imageToTextService != null)
            services.AddSingleton(imageToTextService);

        return AddFileFlux(services, lifetime);
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
    /// Adds OfficeNativeDocumentReader for high-performance DOCX, XLSX, PPTX processing.
    /// Uses undoc native library (Rust-based) which is downloaded on-demand from GitHub releases.
    /// </summary>
    /// <remarks>
    /// The native reader provides:
    /// - Faster processing compared to managed libraries
    /// - Better CJK text handling
    /// - Parallel section processing
    /// - Self-update capability
    ///
    /// Call this BEFORE AddFileFlux() to use native readers as primary:
    /// <code>
    /// services.AddNativeOfficeReader();
    /// services.AddFileFlux();
    /// </code>
    /// </remarks>
    public static IServiceCollection AddNativeOfficeReader(this IServiceCollection services)
    {
        // Register native reader (will be selected by DocumentReaderFactory based on extension)
        services.AddTransient<IDocumentReader, OfficeNativeDocumentReader>();
        return services;
    }

    /// <summary>
    /// Adds FileFlux with native Office reader as the primary handler for DOCX, XLSX, PPTX.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="lifetime">Service lifetime (default: Scoped)</param>
    /// <returns>Service collection for chaining</returns>
    /// <remarks>
    /// This is equivalent to:
    /// <code>
    /// services.AddNativeOfficeReader();
    /// services.AddFileFlux(lifetime);
    /// </code>
    /// </remarks>
    public static IServiceCollection AddFileFluxWithNativeOffice(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        services.AddNativeOfficeReader();
        return AddFileFlux(services, lifetime);
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
    /// <param name="services">Service collection</param>
    /// <param name="useMockServices">Whether to register mock AI services</param>
    /// <param name="lifetime">Service lifetime (default: Scoped)</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddFileFluxWithMocks(
        this IServiceCollection services,
        bool useMockServices = true,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        if (useMockServices)
        {
            services.AddSingleton<IImageToTextService, MockImageToTextService>();
            services.AddSingleton<IEmbeddingService, MockEmbeddingService>();
        }
        return AddFileFlux(services, lifetime);
    }
#endif
}
