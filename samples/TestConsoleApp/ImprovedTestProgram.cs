using FileFlux;
using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Tests.Mocks;
using FileFlux.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using System.Text.Json;

namespace TestConsoleApp;

/// <summary>
/// Phase 15 개선사항을 적용한 테스트 프로그램
/// </summary>
public static class ImprovedTestProgram
{
    public static async Task RunImprovedTest()
    {
        var path = @"D:\test-data\채변프로그램 변경[25.02.03].pdf";

        Console.WriteLine("FileFlux PDF Processing Test (IMPROVED - Phase 15)");
        Console.WriteLine($"Processing: {path}");
        Console.WriteLine(new string('=', 50));

        // Load environment variables from .env.local
        EnvLoader.LoadFromFile();
        var hasOpenAI = EnvLoader.ValidateOpenAIConfig();

        // Setup DI container with FileFlux services
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());

        // Register FileFlux services
        services.AddFileFlux();

        // Configure AI services based on availability
        if (hasOpenAI)
        {
            Console.WriteLine("\n🚀 Using OpenAI services for IMPROVED processing...");

            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!;
            var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-5-nano";

            var openAiClient = new OpenAIClient(apiKey);
            var chatClient = openAiClient.GetChatClient(model);

            services.AddSingleton(chatClient);
            services.AddSingleton<IDocumentAnalysisService, OpenAITextGenerationService>();
            services.AddSingleton<IImageToTextService>(provider => new OpenAiImageToTextService(apiKey));
        }
        else
        {
            Console.WriteLine("\n⚠️ Using Mock services (OpenAI not configured)");
            services.AddSingleton<IDocumentAnalysisService, MockTextCompletionService>();
            services.AddSingleton<IImageToTextService, MockImageToTextService>();
        }

        var serviceProvider = services.BuildServiceProvider();
        var processingService = serviceProvider.GetRequiredService<IDocumentProcessor>();

        var chunks = new List<DocumentChunk>();

        try
        {
            Console.WriteLine("Starting IMPROVED document processing...\n");

            // 개선된 청킹 옵션 사용
            var improvedOptions = TestConfig.GetImprovedOptions();

            await foreach (var chunk in processingService.ProcessAsync(path, improvedOptions))
            {
                chunks.Add(chunk);

                // Display progress
                Console.WriteLine($"Chunk #{chunks.Count}: {chunk.Content.Length} chars, Quality: {chunk.QualityScore:F2}");
            }

            Console.WriteLine($"\n✅ IMPROVED Processing Complete!");
            Console.WriteLine(new string('=', 50));
            Console.WriteLine($"📄 Total chunks: {chunks.Count}");
            Console.WriteLine($"📊 Average quality score: {chunks.Average(c => c.QualityScore):F2}");
            Console.WriteLine($"📝 Total characters: {chunks.Sum(c => c.Content.Length):N0}");
            Console.WriteLine($"⏱️ Average chunk size: {chunks.Average(c => c.Content.Length):F0} chars");
            Console.WriteLine($"🤖 AI Service: {(hasOpenAI ? "OpenAI GPT-5-nano" : "Mock Service")} (IMPROVED)");

            // Show all chunks (since we expect more now)
            Console.WriteLine($"\n📋 All {chunks.Count} chunks preview:");
            Console.WriteLine(new string('-', 50));

            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var preview = chunk.Content.Length > 150
                    ? chunk.Content[..150] + "..."
                    : chunk.Content;

                Console.WriteLine($"\nChunk {i + 1} (Quality: {chunk.QualityScore:F2}, Size: {chunk.Content.Length}):");
                Console.WriteLine(preview.Replace('\n', ' ').Replace('\r', ' '));
            }

            // 상세한 메타데이터 정보
            if (chunks.Any())
            {
                Console.WriteLine($"\n📋 Detailed Metadata:");
                Console.WriteLine($"  - Source: {path}");
                Console.WriteLine($"  - Processing Strategy: {chunks.First().Strategy}");
                Console.WriteLine($"  - Content Types: {string.Join(", ", chunks.Select(c => c.ContentType).Distinct())}");
                Console.WriteLine($"  - Quality Range: {chunks.Min(c => c.QualityScore):F2} - {chunks.Max(c => c.QualityScore):F2}");
            }

            // Save improved results
            await SaveImprovedResults(chunks, hasOpenAI, path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error processing document: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine("\n✨ IMPROVED Test completed successfully!");
    }

    private static async Task SaveImprovedResults(List<DocumentChunk> chunks, bool hasOpenAI, string path)
    {
        Directory.CreateDirectory("output");

        // Save chunk details to JSON
        var chunkData = chunks.Select(c => new {
            Id = c.Id,
            Content = c.Content,
            QualityScore = c.QualityScore,
            StartPosition = c.StartPosition,
            EndPosition = c.EndPosition,
            Strategy = c.Strategy,
            ContentType = c.ContentType,
            StructuralRole = c.StructuralRole,
            TechnicalKeywords = c.TechnicalKeywords,
            Properties = c.Properties,
            Importance = c.Importance,
            RelevanceScore = c.RelevanceScore,
            TopicCategory = c.TopicCategory,
            DocumentDomain = c.DocumentDomain,
            CreatedAt = c.CreatedAt
        }).ToArray();

        var jsonOptions = new JsonSerializerOptions {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var chunkFile = $"output/chunks_improved_{timestamp}.json";
        await File.WriteAllTextAsync(chunkFile, JsonSerializer.Serialize(chunkData, jsonOptions));

        // Save comparison report
        var improvementReport = $"""
            FileFlux Phase 15 개선 테스트 결과
            =====================================

            테스트 일시: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
            입력 파일: {path}
            AI 서비스: {(hasOpenAI ? "OpenAI GPT-5-nano" : "Mock Service")} (IMPROVED)

            개선 결과:
            - 총 청크 수: {chunks.Count}개
            - 평균 품질 점수: {chunks.Average(c => c.QualityScore):F2}
            - 총 문자 수: {chunks.Sum(c => c.Content.Length):N0}
            - 평균 청크 크기: {chunks.Average(c => c.Content.Length):F0} 문자
            - 품질 범위: {chunks.Min(c => c.QualityScore):F2} - {chunks.Max(c => c.QualityScore):F2}

            개선 설정:
            - 최대 청크 크기: 600자 (기존 2000자에서 감소)
            - 최소 청크 크기: 200자 (신규 설정)
            - 오버랩 크기: 50자 (기존 200자에서 감소)
            - 목표 품질: 0.75 (기존 0.50에서 상향)
            - 구조 감지: 강화

            청크별 상세 정보:
            """;

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            improvementReport += $"""

            청크 {i + 1}:
            - 크기: {chunk.Content.Length}자
            - 품질: {chunk.QualityScore:F2}
            - 전략: {chunk.Strategy}
            - 타입: {chunk.ContentType}
            - 중요도: {chunk.Importance:F2}
            - 내용 미리보기: {(chunk.Content.Length > 100 ? chunk.Content[..100] + "..." : chunk.Content).Replace('\n', ' ')}
            """;
        }

        var reportFile = $"output/improvement_report_{timestamp}.md";
        await File.WriteAllTextAsync(reportFile, improvementReport);

        Console.WriteLine($"\n📁 Improved results saved to:");
        Console.WriteLine($"  - Chunks: {chunkFile}");
        Console.WriteLine($"  - Report: {reportFile}");
    }
}