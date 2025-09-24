using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FileFlux.Core;
using FileFlux.Infrastructure.Factories;
using FileFlux.Infrastructure.Readers;
using FileFlux.Infrastructure.Parsers;
using FileFlux.Infrastructure.Services;
using FileFlux.Infrastructure.Processing;
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
        // 핵심 팩토리들 등록 - AI 서비스들은 선택적 의존성
        services.AddSingleton<IDocumentReaderFactory, DocumentReaderFactory>();
        services.AddSingleton<IDocumentParserFactory>(provider =>
            new DocumentParserFactory(provider.GetService<ITextCompletionService>()));

        // 기본 청킹 전략들을 등록하는 팩토리
        RegisterChunkingStrategies(services);

        // 적응형 전략 선택기 등록 (Auto 전략 지원) - AI 서비스 선택적
        services.AddScoped<IAdaptiveStrategySelector>(provider =>
            new AdaptiveStrategySelector(
                provider.GetRequiredService<IChunkingStrategyFactory>(),
                provider.GetService<ITextCompletionService>()));

        // 메인 문서 처리기 등록
        services.AddScoped<IDocumentProcessor, DocumentProcessor>();
        
        // 병렬 문서 처리기 등록 (Phase 8)
        services.AddScoped<IParallelDocumentProcessor, ParallelDocumentProcessor>();
        
        // 스트리밍 및 캐시 서비스 등록 (Phase 8)
        services.AddSingleton<DocumentCacheOptions>(provider => new DocumentCacheOptions());
        services.AddSingleton<IDocumentCacheService, Infrastructure.Caching.DocumentCacheService>();
        services.AddScoped<IStreamingDocumentProcessor, Infrastructure.Streaming.StreamingDocumentProcessor>();

        // 기본 Reader들 등록
        services.AddTransient<IDocumentReader, TextDocumentReader>();
        
        // 이미지 처리 기능이 포함된 PDF Reader 등록
        services.AddTransient<IDocumentReader, MultiModalPdfDocumentReader>();

        // 기본 Parser들 등록 - AI 서비스 선택적
        services.AddTransient<IDocumentParser>(provider =>
            new BasicDocumentParser(provider.GetService<ITextCompletionService>()));

        // Embedding 서비스 관련 등록 (Phase 8)
        // Note: 실제 IEmbeddingService는 소비 애플리케이션에서 등록해야 함
        // Mock 서비스는 DEBUG 빌드에서만 fallback으로 사용
#if DEBUG
        services.TryAddSingleton<IEmbeddingService, MockEmbeddingService>();
#endif

        // Semantic analysis services - AI 서비스들은 선택적 의존성으로 처리
        services.AddSingleton<ISemanticBoundaryDetector, SemanticBoundaryDetector>();
        services.AddSingleton<IChunkCoherenceAnalyzer>(provider =>
            new ChunkCoherenceAnalyzer(provider.GetService<ISemanticBoundaryDetector>()));

        // Quality analysis services - AI 서비스 선택적
        services.AddTransient(provider =>
            new FileFlux.Infrastructure.Quality.ChunkQualityEngine(
                provider.GetService<ITextCompletionService>()));

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
        services.AddSingleton<IChunkingStrategyFactory, ChunkingStrategyFactory>();
    }
}