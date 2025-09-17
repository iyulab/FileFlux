using FileFlux;
using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Tests.Mocks;
using FileFlux.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestConsoleApp;

public class MockTestProgram
{
    public static async Task<int> RunMockTest()
    {
        // Simple PDF test console app - Mock version
        var path = @"D:\test-data\채변프로그램 변경[25.02.03].pdf";

        Console.WriteLine("FileFlux PDF Processing Test (Mock Services ONLY)");
        Console.WriteLine($"Processing: {path}");
        Console.WriteLine(new string('=', 50));

        // Setup DI container with FileFlux services
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());

        // Register FileFlux services
        services.AddFileFlux();

        Console.WriteLine("\n⚠️ Using Mock services (No OpenAI configuration)");
        services.AddSingleton<ITextCompletionService, MockTextCompletionService>();
        services.AddSingleton<IImageToTextService, MockImageToTextService>();

        var serviceProvider = services.BuildServiceProvider();
        var processingService = serviceProvider.GetRequiredService<IDocumentProcessor>();

        try
        {
            Console.WriteLine("Starting document processing...\n");

            var chunks = new List<DocumentChunk>();

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
            Console.WriteLine($"🤖 AI Service: Mock Service ONLY");

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
            return 1;
        }

        Console.WriteLine("\n✨ Mock test completed successfully!");
        return 0;
    }
}