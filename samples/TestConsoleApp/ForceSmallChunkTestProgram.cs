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
/// 매우 작은 청크 크기를 강제하는 테스트 프로그램
/// </summary>
public static class ForceSmallChunkTestProgram
{
    public static async Task RunForceSmallChunkTest()
    {
        var path = Path.Combine("tests", "test-pdf", "oai_gpt-oss_model_card.pdf");

        Console.WriteLine("FileFlux PDF Processing Test (FORCE SMALL CHUNKS)");
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
            Console.WriteLine("\n🚀 Using OpenAI services for SMALL CHUNK processing...");

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
            Console.WriteLine("Starting SMALL CHUNK document processing...\n");

            // 매우 작은 청크 크기로 강제 분할
            var smallChunkOptions = new ChunkingOptions
            {
                Strategy = ChunkingStrategies.Sentence,  // Sentence 전략 사용
                MaxChunkSize = 300,                   // 매우 작은 청크 크기 (300자)
                OverlapSize = 30                      // 작은 오버랩
            };

            // 강제 분할 옵션 추가
            smallChunkOptions.CustomProperties["MinCompleteness"] = 0.6; // 완성도 기준 완화
            smallChunkOptions.CustomProperties["PreserveSentences"] = true;
            smallChunkOptions.CustomProperties["ForceSmallChunks"] = true;
            smallChunkOptions.CustomProperties["AggressiveSplit"] = true;

            await foreach (var chunk in processingService.ProcessAsync(path, smallChunkOptions))
            {
                chunks.Add(chunk);

                // Display progress
                Console.WriteLine($"Chunk #{chunks.Count}: {chunk.Content.Length} chars, Quality: {chunk.QualityScore:F2}");
            }

            Console.WriteLine($"\n✅ SMALL CHUNK Processing Complete!");
            Console.WriteLine(new string('=', 60));
            Console.WriteLine($"📄 Total chunks: {chunks.Count}");
            Console.WriteLine($"📊 Average quality score: {chunks.Average(c => c.QualityScore):F2}");
            Console.WriteLine($"📝 Total characters: {chunks.Sum(c => c.Content.Length):N0}");
            Console.WriteLine($"⏱️ Average chunk size: {chunks.Average(c => c.Content.Length):F0} chars");
            Console.WriteLine($"🤖 AI Service: {(hasOpenAI ? "OpenAI GPT-5-nano" : "Mock Service")} (SMALL CHUNKS)");

            // Show all chunks
            Console.WriteLine($"\n📋 All {chunks.Count} chunks preview:");
            Console.WriteLine(new string('-', 60));

            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var preview = chunk.Content.Length > 100
                    ? chunk.Content[..100] + "..."
                    : chunk.Content;

                Console.WriteLine($"\n--- Chunk {i + 1} (Quality: {chunk.QualityScore:F2}, Size: {chunk.Content.Length}) ---");
                Console.WriteLine(preview.Replace('\n', ' ').Replace('\r', ' '));
            }

            // 상세한 메타데이터 정보
            if (chunks.Any())
            {
                Console.WriteLine($"\n📋 Detailed Metadata (SMALL CHUNKS):");
                Console.WriteLine($"  - Source: {path}");
                Console.WriteLine($"  - Processing Strategy: {chunks.First().Strategy}");
                Console.WriteLine($"  - Content Types: {string.Join(", ", chunks.Select(c => c.ContentType).Distinct())}");
                Console.WriteLine($"  - Quality Range: {chunks.Min(c => c.QualityScore):F2} - {chunks.Max(c => c.QualityScore):F2}");
                Console.WriteLine($"  - Size Range: {chunks.Min(c => c.Content.Length)} - {chunks.Max(c => c.Content.Length)} chars");
            }

            // Save small chunk results
            await SaveSmallChunkResults(chunks, hasOpenAI, path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error processing document: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine("\n✨ SMALL CHUNK Test completed successfully!");
    }

    private static async Task SaveSmallChunkResults(List<DocumentChunk> chunks, bool hasOpenAI, string path)
    {
        Directory.CreateDirectory("output");

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

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

        var chunkFile = $"output/chunks_small_chunks_{timestamp}.json";
        await File.WriteAllTextAsync(chunkFile, JsonSerializer.Serialize(chunkData, jsonOptions));

        // Save small chunk report
        var smallChunkReport = $"""
            FileFlux SMALL CHUNK 강제 분할 테스트 결과
            =========================================

            테스트 일시: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
            입력 파일: {path}
            강제 전략: Smart (MaxChunkSize=300)
            AI 서비스: {(hasOpenAI ? "OpenAI GPT-5-nano" : "Mock Service")}

            Small Chunk 결과:
            - 총 청크 수: {chunks.Count}개
            - 평균 품질 점수: {chunks.Average(c => c.QualityScore):F2}
            - 총 문자 수: {chunks.Sum(c => c.Content.Length):N0}
            - 평균 청크 크기: {chunks.Average(c => c.Content.Length):F0} 문자
            - 품질 범위: {chunks.Min(c => c.QualityScore):F2} - {chunks.Max(c => c.QualityScore):F2}
            - 크기 범위: {chunks.Min(c => c.Content.Length)} - {chunks.Max(c => c.Content.Length)} 문자

            설정:
            - 최대 청크 크기: 300자 (강제 분할)
            - 오버랩 크기: 30자
            - 완성도 기준: 0.6 (완화)
            - 강제 분할: True

            📊 분할 성공 여부:
            {(chunks.Count > 1 ? "✅ 성공 - 문서가 여러 청크로 분할됨" : "❌ 실패 - 여전히 단일 청크")}

            청크별 상세 정보:
            """;

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            smallChunkReport += $"""

            청크 {i + 1}:
            - 크기: {chunk.Content.Length}자
            - 품질: {chunk.QualityScore:F2}
            - 전략: {chunk.Strategy}
            - 타입: {chunk.ContentType}
            - 중요도: {chunk.Importance:F2}
            - 내용 미리보기: {(chunk.Content.Length > 100 ? chunk.Content[..100] + "..." : chunk.Content).Replace('\n', ' ')}
            """;
        }

        var reportFile = $"output/small_chunk_report_{timestamp}.md";
        await File.WriteAllTextAsync(reportFile, smallChunkReport);

        Console.WriteLine($"\n📁 Small Chunk results saved to:");
        Console.WriteLine($"  - Chunks: {chunkFile}");
        Console.WriteLine($"  - Report: {reportFile}");
    }
}