using FileFlux.CLI.Services;
using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace FileFlux.CLI.Commands;

/// <summary>
/// Process command - complete pipeline (extract + chunk + enrich)
/// </summary>
public class ProcessCommand : Command
{
    public ProcessCommand() : base("process", "Complete processing pipeline with extraction, chunking, and enrichment")
    {
        var inputArg = new Argument<string>("input")
        {
            Description = "Input file path"
        };

        var outputOpt = new Option<string>("--output", "-o")
        {
            Description = "Output directory path (default: input.processed/)"
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
                await ExecuteAsync(input, output, format, strategy, maxSize, overlap, enableAI, quiet,
                    !noExtractImages, minImageSize, minImageDimension, verbose, cancellationToken);
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

        // Setup services
        var services = new ServiceCollection();
        var config = new CliEnvironmentConfig();
        var factory = new AIProviderFactory(config, enableVision: extractImages && enableAI, verbose: verbose);
        string? aiProvider = null;

        if (enableAI && factory.HasAIProvider())
        {
            factory.ConfigureServices(services);
            aiProvider = config.DetectProvider();
        }
        else if (enableAI)
        {
            if (!quiet)
            {
                AnsiConsole.MarkupLine("[yellow]Warning:[/] AI enabled but no provider configured");
                AnsiConsole.MarkupLine("[yellow]→[/] Set OPENAI_API_KEY or ANTHROPIC_API_KEY environment variable");
            }
            enableAI = false;
        }

        services.AddFileFlux();
        using var provider = services.BuildServiceProvider();
        var processor = (FluxDocumentProcessor)provider.GetRequiredService<IDocumentProcessor>();
        var imageToTextService = enableAI ? provider.GetService<IImageToTextService>() : null;

        // Configure options
        format ??= "md";
        strategy ??= "Auto";

        var chunkingOptions = new ChunkingOptions
        {
            Strategy = strategy,
            MaxChunkSize = maxSize,
            OverlapSize = overlap
        };

        if (enableAI)
        {
            chunkingOptions.CustomProperties["enableMetadataEnrichment"] = true;
            chunkingOptions.CustomProperties["metadataSchema"] = Core.MetadataSchema.General;
        }

        var outputOptions = new OutputOptions
        {
            OutputDirectory = output,
            Format = format,
            ExtractImages = extractImages,
            MinImageSize = minImageSize,
            MinImageDimension = minImageDimension,
            EnableAI = enableAI
        };

        if (!quiet)
        {
            var outputDir = output ?? OutputOptions.GetDefaultOutputDirectory(input, "processed");
            var panel = new Panel(new Markup(
                $"[bold]Input:[/] {Markup.Escape(input)}\n" +
                $"[bold]Output:[/] {Markup.Escape(outputDir)}/\n" +
                $"[bold]Strategy:[/] {strategy} (max: {maxSize}, overlap: {overlap})\n" +
                $"[bold]AI Provider:[/] {(enableAI ? aiProvider : "Disabled")}\n" +
                $"[bold]Format:[/] {format}"))
            {
                Header = new PanelHeader("[blue]FileFlux CLI - Process[/]"),
                Border = BoxBorder.Rounded
            };

            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine();
        }

        try
        {
            var result = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Processing document...", async ctx =>
                {
                    return await processor.ProcessToDirectoryAsync(
                        input, chunkingOptions, outputOptions, imageToTextService, cancellationToken);
                });

            if (!quiet)
            {
                var chunks = result.Chunks;
                AnsiConsole.MarkupLine($"\n[green]✓ Success![/] Processed document into {chunks.Length} chunks");
                AnsiConsole.MarkupLine($"[green]✓[/] Output: {Markup.Escape(result.OutputDirectory!)}/\n");

                // Summary
                var totalChars = chunks.Sum(c => c.Content.Length);
                var avgSize = chunks.Length > 0 ? totalChars / chunks.Length : 0;
                var enrichedCount = chunks.Count(c => c.Metadata.CustomProperties.Keys.Any(k => k.StartsWith("enriched_")));

                var grid = new Grid();
                grid.AddColumn();
                grid.AddColumn();

                grid.AddRow("[bold]Chunks created:[/]", chunks.Length.ToString());
                grid.AddRow("[bold]Total characters:[/]", totalChars.ToString("N0"));
                grid.AddRow("[bold]Average size:[/]", avgSize.ToString("N0"));
                grid.AddRow("[bold]Size range:[/]", chunks.Length > 0
                    ? $"{chunks.Min(c => c.Content.Length):N0} - {chunks.Max(c => c.Content.Length):N0}"
                    : "0 - 0");

                // Auto 전략인 경우 실제 최적화된 값 표시
                if (strategy.Equals("Auto", StringComparison.OrdinalIgnoreCase) && chunks.Length > 0)
                {
                    var firstChunk = chunks[0];
                    if (firstChunk.Props.TryGetValue("OptimizedMaxChunkSize", out var optimizedMax) &&
                        firstChunk.Props.TryGetValue("OptimizedOverlapSize", out var optimizedOverlap))
                    {
                        var selectedStrategy = firstChunk.Props.TryGetValue("AutoSelectedStrategy", out var autoStrategy) ? autoStrategy?.ToString() : null;
                        grid.AddRow("[bold]Optimized strategy:[/]", selectedStrategy ?? "Unknown");
                        grid.AddRow("[bold]Optimized max size:[/]", optimizedMax?.ToString() ?? maxSize.ToString());
                        grid.AddRow("[bold]Optimized overlap:[/]", optimizedOverlap?.ToString() ?? overlap.ToString());
                    }
                }

                if (result.Extraction.Images.Count > 0)
                    grid.AddRow("[bold]Images extracted:[/]", result.Extraction.Images.Count.ToString());

                if (enableAI && enrichedCount > 0)
                    grid.AddRow("[bold]Enriched chunks:[/]", $"{enrichedCount} ({enrichedCount * 100 / chunks.Length}%)");

                var summaryPanel = new Panel(grid)
                {
                    Header = new PanelHeader("[yellow]Summary[/]"),
                    Border = BoxBorder.Rounded
                };

                AnsiConsole.Write(summaryPanel);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]✗ Error:[/] {ex.Message}");
            if (verbose)
            {
                AnsiConsole.WriteException(ex);
            }
        }
    }
}
