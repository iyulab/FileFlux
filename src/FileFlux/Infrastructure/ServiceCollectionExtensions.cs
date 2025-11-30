using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using FileFlux.Core;
using FileFlux.Infrastructure.Factories;
using FileFlux.Infrastructure.Languages;
using FileFlux.Core.Infrastructure.Readers;
using FileFlux.Infrastructure.Parsers;
using FileFlux.Infrastructure.Services;
using FileFlux.Infrastructure.Strategies;
using FileFlux.Infrastructure;

namespace FileFlux;

/// <summary>
/// FileFlux 서비스 등록을 위한 확장 메서드 - 새 Reader/Parser 아키텍처
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// FileFlux 서비스들을 DI 컨테이너에 등록 - AI 서비스들은 선택적 의존성으로 처리
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddFileFlux(this IServiceCollection services)
    {
        // 기본 텍스트 Reader들 등록 (Factory보다 먼저 등록)
        services.AddTransient<IDocumentReader, TextDocumentReader>();
        services.AddTransient<IDocumentReader, MarkdownDocumentReader>();
        services.AddTransient<IDocumentReader, HtmlDocumentReader>();

        // 이미지 처리 기능이 포함된 멀티모달 Reader들 등록
        // IImageToTextService가 제공되면 vision API 사용, 없으면 기본 텍스트만 추출
        services.AddTransient<IDocumentReader, MultiModalPdfDocumentReader>();
        services.AddTransient<IDocumentReader, MultiModalPowerPointDocumentReader>();
        services.AddTransient<IDocumentReader, MultiModalWordDocumentReader>();
        services.AddTransient<IDocumentReader, MultiModalExcelDocumentReader>();

        // Language profile provider for multilingual text segmentation
        services.AddSingleton<ILanguageProfileProvider, DefaultLanguageProfileProvider>();

        // 핵심 팩토리들 등록 - DI로 주입된 Reader들 사용
        // Scoped로 변경: MultiModalPdfDocumentReader가 scoped IImageToTextService를 사용하므로
        services.AddScoped<IDocumentReaderFactory>(provider =>
        {
            // DI로 주입된 Reader들로 Factory 생성
            return new DocumentReaderFactory(provider.GetServices<IDocumentReader>());
        });
        services.AddSingleton<IDocumentParserFactory>(provider =>
            new DocumentParserFactory(provider.GetService<ITextCompletionService>()));

        // 기본 청킹 전략들을 등록하는 팩토리
        RegisterChunkingStrategies(services);

        // 문서 유형 최적화기 등록
        services.AddSingleton<IDocumentTypeOptimizer, DocumentTypeOptimizer>();

        // 적응형 전략 선택기 등록 (Auto 전략 지원) - AI 서비스 및 DocumentTypeOptimizer 선택적
        services.AddScoped<IAdaptiveStrategySelector>(provider =>
            new AdaptiveStrategySelector(
                provider.GetRequiredService<IChunkingStrategyFactory>(),
                provider.GetService<ITextCompletionService>(),
                documentReader: null,
                provider.GetService<IDocumentTypeOptimizer>()));

        // Metadata enrichment services (Phase 16) - DocumentProcessor보다 먼저 등록
        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 100; // Cache up to 100 documents
        });
        services.AddSingleton<RuleBasedMetadataExtractor>();
        services.AddScoped<IMetadataEnricher>(provider =>
            new AIMetadataEnricher(
                provider.GetRequiredService<RuleBasedMetadataExtractor>(),
                provider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                provider.GetService<ITextCompletionService>(),
                provider.GetService<ILogger<AIMetadataEnricher>>()));

        // 메인 문서 처리기 등록 - IMetadataEnricher를 명시적으로 주입
        services.AddScoped<IDocumentProcessor>(provider =>
            new DocumentProcessor(
                provider.GetRequiredService<IDocumentReaderFactory>(),
                provider.GetRequiredService<IDocumentParserFactory>(),
                provider.GetRequiredService<IChunkingStrategyFactory>(),
                provider.GetService<IMetadataEnricher>(),
                provider.GetService<ILogger<DocumentProcessor>>()));

        // 기본 Parser들 등록 - AI 서비스 선택적
        services.AddTransient<IDocumentParser>(provider =>
            new BasicDocumentParser(provider.GetService<ITextCompletionService>()));

        // Embedding 서비스 관련 등록 (Phase 8)
        // LocalEmbedder를 기본 임베딩 서비스로 사용 (production-ready)
        // 소비 애플리케이션에서 커스텀 IEmbeddingService를 등록하여 오버라이드 가능
        services.TryAddSingleton<IEmbeddingService>(provider =>
        {
            var logger = provider.GetService<ILogger<LocalEmbedderService>>();
            return new LocalEmbedderService(options: null, logger);
        });

        // Semantic analysis services - AI 서비스들은 선택적 의존성으로 처리
        services.AddSingleton<ISemanticBoundaryDetector, SemanticBoundaryDetector>();
        services.AddSingleton<IChunkCoherenceAnalyzer>(provider =>
            new ChunkCoherenceAnalyzer(provider.GetService<ISemanticBoundaryDetector>()));

        // Quality analysis services
        services.AddTransient<FileFlux.Infrastructure.Quality.ChunkQualityEngine>();

        // Phase 15 성능 최적화 컴포넌트들은 별도로 등록 가능

        return services;
    }

    /// <summary>
    /// 특정 텍스트 완성 서비스 인스턴스와 함께 FileFlux 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="textCompletionService">텍스트 완성 서비스 인스턴스</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddFileFlux(this IServiceCollection services, ITextCompletionService textCompletionService)
    {
        ArgumentNullException.ThrowIfNull(textCompletionService);

        // 텍스트 완성 서비스 등록
        services.AddSingleton(textCompletionService);

        // FileFlux 서비스들 등록
        return AddFileFlux(services);
    }

    /// <summary>
    /// 텍스트 완성 및 이미지-텍스트 서비스와 함께 FileFlux 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="textCompletionService">텍스트 완성 서비스 인스턴스</param>
    /// <param name="imageToTextService">이미지-텍스트 변환 서비스 인스턴스 (선택사항)</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddFileFlux(
        this IServiceCollection services,
        ITextCompletionService textCompletionService,
        IImageToTextService? imageToTextService = null)
    {
        ArgumentNullException.ThrowIfNull(textCompletionService);

        // 텍스트 완성 서비스 등록
        services.AddSingleton(textCompletionService);

        // 이미지-텍스트 서비스 등록 (제공된 경우)
        if (imageToTextService != null)
        {
            services.AddSingleton(imageToTextService);

            // 이미지 서비스가 있을 때 기본 관련성 평가기 등록 가능
        }

        // FileFlux 서비스들 등록
        return AddFileFlux(services);
    }

    /// <summary>
    /// 모든 선택적 서비스와 함께 FileFlux 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="textCompletionService">텍스트 완성 서비스 인스턴스</param>
    /// <param name="imageToTextService">이미지-텍스트 변환 서비스 인스턴스 (선택사항)</param>
    /// <param name="imageRelevanceEvaluator">이미지 관련성 평가 서비스 인스턴스 (선택사항)</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddFileFlux(
        this IServiceCollection services,
        ITextCompletionService textCompletionService,
        IImageToTextService? imageToTextService = null,
        IImageRelevanceEvaluator? imageRelevanceEvaluator = null)
    {
        ArgumentNullException.ThrowIfNull(textCompletionService);

        // 텍스트 완성 서비스 등록
        services.AddSingleton(textCompletionService);

        // 이미지-텍스트 서비스 등록 (제공된 경우)
        if (imageToTextService != null)
        {
            services.AddSingleton(imageToTextService);
        }

        // 이미지 관련성 평가 서비스 등록
        if (imageRelevanceEvaluator != null)
        {
            services.AddSingleton(imageRelevanceEvaluator);
        }
        else if (imageToTextService != null)
        {
            // 이미지 서비스는 있지만 평가기가 없는 경우 기본 평가기 사용 가능
        }

        // FileFlux 서비스들 등록
        return AddFileFlux(services);
    }

    /// <summary>
    /// LocalEmbedder 옵션을 구성하여 FileFlux 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">LocalEmbedder 옵션 구성 액션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddFileFluxWithLocalEmbedder(
        this IServiceCollection services,
        Action<LocalEmbedderOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

        var options = new LocalEmbedderOptions();
        configureOptions(options);

        // LocalEmbedder 서비스를 커스텀 옵션과 함께 등록
        services.AddSingleton<IEmbeddingService>(provider =>
        {
            var logger = provider.GetService<ILogger<LocalEmbedderService>>();
            return new LocalEmbedderService(options, logger);
        });

        // FileFlux 서비스들 등록
        return AddFileFlux(services);
    }

    /// <summary>
    /// Mock 서비스들과 함께 FileFlux 서비스 등록 (테스트용)
    /// Only available in DEBUG builds - excluded from production Release builds.
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="useMockServices">Mock 서비스 사용 여부</param>
    /// <returns>서비스 컬렉션</returns>
