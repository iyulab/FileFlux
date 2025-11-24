using FileFlux;
using FileFlux.CLI.Output;
using FileFlux.CLI.Services;
using FileFlux.Domain;
using FileFlux.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace FileFlux.CLI.Commands;

/// <summary>
/// Chunk command - intelligent chunking with optional AI enrichment
/// </summary>
public class ChunkCommand : Command
{
    public ChunkCommand() : base("chunk", "Chunk document with intelligent strategies")
    {
        var inputArg = new Argument<string>("input")
        {
            Description = "Input file path"
        };

        var outputOpt = new Option<string>("--output", "-o")
        {
            Description = "Output file path (default: input.chunk.md)"
        };

        var formatOpt = new Option<string>("--format", "-f")
        {
            Description = "Output format (md, json, jsonl)",
            DefaultValueFactory = _ => "md"
        };

        var strategyOpt = new Option<string>("--strategy", "-s")
        {
            Description = "Chunking strategy: Auto, Smart, Intelligent, Semantic, Paragraph, FixedSize (default: Auto)",
            DefaultValueFactory = _ => "Auto"
        };

        var maxSizeOpt = new Option<int>("--max-size", "-m")
        {
            Description = "Maximum chunk size in tokens (default: 512)",
            DefaultValueFactory = _ => 512
        };

        var overlapOpt = new Option<int>("--overlap", "-l")
        {
            Description = "Overlap size between chunks in tokens (default: 64)",
            DefaultValueFactory = _ => 64
        };

        var aiOpt = new Option<bool>("--ai", "-a")
        {
            Description = "Enable AI metadata enrichment (requires OPENAI_API_KEY or ANTHROPIC_API_KEY)"
        };

        var quietOpt = new Option<bool>("--quiet", "-q")
        {
            Description = "Minimal output"
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
        Options.Add(strategyOpt);
        Options.Add(maxSizeOpt);
        Options.Add(overlapOpt);
        Options.Add(aiOpt);
        Options.Add(quietOpt);
        Options.Add(noExtractImagesOpt);
        Options.Add(minImageSizeOpt);
        Options.Add(minImageDimensionOpt);
        Options.Add(verboseOpt);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var input = parseResult.GetValue(inputArg);
            var output = parseResult.GetValue(outputOpt);
            var format = parseResult.GetValue(formatOpt);
            var strategy = parseResult.GetValue(strategyOpt);
            var maxSize = parseResult.GetValue(maxSizeOpt);
            var overlap = parseResult.GetValue(overlapOpt);
            var enableAI = parseResult.GetValue(aiOpt);
            var quiet = parseResult.GetValue(quietOpt);
            var noExtractImages = parseResult.GetValue(noExtractImagesOpt);
            var minImageSize = parseResult.GetValue(minImageSizeOpt);
            var minImageDimension = parseResult.GetValue(minImageDimensionOpt);
            var verbose = parseResult.GetValue(verboseOpt);

            if (input != null)
            {
                // Extract images by default (invert the flag)
                var extractImages = !noExtractImages;
                await ExecuteAsync(input, output, format, strategy, maxSize, overlap, enableAI, quiet,
                    extractImages, minImageSize, minImageDimension, verbose, cancellationToken);
            }
        });
    }

    private static async Task ExecuteAsync(
        string input,
        string? output,
        string? format,
        string? strategy,
        int maxSize,
        int overlap,
        bool enableAI,
        bool quiet,
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

        // Determine output path and format
        format ??= "md";
        strategy ??= "Auto";

        // Use chunked output writer for directory-based output
        var writer = new ChunkedOutputWriter(format);

        // Output directory: input.chunks/
        output ??= $"{input}.chunks";

        // Check AI provider if enrichment requested
        var config = new CliEnvironmentConfig();
        var factory = new AIProviderFactory(config, enableVision: extractImages && enableAI);

        if (enableAI && !factory.HasAIProvider())
        {
            AnsiConsole.MarkupLine("[yellow]Warning:[/] AI enabled but no provider configured");
            AnsiConsole.MarkupLine("[yellow]→[/] Set OPENAI_API_KEY or ANTHROPIC_API_KEY environment variable");
            enableAI = false;
        }

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
            AnsiConsole.MarkupLine($"[blue]FileFlux CLI - Chunk[/]");
            AnsiConsole.MarkupLine($"  Input:    {Markup.Escape(input)}");
            AnsiConsole.MarkupLine($"  Output:   {Markup.Escape(output)}");
            AnsiConsole.MarkupLine($"  Format:   {format}");
            AnsiConsole.MarkupLine($"  Strategy: {strategy}");
            AnsiConsole.MarkupLine($"  Max size: {maxSize} tokens");
            AnsiConsole.MarkupLine($"  Overlap:  {overlap} tokens");
            AnsiConsole.MarkupLine($"  AI:       {(enableAI ? $"Enabled ({factory.GetProviderStatus()})" : "Disabled")}");
            if (extractImages)
                AnsiConsole.MarkupLine($"  Images:   [green]Extracting[/] to {Markup.Escape(imagesDir ?? "N/A")}");
            AnsiConsole.WriteLine();
        }

        try
        {
            // Phase 1: Extract and process images (shared with extract command)
            var extractResult = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Extracting document...", async ctx =>
                {
                    return await ExtractCommand.ExtractDocumentAsync(
                        input, imagesDir, enableAI, extractImages,
                        minImageSize, minImageDimension, verbose, ctx, cancellationToken);
                });

            // Phase 2: Chunk the processed content
            var chunks = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Chunking document...", async ctx =>
                {
                    // Setup services
                    var services = new ServiceCollection();

                    // Add AI provider if enrichment enabled
                    if (enableAI)
                    {
                        factory.ConfigureServices(services);
                    }

                    services.AddFileFlux();
                    using var provider = services.BuildServiceProvider();
                    var processor = provider.GetRequiredService<IDocumentProcessor>();

                    // Configure chunking options
                    var options = new ChunkingOptions
                    {
                        Strategy = strategy,
                        MaxChunkSize = maxSize,
                        OverlapSize = overlap
                    };

                    // Enable metadata enrichment if AI available
                    if (enableAI)
                    {
                        options.CustomProperties["enableMetadataEnrichment"] = true;
                        options.CustomProperties["metadataSchema"] = "General";
                    }

                    // Update ParsedContent with processed text (images replaced with file paths)
                    extractResult.ParsedContent.Text = extractResult.ProcessedText;

                    // Chunk the processed content (with images already handled)
                    return await processor.ChunkAsync(extractResult.ParsedContent, options, cancellationToken);
                });

            // Write output
            var chunkList = chunks.ToList();
            await writer.WriteAsync(chunkList, output, cancellationToken);

            // Write info file for chunked output
            var info = new ProcessingInfo
            {
                Command = "chunk",
                Format = format,
                Strategy = strategy,
                MaxChunkSize = maxSize,
                OverlapSize = overlap,
                AIProvider = enableAI ? factory.GetProviderStatus() : null,
                EnrichmentEnabled = enableAI
            };
            await ProcessingInfoWriter.WriteChunkedInfoAsync(output, input, chunkList, info, cancellationToken);

            if (!quiet)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Created {chunkList.Count} chunks");
                AnsiConsole.MarkupLine($"[green]✓[/] Saved to: {Markup.Escape(output)}/");
                AnsiConsole.MarkupLine($"[green]✓[/] Info file: {Markup.Escape(Path.Combine(output, "info.json"))}");

                // Show summary
                var totalChars = chunkList.Sum(c => c.Content.Length);
                var avgChunkSize = totalChars / chunkList.Count;

                var table = new Table();
                table.AddColumn("Metric");
                table.AddColumn("Value");
                table.AddRow("Total chunks", chunkList.Count.ToString());
                table.AddRow("Total characters", totalChars.ToString("N0"));
                table.AddRow("Average chunk size", avgChunkSize.ToString("N0"));
                table.AddRow("Min chunk size", chunkList.Min(c => c.Content.Length).ToString("N0"));
                table.AddRow("Max chunk size", chunkList.Max(c => c.Content.Length).ToString("N0"));

                if (extractResult.Images.Count > 0)
                    table.AddRow("Images extracted", extractResult.Images.Count.ToString());

                if (extractResult.SkippedImageCount > 0)
                    table.AddRow("Images skipped", extractResult.SkippedImageCount.ToString());

                if (enableAI)
                {
                    var enrichedCount = chunkList.Count(c => c.Metadata.CustomProperties.ContainsKey("enriched_topics"));
                    table.AddRow("Enriched chunks", enrichedCount.ToString());

                    if (extractResult.Images.Any(i => !string.IsNullOrEmpty(i.AIDescription)))
                        table.AddRow("AI analyzed images", extractResult.Images.Count(i => !string.IsNullOrEmpty(i.AIDescription)).ToString());
                }

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
}
