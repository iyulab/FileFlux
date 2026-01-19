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
        services.AddTransient<IDocumentReader, HwpDocumentReader>();

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

        // === Markdown Converter ===
        services.AddSingleton<IMarkdownConverter>(provider =>
            new MarkdownConverter(provider.GetService<ITextCompletionService>()));

        // === Markdown Normalizer ===
        services.AddSingleton<IMarkdownNormalizer, MarkdownNormalizer>();

        // === Document Refiner ===
        services.AddScoped<IDocumentRefiner>(provider =>
        {
            var markdownConverter = provider.GetService<IMarkdownConverter>();
            var markdownNormalizer = provider.GetService<IMarkdownNormalizer>();
            var loggerFactory = provider.GetService<ILoggerFactory>();
            var logger = loggerFactory?.CreateLogger<DocumentRefiner>();
            return new DocumentRefiner(markdownConverter, markdownNormalizer, logger);
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

        // Note: IEmbeddingService and ITextCompletionService are not registered by default.
        // Consumer applications should inject their own implementations via DI.

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
    /// <remarks>
    /// This is equivalent to:
    /// <code>
    /// services.AddNativeOfficeReader();
    /// services.AddFileFlux();
    /// </code>
    /// </remarks>
    public static IServiceCollection AddFileFluxWithNativeOffice(this IServiceCollection services)
    {
        services.AddNativeOfficeReader();
        return AddFileFlux(services);
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
}
