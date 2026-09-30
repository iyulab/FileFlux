using FileFlux;
using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Tests.Mocks;
using FileFlux.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using TestConsoleApp;
using System.Text.Json;

// Simple PDF test console app: first argument, or the bundled sample PDF when run from the repository root
var path = args.Length > 0 ? args[0] : Path.Combine("tests", "test-pdf", "oai_gpt-oss_model_card.pdf");

Console.WriteLine("FileFlux PDF Processing Test");
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
    Console.WriteLine("\n🚀 Using OpenAI services for enhanced processing...");

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
    Console.WriteLine("Starting document processing...\n");

    // Simple processing without complex options - just use the processor directly

    await foreach (var chunk in processingService.ProcessAsync(path))
    {
        chunks.Add(chunk);

        // Display progress
        if (chunks.Count % 10 == 0 || chunks.Count <= 5)
        {
            Console.WriteLine($"Processed chunk #{chunks.Count}: {chunk.Content.Length} chars, Quality: {chunk.QualityScore:F2}");
        }
    }

    Console.WriteLine($"\n✅ Processing Complete!");
    Console.WriteLine(new string('=', 50));
    Console.WriteLine($"📄 Total chunks: {chunks.Count}");
    Console.WriteLine($"📊 Average quality score: {chunks.Average(c => c.QualityScore):F2}");
    Console.WriteLine($"📝 Total characters: {chunks.Sum(c => c.Content.Length):N0}");
    Console.WriteLine($"⏱️ Average chunk size: {chunks.Average(c => c.Content.Length):F0} chars");
    Console.WriteLine($"🤖 AI Service: {(hasOpenAI ? "OpenAI GPT-5-nano" : "Mock Service")}");

    // Show first few chunks
    Console.WriteLine("\n📋 First 3 chunks preview:");
    Console.WriteLine(new string('-', 50));

    for (int i = 0; i < Math.Min(3, chunks.Count); i++)
    {
        var chunk = chunks[i];
        var preview = chunk.Content.Length > 200
            ? chunk.Content[..200] + "..."
            : chunk.Content;

        Console.WriteLine($"\nChunk {i + 1} (Quality: {chunk.QualityScore:F2}):");
        Console.WriteLine(preview.Replace('\n', ' ').Replace('\r', ' '));
    }

    // Metadata information
    if (chunks.Any())
    {
        var firstChunk = chunks.First();
        Console.WriteLine($"\n📋 Document Metadata:");
        Console.WriteLine($"  - Source: {path}");
        Console.WriteLine($"  - Chunk ID: {firstChunk.Id}");
        Console.WriteLine($"  - Position: {firstChunk.StartPosition}-{firstChunk.EndPosition}");
        Console.WriteLine($"  - Strategy: {firstChunk.Strategy}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error processing document: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
    return 1;
}

    // Save results to output directory
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
    var chunkFile = $"output/chunks_openai_{timestamp}.json";
    await File.WriteAllTextAsync(chunkFile, JsonSerializer.Serialize(chunkData, jsonOptions));

    // Save summary report
    var summaryReport = $"""
        FileFlux 실제 API 테스트 결과 보고서
        ========================================

        테스트 일시: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
        입력 파일: {path}
        AI 서비스: {(hasOpenAI ? "OpenAI GPT-5-nano" : "Mock Service")}

        처리 결과:
        - 총 청크 수: {chunks.Count}
        - 평균 품질 점수: {chunks.Average(c => c.QualityScore):F2}
        - 총 문자 수: {chunks.Sum(c => c.Content.Length):N0}
        - 평균 청크 크기: {chunks.Average(c => c.Content.Length):F0} 문자

        환경 설정:
        - OpenAI API Key: {(hasOpenAI ? "✅ 설정됨" : "❌ 미설정")}
        - 모델: {Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "N/A"}
        - 임베딩 모델: {Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_MODEL") ?? "N/A"}

        파일 출력:
        - 청크 상세 데이터: {chunkFile}
        - 실행 로그: output/test_execution_log.txt
        """;

    var reportFile = $"output/test_report_openai_{timestamp}.md";
    await File.WriteAllTextAsync(reportFile, summaryReport);

    Console.WriteLine($"\n📁 Results saved to:");
    Console.WriteLine($"  - Chunks: {chunkFile}");
    Console.WriteLine($"  - Report: {reportFile}");

Console.WriteLine("\n✨ Test completed successfully!");

// Run Phase 15 improved test
Console.WriteLine("\n" + new string('=', 70));
Console.WriteLine("🚀 Running Phase 15 IMPROVED Test (Enhanced Chunking)...");
Console.WriteLine(new string('=', 70));

await ImprovedTestProgram.RunImprovedTest();

// Run Force Smart test for comparison
Console.WriteLine("\n" + new string('=', 70));
Console.WriteLine("⚡ Running FORCE SMART Test (Direct Strategy)...");
Console.WriteLine(new string('=', 70));

await ForceSmartTestProgram.RunForceSmartTest();

// Run Force Small Chunk test for final verification
Console.WriteLine("\n" + new string('=', 70));
Console.WriteLine("🔥 Running FORCE SMALL CHUNK Test (Final Verification)...");
Console.WriteLine(new string('=', 70));

await ForceSmallChunkTestProgram.RunForceSmallChunkTest();

// Run additional Mock-only test for comparison
Console.WriteLine("\n" + new string('=', 60));
Console.WriteLine("🔄 Running Mock Service Comparison Test...");
Console.WriteLine(new string('=', 60));

await MockTestProgram.RunMockTest();

return 0;