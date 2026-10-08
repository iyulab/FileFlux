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
    /// The Markdown normalizer is always registered as Singleton (thread-safe, stateless, no DI captures).
    /// Factories, converters, and processors respect the lifetime parameter. In particular the parser
    /// factory and Markdown converter capture <see cref="IDocumentAnalysisService"/>, which consumers
    /// commonly register as Scoped; registering them with the shared lifetime keeps the graph scope-valid
    /// (no captive dependency) so consumers need not disable <c>ValidateScopes</c>.
    /// </remarks>
    public static IServiceCollection AddFileFlux(
        this IServiceCollection services,
        ServiceLifetime lifetime)
    {
        // === FileFlux.Core: Document Readers (always Transient - stateless) ===
        services.AddTransient<IDocumentReader, TextDocumentReader>();
        services.AddTransient<IDocumentReader, MarkdownDocumentReader>();
        services.AddTransient<IDocumentReader, HtmlDocumentReader>();
        services.AddTransient<IDocumentReader, CsvDocumentReader>();
        services.AddTransient<IDocumentReader, MultiModalPdfDocumentReader>();
        services.AddTransient<IDocumentReader, MultiModalPowerPointDocumentReader>();
        services.AddTransient<IDocumentReader, MultiModalWordDocumentReader>();
        services.AddTransient<IDocumentReader, MultiModalExcelDocumentReader>();
        services.AddTransient<IDocumentReader, LegacyExcelDocumentReader>();
        services.AddTransient<IDocumentReader, HwpDocumentReader>();
        // Audio reads through IAudioToTextService; with none registered the reader claims no file.
        services.AddTransient<IDocumentReader>(sp => new AudioDocumentReader(sp.GetService<IAudioToTextService>()));

        // Language profile for multilingual support (always Singleton - thread-safe)
        services.AddSingleton<ILanguageProfileProvider, DefaultLanguageProfileProvider>();

        // Reader factory (configurable lifetime). The factory prefers the reader registered last for an extension, so the
        // built-ins go first and every reader the caller added (AddDocumentReader, AddNativeOfficeReader, a plain
        // AddTransient<IDocumentReader, …>) goes after them, whichever side of AddFileFlux() it was registered on.
        services.Add(new ServiceDescriptor(
            typeof(IDocumentReaderFactory),
            provider => new DocumentReaderFactory(BuiltInReadersFirst(provider.GetServices<IDocumentReader>())),
            lifetime));

        // Parser factory (configurable lifetime - captures IDocumentAnalysisService, which consumers
        // commonly register as Scoped; sharing the lifetime avoids a captive dependency)
        services.Add(new ServiceDescriptor(
            typeof(IDocumentParserFactory),
            provider => new DocumentParserFactory(provider.GetService<IDocumentAnalysisService>()),
            lifetime));

        // Basic parser (always Transient - may hold state)
        services.AddTransient<IDocumentParser>(provider =>
            new BasicDocumentParser(provider.GetService<IDocumentAnalysisService>()));

        // === Markdown Converter (configurable lifetime - captures IDocumentAnalysisService, which
        // consumers commonly register as Scoped; sharing the lifetime avoids a captive dependency) ===
        services.Add(new ServiceDescriptor(
            typeof(IMarkdownConverter),
            provider => new MarkdownConverter(provider.GetService<IDocumentAnalysisService>()),
            lifetime));

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
                var textCompletionService = provider.GetService<IDocumentAnalysisService>();
                var loggerFactory = provider.GetService<ILoggerFactory>();
                var logger = loggerFactory?.CreateLogger<LlmRefiner>();
                return new LlmRefiner(textCompletionService, logger);
            },
            lifetime));

        // === FluxCurator: Chunking ===
        // Semantic chunking runs on FluxCurator, which reads FluxCurator's IEmbedder. A FileFlux IEmbeddingService
        // (AddLMSupplyEmbedding, an adapter, your own - registered as a singleton) reaches it through this bridge; an IEmbedder
        // registered before AddFileFlux wins. With neither, the factory yields null and Semantic reports the missing embedder.
        services.TryAddSingleton<IEmbedder>(sp =>
            sp.GetService<IEmbeddingService>() is { } embeddingService ? new EmbeddingServiceEmbedder(embeddingService) : null!);
        services.AddFluxCurator();

        // === FluxImprover: Enhancement (optional, configurable lifetime) ===
        // FluxImproverServices is FluxImprover's own aggregate type, and a consumer may register it
        // through FluxImprover itself (AddFluxImprover / AddFluxImproverWithLMSupply). This descriptor
        // therefore (1) yields to an existing registration instead of shadowing it — with services.Add
        // the later, null-returning factory won and every FluxImprover facade in the same container
        // failed with "No service for type FluxImproverServices" — and
        // (2) when FileFlux has no IDocumentAnalysisService of its own, builds from FluxImprover's
        // ITextGenerationService if the consumer registered one, so the order of AddFileFlux and
        // AddFluxImprover does not matter. Null only when neither exists (FileFlux's "no LLM" case).
        services.TryAdd(new ServiceDescriptor(
            typeof(FluxImproverServices),
            provider =>
            {
                var completionService = provider.GetService<IDocumentAnalysisService>();
                if (completionService != null)
                {
                    // Adapt FileFlux's IDocumentAnalysisService to FluxImprover's interface
                    var adapter = new FluxImproverTextCompletionAdapter(completionService);
                    return new FluxImproverBuilder()
                        .WithCompletionService(adapter)
                        .Build();
                }

                var improverCompletion = provider.GetService<ITextGenerationService>();
                if (improverCompletion != null)
                {
                    return new FluxImproverBuilder()
                        .WithCompletionService(improverCompletion)
                        .Build();
                }

                return null!;
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

        // No IMemoryCache is registered here: the shared cache and its policy (SizeLimit) belong to
        // the host. Nothing FileFlux registers resolves IMemoryCache; AIMetadataEnricher takes one
        // from its caller.

        // Note: IEmbeddingService and IDocumentAnalysisService are not registered by default.
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
        IDocumentAnalysisService textCompletionService,
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
        IDocumentAnalysisService textCompletionService,
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
    /// The readers <see cref="AddFileFlux(IServiceCollection, ServiceLifetime)"/> registers. Keep in step with the
    /// registrations there — a test pins it.
    /// </summary>
    internal static readonly IReadOnlySet<Type> BuiltInReaderTypes = new HashSet<Type>
    {
        typeof(TextDocumentReader), typeof(MarkdownDocumentReader), typeof(HtmlDocumentReader), typeof(CsvDocumentReader),
        typeof(MultiModalPdfDocumentReader), typeof(MultiModalPowerPointDocumentReader), typeof(MultiModalWordDocumentReader),
        typeof(MultiModalExcelDocumentReader), typeof(LegacyExcelDocumentReader), typeof(HwpDocumentReader),
        typeof(AudioDocumentReader),
    };

    private static IEnumerable<IDocumentReader> BuiltInReadersFirst(IEnumerable<IDocumentReader> readers)
    {
        var all = readers.ToList();
        return all.Where(r => BuiltInReaderTypes.Contains(r.GetType()))
            .Concat(all.Where(r => !BuiltInReaderTypes.Contains(r.GetType())));
    }

    /// <summary>
    /// Adds a custom document reader. It takes precedence over the built-in reader for the extensions it claims, whether it
    /// is registered before or after <c>AddFileFlux()</c>; among readers you add, the one registered last wins.
    /// </summary>
    public static IServiceCollection AddDocumentReader<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this IServiceCollection services)
        where T : class, IDocumentReader
    {
        services.AddTransient<IDocumentReader, T>();
        return services;
    }

    /// <summary>
    /// Adds OfficeNativeDocumentReader for high-performance DOCX, XLSX, PPTX processing.
    /// Uses the undoc native library (Rust-based). The binary is downloaded on-demand from GitHub
    /// releases on first use; ongoing self-update is OFF by default (the cached/pinned binary is reused).
    /// </summary>
    /// <remarks>
    /// The native reader provides:
    /// - Faster processing compared to managed libraries
    /// - Better CJK text handling
    /// - Parallel section processing
    /// - Optional background self-update (opt-in via UndocNativeLoader.AutoUpdateEnabled or the
    ///   FILEFLUX_NATIVE_AUTOUPDATE environment variable; off by default for reproducibility)
    ///
    /// The native reader takes precedence over the built-in DOCX/XLSX/PPTX readers whether this is called before or after
    /// AddFileFlux(). (It used to lose to them: the reader factory prefers the reader registered last, and the built-ins
    /// were registered after it.)
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
    public static IServiceCollection AddDocumentParser<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this IServiceCollection services)
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
