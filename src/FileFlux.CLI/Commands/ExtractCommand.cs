using FileFlux;
using FileFlux.CLI.Output;
using FileFlux.CLI.Services;
using FileFlux.Domain;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text;
using System.Text.Json;

namespace FileFlux.CLI.Commands;

/// <summary>
/// Extract command - extract raw text/content from documents without chunking
/// </summary>
public class ExtractCommand : Command
{
    public ExtractCommand() : base("extract", "Extract raw text and content from document")
    {
        var inputArg = new Argument<string>("input")
        {
            Description = "Input file path"
        };

        var outputOpt = new Option<string>("--output", "-o")
        {
            Description = "Output file path (default: input.extract.md)"
        };

        var formatOpt = new Option<string>("--format", "-f")
        {
            Description = "Output format (md, json)",
            DefaultValueFactory = _ => "md"
        };

        var quietOpt = new Option<bool>("--quiet", "-q")
        {
            Description = "Minimal output"
        };

        var aiOpt = new Option<bool>("--ai", "-a")
        {
            Description = "Enable AI for image analysis (requires OPENAI_API_KEY or ANTHROPIC_API_KEY)"
        };

        var noExtractImagesOpt = new Option<bool>("--no-extract-images")
        {
            Description = "Keep base64 images in content instead of extracting to files"
        };

        var minImageSizeOpt = new Option<int>("--min-image-size")
        {
            Description = "Minimum image file size in bytes (default: 5000)",
            DefaultValueFactory = _ => 5000
        };

        var minImageDimensionOpt = new Option<int>("--min-image-dimension")
        {
            Description = "Minimum image dimension in pixels (default: 100)",
            DefaultValueFactory = _ => 100
        };

        var verboseOpt = new Option<bool>("--verbose", "-v")
        {
            Description = "Show detailed processing information"
        };

        Arguments.Add(inputArg);
        Options.Add(outputOpt);
        Options.Add(formatOpt);
        Options.Add(quietOpt);
        Options.Add(aiOpt);
        Options.Add(noExtractImagesOpt);
        Options.Add(minImageSizeOpt);
        Options.Add(minImageDimensionOpt);
        Options.Add(verboseOpt);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var input = parseResult.GetValue(inputArg);
            var output = parseResult.GetValue(outputOpt);
            var format = parseResult.GetValue(formatOpt);
            var quiet = parseResult.GetValue(quietOpt);
            var enableAI = parseResult.GetValue(aiOpt);
            var noExtractImages = parseResult.GetValue(noExtractImagesOpt);
            var minImageSize = parseResult.GetValue(minImageSizeOpt);
            var minImageDimension = parseResult.GetValue(minImageDimensionOpt);
            var verbose = parseResult.GetValue(verboseOpt);

            if (input != null)
            {
                // Extract images by default (invert the flag)
                var extractImages = !noExtractImages;
                await ExecuteAsync(input, output, format, quiet, enableAI, extractImages,
                    minImageSize, minImageDimension, verbose, cancellationToken);
            }
        });
    }

    private static async Task ExecuteAsync(
        string input,
        string? output,
        string? format,
        bool quiet,
        bool enableAI,
        bool extractImages,
        int minImageSize,
        int minImageDimension,
        bool verbose,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(input))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] File not found: {Markup.Escape(input)}");
            return;
        }

        format ??= "md";
        var extension = format == "json" ? ".json" : ".md";
        output ??= $"{input}.extract{extension}";

        // Images directory
        string? imagesDir = null;
        if (extractImages)
        {
            var baseName = Path.GetFileNameWithoutExtension(input);
            var dir = Path.GetDirectoryName(input) ?? ".";
            imagesDir = Path.Combine(dir, $"{baseName}.images");
        }

        if (!quiet)
        {
            AnsiConsole.MarkupLine($"[blue]FileFlux CLI - Extract[/]");
            AnsiConsole.MarkupLine($"  Input:  {Markup.Escape(input)}");
            AnsiConsole.MarkupLine($"  Output: {Markup.Escape(output)}");
            AnsiConsole.MarkupLine($"  Format: {format}");
            if (enableAI)
                AnsiConsole.MarkupLine($"  AI:     [green]Enabled[/]");
            if (extractImages)
                AnsiConsole.MarkupLine($"  Images: [green]Extracting[/] to {Markup.Escape(imagesDir ?? "N/A")}");
            AnsiConsole.WriteLine();
        }

        try
        {
            var extractResult = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Extracting document...", async ctx =>
                {
                    return await ExtractDocumentAsync(
                        input, imagesDir, enableAI, extractImages,
                        minImageSize, minImageDimension, verbose, ctx, cancellationToken);
                });

            // Write output
            await WriteOutputAsync(extractResult, output, format, cancellationToken);

            // Write info file
            await WriteInfoAsync(output, input, extractResult, format, cancellationToken);

            if (!quiet)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Extracted successfully");
                AnsiConsole.MarkupLine($"[green]✓[/] Saved to: {Markup.Escape(output)}");
                AnsiConsole.MarkupLine($"[green]✓[/] Info file: {Markup.Escape(GetInfoPath(output))}");

                // Summary table
                var table = new Table();
                table.AddColumn("Metric");
                table.AddColumn("Value");
                table.AddRow("Total characters", extractResult.ProcessedText.Length.ToString("N0"));
                table.AddRow("Total words", extractResult.ParsedContent.Metadata.WordCount.ToString("N0"));

                if (extractResult.ParsedContent.Metadata.PageCount > 0)
                    table.AddRow("Pages", extractResult.ParsedContent.Metadata.PageCount.ToString());

                if (!string.IsNullOrEmpty(extractResult.ParsedContent.Metadata.Language))
                    table.AddRow("Language", extractResult.ParsedContent.Metadata.Language);

                if (extractResult.Images.Count > 0)
                    table.AddRow("Images extracted", extractResult.Images.Count.ToString());

                if (extractResult.SkippedImageCount > 0)
                    table.AddRow("Images skipped", extractResult.SkippedImageCount.ToString());

                if (extractResult.Images.Any(i => !string.IsNullOrEmpty(i.AIDescription)))
                    table.AddRow("AI analyzed", extractResult.Images.Count(i => !string.IsNullOrEmpty(i.AIDescription)).ToString());

                AnsiConsole.Write(table);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            if (!quiet)
            {
                AnsiConsole.WriteException(ex);
            }
        }
    }

    /// <summary>
    /// Core extraction logic - reusable by ChunkCommand
    /// </summary>
    public static async Task<ExtractResult> ExtractDocumentAsync(
        string input,
        string? imagesDir,
        bool enableAI,
        bool extractImages,
        int minImageSize,
        int minImageDimension,
        bool verbose,
        StatusContext? ctx,
        CancellationToken cancellationToken)
    {
        // Create services
        var services = new ServiceCollection();
        var config = new CliEnvironmentConfig();
        string? aiProvider = null;
        IImageToTextService? imageToTextService = null;

        if (enableAI && config.HasAnyProvider())
        {
            var factory = new AIProviderFactory(config, enableVision: true);
            ctx?.Status($"Processing with AI ({factory.GetProviderStatus()})...");
            factory.ConfigureServices(services);
            aiProvider = config.DetectProvider();
        }

        services.AddFileFlux();
        using var provider = services.BuildServiceProvider();
        var processor = provider.GetRequiredService<IDocumentProcessor>();

        // Try to get image-to-text service if AI is enabled
        if (enableAI)
        {
            imageToTextService = provider.GetService<IImageToTextService>();
        }

        // Stage 1: Extract raw content
        ctx?.Status("Extracting raw content...");
        var rawContent = await processor.ExtractAsync(input, cancellationToken);

        // Stage 2: Parse document structure
        ctx?.Status("Parsing document structure...");
        var parsedContent = await processor.ParseAsync(rawContent, null, cancellationToken);

        // Stage 3: Process images
        var processedText = parsedContent.Text;
        var images = new List<ProcessedImage>();
        var skippedCount = 0;

        if (extractImages && !string.IsNullOrEmpty(imagesDir))
        {
            ctx?.Status("Processing images...");
            var imageOptions = new ImageProcessingOptions
            {
                ExtractImages = true,
                EnableAI = enableAI,
                MinImageSize = minImageSize,
                MinImageDimension = minImageDimension
            };

            var imageProcessor = new ImageProcessor(imageOptions, verbose);
            var imageResult = await imageProcessor.ProcessImagesAsync(
                parsedContent.Text, imagesDir, imageToTextService, cancellationToken);

            processedText = imageResult.ProcessedContent;
            images = imageResult.Images;
            skippedCount = imageResult.SkippedCount;
        }
        else
        {
            // Remove base64 images from content
            processedText = ImageProcessor.RemoveBase64Images(parsedContent.Text);
        }

        return new ExtractResult
        {
            ParsedContent = parsedContent,
            ProcessedText = processedText,
            Images = images,
            SkippedImageCount = skippedCount,
            AIProvider = aiProvider,
            ImagesDirectory = imagesDir
        };
    }

    private static async Task WriteOutputAsync(ExtractResult result, string outputPath, string format, CancellationToken cancellationToken)
    {
        if (format == "json")
        {
            await WriteJsonOutputAsync(result, outputPath, cancellationToken);
        }
        else
        {
            await WriteMarkdownOutputAsync(result, outputPath, cancellationToken);
        }
    }

    private static async Task WriteMarkdownOutputAsync(ExtractResult result, string outputPath, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var metadata = result.ParsedContent.Metadata;

        // YAML frontmatter
        sb.AppendLine("---");
        if (!string.IsNullOrEmpty(metadata.Title))
            sb.AppendLine($"title: \"{EscapeYaml(metadata.Title)}\"");

        sb.AppendLine($"source: \"{EscapeYaml(metadata.FileName)}\"");

        if (!string.IsNullOrEmpty(metadata.Author))
            sb.AppendLine($"author: \"{EscapeYaml(metadata.Author)}\"");

        if (metadata.PageCount > 0)
            sb.AppendLine($"pages: {metadata.PageCount}");

        if (metadata.WordCount > 0)
            sb.AppendLine($"words: {metadata.WordCount}");

        if (!string.IsNullOrEmpty(metadata.Language))
            sb.AppendLine($"language: {metadata.Language}");

        sb.AppendLine($"file_type: {metadata.FileType}");
        sb.AppendLine($"file_size: {metadata.FileSize}");
        sb.AppendLine($"processed_at: {metadata.ProcessedAt:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine("---");
        sb.AppendLine();

        sb.AppendLine(result.ProcessedText);

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8, cancellationToken);
    }

    private static async Task WriteJsonOutputAsync(ExtractResult result, string outputPath, CancellationToken cancellationToken)
    {
        var metadata = result.ParsedContent.Metadata;
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var data = new
        {
            metadata = new
            {
                title = metadata.Title,
                source = metadata.FileName,
                author = metadata.Author,
                pages = metadata.PageCount,
                words = metadata.WordCount,
                language = metadata.Language,
                fileType = metadata.FileType,
                fileSize = metadata.FileSize,
                processedAt = metadata.ProcessedAt.ToString("o")
            },
            content = result.ProcessedText,
            statistics = new
            {
                totalCharacters = result.ProcessedText.Length,
                totalWords = CountWords(result.ProcessedText)
            }
        };

        var json = JsonSerializer.Serialize(data, options);
        await File.WriteAllTextAsync(outputPath, json, Encoding.UTF8, cancellationToken);
    }

    private static async Task WriteInfoAsync(string outputPath, string inputPath, ExtractResult result, string format, CancellationToken cancellationToken)
    {
        var infoPath = GetInfoPath(outputPath);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var data = new
        {
            command = "extract",
            input = Path.GetFileName(inputPath),
            output = Path.GetFileName(outputPath),
            format,
            statistics = new
            {
                totalCharacters = result.ProcessedText.Length,
                totalWords = result.ParsedContent.Metadata.WordCount,
                pageCount = result.ParsedContent.Metadata.PageCount,
                language = result.ParsedContent.Metadata.Language,
                imagesExtracted = result.Images.Count,
                imagesSkipped = result.SkippedImageCount
            },
            aiAnalysis = result.AIProvider != null ? new
            {
                provider = result.AIProvider,
                imagesAnalyzed = result.Images.Count(i => !string.IsNullOrEmpty(i.AIDescription)),
                images = result.Images.Select(i => new
                {
                    fileName = i.FileName,
                    dimensions = $"{i.Width}x{i.Height}",
                    fileSize = i.FileSize,
                    description = i.AIDescription,
                    error = i.AIError
                }).ToArray()
            } : null,
            processedAt = result.ParsedContent.Metadata.ProcessedAt.ToString("o")
        };

        var json = JsonSerializer.Serialize(data, options);
        await File.WriteAllTextAsync(infoPath, json, Encoding.UTF8, cancellationToken);
    }

    private static string GetInfoPath(string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(dir, $"{name}.info.json");
    }

    private static string EscapeYaml(string value)
    {
        return value.Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
