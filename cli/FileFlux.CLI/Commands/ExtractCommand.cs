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
            Description = "Output directory path (default: <input>_output/)"
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
                await ExecuteAsync(input, output, format, quiet, enableAI, !noExtractImages,
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

        // Setup services
        var services = new ServiceCollection();
        var config = new CliEnvironmentConfig();
        IImageToTextService? imageToTextService = null;
        string? aiProvider = null;

        if (enableAI && config.HasAnyProvider())
        {
            var factory = new AIProviderFactory(config, enableVision: true, verbose: verbose);
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
        await using var provider = services.BuildServiceProvider();
        var processor = provider.GetRequiredService<FluxDocumentProcessor>();

        if (enableAI)
        {
            imageToTextService = provider.GetService<IImageToTextService>();
        }

        // Configure output options
        var outputOptions = new OutputOptions
        {
            OutputDirectory = output,
            Format = format ?? "md",
            ExtractImages = extractImages,
            MinImageSize = minImageSize,
            MinImageDimension = minImageDimension,
            EnableAI = enableAI
        };

        // Get output directories
        var dirs = OutputOptions.GetOutputDirectories(input, output);

        if (!quiet)
        {
            AnsiConsole.MarkupLine($"[blue]FileFlux CLI - Extract[/]");
            AnsiConsole.MarkupLine($"  Input:  {Markup.Escape(input)}");
            AnsiConsole.MarkupLine($"  Output: {Markup.Escape(dirs.Base)}/");
            AnsiConsole.MarkupLine($"  Format: {format ?? "md"}");
            AnsiConsole.MarkupLine($"  AI:     {(enableAI ? $"[green]Enabled[/] ({aiProvider})" : "Disabled")}");
            if (extractImages)
                AnsiConsole.MarkupLine($"  Images: [green]Extracting[/]");
            AnsiConsole.WriteLine();
        }

        // Override output options with new directory structure
        outputOptions.OutputDirectory = dirs.Extract;
        outputOptions.ImagesDirectory = dirs.Images;

        try
        {
            var result = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Extracting document...", async ctx =>
                {
                    return await processor.ExtractToDirectoryAsync(
                        input, outputOptions, imageToTextService, cancellationToken);
                });

            if (!quiet)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Extracted successfully");
                AnsiConsole.MarkupLine($"[green]✓[/] Output: {Markup.Escape(result.OutputDirectory!)}/");

                // Summary table
                var table = new Table();
                table.AddColumn("Metric");
                table.AddColumn("Value");
                table.AddRow("Total characters", result.ProcessedText.Length.ToString("N0"));
                table.AddRow("Total words", result.ParsedContent.Metadata.WordCount.ToString("N0"));

                if (result.ParsedContent.Metadata.PageCount > 0)
                    table.AddRow("Pages", result.ParsedContent.Metadata.PageCount.ToString());

                if (!string.IsNullOrEmpty(result.ParsedContent.Metadata.Language))
                    table.AddRow("Language", result.ParsedContent.Metadata.Language);

                if (result.Images.Count > 0)
                    table.AddRow("Images extracted", result.Images.Count.ToString());

                if (result.SkippedImageCount > 0)
                    table.AddRow("Images skipped", result.SkippedImageCount.ToString());

                if (result.Images.Any(i => !string.IsNullOrEmpty(i.AIDescription)))
                    table.AddRow("AI analyzed", result.Images.Count(i => !string.IsNullOrEmpty(i.AIDescription)).ToString());

                AnsiConsole.Write(table);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            if (verbose)
            {
                AnsiConsole.WriteException(ex);
            }
        }
    }
}
