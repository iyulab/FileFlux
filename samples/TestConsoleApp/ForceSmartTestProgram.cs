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
/// Smart 전략을 강제 사용하는 테스트 프로그램
/// </summary>
public static class ForceSmartTestProgram
{
    public static async Task RunForceSmartTest()
    {
        var path = @"D:\test-data\채변프로그램 변경[25.02.03].pdf";

        Console.WriteLine("FileFlux PDF Processing Test (FORCE SMART STRATEGY)");
        Console.WriteLine($"Processing: {path}");
        Console.WriteLine(new string('=', 60));

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
            Console.WriteLine("\n🚀 Using OpenAI services for FORCE SMART processing...");

            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!;
            var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-5-nano";

            var openAiClient = new OpenAIClient(apiKey);
            var chatClient = openAiClient.GetChatClient(model);

            services.AddSingleton(chatClient);
            services.AddSingleton<ITextCompletionService, OpenAiTextCompletionService>();
            services.AddSingleton<IImageToTextService>(provider => new OpenAiImageToTextService(apiKey));
        }
        else
        {
            Console.WriteLine("\n⚠️ Using Mock services (OpenAI not configured)");
            services.AddSingleton<ITextCompletionService, MockTextCompletionService>();
            services.AddSingleton<IImageToTextService, MockImageToTextService>();
        }

        var serviceProvider = services.BuildServiceProvider();
        var processingService = serviceProvider.GetRequiredService<IDocumentProcessor>();

        var chunks = new List<DocumentChunk>();

        try
        {
            Console.WriteLine("Starting FORCE SMART document processing...\n");

            // Smart 전략 강제 사용
            var smartOptions = new ChunkingOptions
            {
                Strategy = ChunkingStrategies.Smart,  // 강제로 Smart 전략 사용
                MaxChunkSize = 600,                   // 작은 청크 크기
                OverlapSize = 50
            };

            // 추가 옵션 설정
            smartOptions.CustomProperties["MinCompleteness"] = 0.8;
            smartOptions.CustomProperties["PreserveSentences"] = true;
            smartOptions.CustomProperties["SmartOverlap"] = true;

            await foreach (var chunk in processingService.ProcessAsync(path, smartOptions))
            {
                chunks.Add(chunk);

                // Display progress
                Console.WriteLine($"Chunk #{chunks.Count}: {chunk.Content.Length} chars, Quality: {chunk.QualityScore:F2}");
            }

            Console.WriteLine($"\n✅ FORCE SMART Processing Complete!");
            Console.WriteLine(new string('=', 60));
            Console.WriteLine($"📄 Total chunks: {chunks.Count}");
            Console.WriteLine($"📊 Average quality score: {chunks.Average(c => c.QualityScore):F2}");
            Console.WriteLine($"📝 Total characters: {chunks.Sum(c => c.Content.Length):N0}");
            Console.WriteLine($"⏱️ Average chunk size: {chunks.Average(c => c.Content.Length):F0} chars");
            Console.WriteLine($"🤖 AI Service: {(hasOpenAI ? "OpenAI GPT-5-nano" : "Mock Service")} (FORCE SMART)");

            // Show all chunks
            Console.WriteLine($"\n📋 All {chunks.Count} chunks preview:");
            Console.WriteLine(new string('-', 60));

            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var preview = chunk.Content.Length > 200
                    ? chunk.Content[..200] + "..."
                    : chunk.Content;

                Console.WriteLine($"\n--- Chunk {i + 1} (Quality: {chunk.QualityScore:F2}, Size: {chunk.Content.Length}) ---");
                Console.WriteLine(preview.Replace('\n', ' ').Replace('\r', ' '));
            }

            // 상세한 메타데이터 정보
            if (chunks.Any())
            {
                Console.WriteLine($"\n📋 Detailed Metadata (FORCE SMART):");
                Console.WriteLine($"  - Source: {path}");
                Console.WriteLine($"  - Processing Strategy: {chunks.First().Strategy}");
                Console.WriteLine($"  - Content Types: {string.Join(", ", chunks.Select(c => c.ContentType).Distinct())}");
                Console.WriteLine($"  - Quality Range: {chunks.Min(c => c.QualityScore):F2} - {chunks.Max(c => c.QualityScore):F2}");
                Console.WriteLine($"  - Size Range: {chunks.Min(c => c.Content.Length)} - {chunks.Max(c => c.Content.Length)} chars");
            }

            // Save force smart results
            await SaveForceSmartResults(chunks, hasOpenAI, path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error processing document: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine("\n✨ FORCE SMART Test completed successfully!");
    }

    private static async Task SaveForceSmartResults(List<DocumentChunk> chunks, bool hasOpenAI, string path)
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
        var chunkFile = $"output/chunks_force_smart_{timestamp}.json";
        await File.WriteAllTextAsync(chunkFile, JsonSerializer.Serialize(chunkData, jsonOptions));

        // Save force smart report
        var forceSmartReport = $"""
            FileFlux FORCE SMART 전략 테스트 결과
            ====================================

            테스트 일시: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
            입력 파일: {path}
            강제 전략: Smart (Auto 대신 직접 지정)
            AI 서비스: {(hasOpenAI ? "OpenAI GPT-5-nano" : "Mock Service")}

            Smart 전략 결과:
            - 총 청크 수: {chunks.Count}개
            - 평균 품질 점수: {chunks.Average(c => c.QualityScore):F2}
            - 총 문자 수: {chunks.Sum(c => c.Content.Length):N0}
            - 평균 청크 크기: {chunks.Average(c => c.Content.Length):F0} 문자
            - 품질 범위: {chunks.Min(c => c.QualityScore):F2} - {chunks.Max(c => c.QualityScore):F2}
            - 크기 범위: {chunks.Min(c => c.Content.Length)} - {chunks.Max(c => c.Content.Length)} 문자

            Smart 전략 설정:
            - 최대 청크 크기: 600자
            - 오버랩 크기: 50자
            - 완성도 기준: 0.8
            - 문장 경계 보존: True

            청크별 상세 정보:
            """;

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            forceSmartReport += $"""

            청크 {i + 1}:
            - 크기: {chunk.Content.Length}자
            - 품질: {chunk.QualityScore:F2}
            - 전략: {chunk.Strategy}
            - 타입: {chunk.ContentType}
            - 중요도: {chunk.Importance:F2}
            - 내용 미리보기: {(chunk.Content.Length > 150 ? chunk.Content[..150] + "..." : chunk.Content).Replace('\n', ' ')}
            """;
        }

        var reportFile = $"output/force_smart_report_{timestamp}.md";
        await File.WriteAllTextAsync(reportFile, forceSmartReport);

        Console.WriteLine($"\n📁 Force Smart results saved to:");
        Console.WriteLine($"  - Chunks: {chunkFile}");
        Console.WriteLine($"  - Report: {reportFile}");
    }
}