#if DEBUG
    public static IServiceCollection AddFileFluxWithMocks(this IServiceCollection services, bool useMockServices = true)
    {
        if (useMockServices)
        {
            services.AddSingleton<IImageToTextService, MockImageToTextService>();
            // Mock embedding service for testing (overrides LocalEmbedder)
            services.AddSingleton<IEmbeddingService, MockEmbeddingService>();
        }

        // FileFlux 서비스들 등록
        return AddFileFlux(services);
    }
#endif

    /// <summary>
    /// 커스텀 Document Reader 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="reader">등록할 Reader</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddDocumentReader<T>(this IServiceCollection services) where T : class, IDocumentReader
    {
        services.AddTransient<IDocumentReader, T>();
        return services;
    }

    /// <summary>
    /// 커스텀 Document Parser 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddDocumentParser<T>(this IServiceCollection services) where T : class, IDocumentParser
    {
        services.AddTransient<IDocumentParser, T>();
        return services;
    }

    private static void RegisterChunkingStrategies(IServiceCollection services)
    {
        // Auto 전략을 지원하는 팩토리 사용 (DI 지원)
        // Scoped로 등록: IDocumentProcessor (Scoped)가 주입받고,
        // scoped ServiceProvider를 통해 scoped IAdaptiveStrategySelector를 resolve할 수 있음
        services.AddScoped<IChunkingStrategyFactory, ChunkingStrategyFactory>();
    }
}
