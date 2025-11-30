using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileFlux;
using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FileFlux.ExtractionSample;

class Program
{
    static async Task Main(string[] args)
    {
        // Setup DI container
        var services = new ServiceCollection()
            .AddLogging(builder => builder.AddConsole())
            .AddScoped<IDocumentReader, PdfDocumentReader>()
            .BuildServiceProvider();

        // Get PDF reader
        var pdfReader = services.GetRequiredService<IDocumentReader>();

        // Target PDF path
        var pdfPath = @"D:\data\FileFlux\tests\test-pdf\oai_gpt-oss_model_card.pdf";

        try
        {
            Console.WriteLine($"Starting PDF extraction from: {pdfPath}");
            Console.WriteLine(new string('=', 60));
            
            // Extract PDF document
            var document = await pdfReader.ExtractAsync(pdfPath, CancellationToken.None);
            
            Console.WriteLine($"File Name: {document.FileInfo.FileName}");
            Console.WriteLine($"File Extension: {document.FileInfo.FileExtension}");
            Console.WriteLine($"File Size: {document.FileInfo.FileSize:N0} bytes");
            Console.WriteLine($"Created At: {document.FileInfo.CreatedAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Modified At: {document.FileInfo.ModifiedAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Extracted At: {document.FileInfo.ExtractedAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Reader Type: {document.FileInfo.ReaderType}");
            Console.WriteLine();
            
            if (document.ExtractionWarnings.Count > 0)
            {
                Console.WriteLine("Extraction Warnings:");
                foreach (var warning in document.ExtractionWarnings)
                {
                    Console.WriteLine($"- {warning}");
                }
                Console.WriteLine();
            }
            
            Console.WriteLine("Content Preview (first 1000 characters):");
            Console.WriteLine(new string('-', 60));
            
            var content = document.Text;
            var preview = content.Length > 1000 ? content[..1000] + "..." : content;
            Console.WriteLine(preview);
            
            Console.WriteLine();
            Console.WriteLine($"Total content length: {content.Length:N0} characters");
            
            // Save extracted content to file
            var originalFileName = Path.GetFileNameWithoutExtension(pdfPath);
            var outputFileName = $"{originalFileName}_extract.txt";
            var outputPath = Path.Combine(Path.GetDirectoryName(pdfPath)!, outputFileName);
            
            await File.WriteAllTextAsync(outputPath, content, CancellationToken.None);
                        
            Console.WriteLine();
            Console.WriteLine($"Extracted content saved to: {outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting PDF: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
        
        Console.WriteLine("\nExtraction completed.");
    }
}