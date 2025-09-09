using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FileFlux.Infrastructure.Readers;
using FileFlux.Infrastructure.Parsers;
using FileFlux.Infrastructure.Services;
using FileFlux.Infrastructure.Processing;

namespace FileFlux;

/// <summary>
/// FileFlux 서비스 등록을 위한 확장 메서드 - 새 Reader/Parser 아키텍처
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// FileFlux 서비스들을 DI 컨테이너에 등록 - 텍스트 완성 서비스를 DI에서 주입받아 사용
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddFileFlux(this IServiceCollection services)
    {
        // 핵심 팩토리들 등록
        services.AddSingleton<IDocumentReaderFactory, DocumentReaderFactory>();
        services.AddSingleton<IDocumentParserFactory>(provider =>
            new DocumentParserFactory(provider.GetRequiredService<ITextCompletionService>()));

        // 기본 청킹 전략들을 등록하는 팩토리
        RegisterChunkingStrategies(services);

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

        // 기본 Parser들 등록
        services.AddTransient<IDocumentParser>(provider =>
            new BasicDocumentParser(provider.GetRequiredService<ITextCompletionService>()));

        // Embedding 서비스 관련 등록 (Phase 8)
        // Note: 실제 IEmbeddingService는 소비 애플리케이션에서 등록해야 함
        // 여기서는 Mock 서비스를 fallback으로 등록
        services.TryAddSingleton<IEmbeddingService, MockEmbeddingService>();
        
        // Semantic analysis services
        services.AddSingleton<ISemanticBoundaryDetector, SemanticBoundaryDetector>();
        services.AddSingleton<IChunkCoherenceAnalyzer, ChunkCoherenceAnalyzer>();

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
        }

        // FileFlux 서비스들 등록
        return AddFileFlux(services);
    }

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
        // 기본 청킹 전략들을 ChunkingStrategyFactory에 등록
        services.AddSingleton<IChunkingStrategyFactory>(provider =>
        {
            var factory = new ChunkingStrategyFactory();

            // 기본 청킹 전략들 등록 (모든 전략 포함)
            factory.RegisterStrategy(() => new Infrastructure.Strategies.FixedSizeChunkingStrategy());
            factory.RegisterStrategy(() => new Infrastructure.Strategies.SemanticChunkingStrategy());
            factory.RegisterStrategy(() => new Infrastructure.Strategies.ParagraphChunkingStrategy());
            factory.RegisterStrategy(() => new Infrastructure.Strategies.IntelligentChunkingStrategy());

            return factory;
        });
    }
}