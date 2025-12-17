using FileFlux.CLI.Services;
using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;

namespace FileFlux.CLI.Commands;

/// <summary>
/// Refine command - clean and enhance extracted content before chunking
/// </summary>
public class RefineCommand : Command
{
    public RefineCommand() : base("refine", "Refine/clean extracted content (remove headers, footers, fix structure)")
    {
        var inputArg = new Argument<string>("input")
        {
            Description = "Input file path (document or extracted JSON)"
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

        var noCleanWhitespaceOpt = new Option<bool>("--no-clean-whitespace")
        {
            Description = "Skip whitespace cleaning"
        };

        var noRemoveHeadersOpt = new Option<bool>("--no-remove-headers")
        {
            Description = "Keep headers and footers"
        };

        var noRemovePageNumbersOpt = new Option<bool>("--no-remove-page-numbers")
        {
            Description = "Keep page numbers"
        };

        var noRestructureOpt = new Option<bool>("--no-restructure")
        {
            Description = "Skip heading restructuring"
        };

        var aiOpt = new Option<bool>("--ai", "-a")
        {
            Description = "Enable AI for OCR correction and descriptions"
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

        var quietOpt = new Option<bool>("--quiet", "-q")
        {
            Description = "Minimal output"
        };

        var verboseOpt = new Option<bool>("--verbose", "-v")
        {
            Description = "Show detailed processing information"
        };

        Arguments.Add(inputArg);
        Options.Add(outputOpt);
        Options.Add(formatOpt);
        Options.Add(noCleanWhitespaceOpt);
        Options.Add(noRemoveHeadersOpt);
        Options.Add(noRemovePageNumbersOpt);
        Options.Add(noRestructureOpt);
        Options.Add(aiOpt);
        Options.Add(noExtractImagesOpt);
        Options.Add(minImageSizeOpt);
        Options.Add(minImageDimensionOpt);
        Options.Add(quietOpt);
        Options.Add(verboseOpt);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var input = parseResult.GetValue(inputArg);
            var output = parseResult.GetValue(outputOpt);
            var format = parseResult.GetValue(formatOpt);
            var noCleanWhitespace = parseResult.GetValue(noCleanWhitespaceOpt);
            var noRemoveHeaders = parseResult.GetValue(noRemoveHeadersOpt);
            var noRemovePageNumbers = parseResult.GetValue(noRemovePageNumbersOpt);
            var noRestructure = parseResult.GetValue(noRestructureOpt);
            var enableAI = parseResult.GetValue(aiOpt);
            var noExtractImages = parseResult.GetValue(noExtractImagesOpt);
            var minImageSize = parseResult.GetValue(minImageSizeOpt);
            var minImageDimension = parseResult.GetValue(minImageDimensionOpt);
            var quiet = parseResult.GetValue(quietOpt);
            var verbose = parseResult.GetValue(verboseOpt);

            if (input != null)
            {
                await ExecuteAsync(input, output, format, !noCleanWhitespace, !noRemoveHeaders,
                    !noRemovePageNumbers, !noRestructure, enableAI, !noExtractImages,
                    minImageSize, minImageDimension, quiet, verbose, cancellationToken);
            }
        });
    }

    private static async Task ExecuteAsync(
        string input,
        string? output,
        string? format,
        bool cleanWhitespace,
        bool removeHeaders,
        bool removePageNumbers,
        bool restructure,
        bool enableAI,
        bool extractImages,
        int minImageSize,
        int minImageDimension,
        bool quiet,
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

        format ??= "md";

        // Get output directories
        var dirs = OutputOptions.GetOutputDirectories(input, output);
        var outputFile = Path.Combine(dirs.Refine, $"refined.{format}");

        // Configure extract output options
        var extractOptions = new OutputOptions
        {
            OutputDirectory = dirs.Extract,
            ImagesDirectory = dirs.Images,
            Format = format,
            ExtractImages = extractImages,
            MinImageSize = minImageSize,
            MinImageDimension = minImageDimension,
            EnableAI = enableAI,
            Verbose = verbose
        };

        var refineOptions = new RefiningOptions
        {
            CleanWhitespace = cleanWhitespace,
            RemoveHeadersFooters = removeHeaders,
            RemovePageNumbers = removePageNumbers,
            RestructureHeadings = restructure,
            UseAIForOCRCorrection = enableAI,
            UseAIForDescriptions = enableAI
        };

        if (!quiet)
        {
            AnsiConsole.MarkupLine($"[blue]FileFlux CLI - Refine[/]");
            AnsiConsole.MarkupLine($"  Input:      {Markup.Escape(input)}");
            AnsiConsole.MarkupLine($"  Output:     {Markup.Escape(dirs.Base)}/");
            AnsiConsole.MarkupLine($"  Format:     {format}");
            if (extractImages)
                AnsiConsole.MarkupLine($"  Images:     [green]Extracting[/]");
            AnsiConsole.MarkupLine($"  Options:    {(cleanWhitespace ? "clean" : "")} {(removeHeaders ? "headers" : "")} {(removePageNumbers ? "pages" : "")} {(restructure ? "restructure" : "")}".Trim());
            AnsiConsole.WriteLine();

            // Display detailed model information if AI is enabled
            if (enableAI)
            {
                var factory = new AIProviderFactory(config, enableVision: true, verbose: verbose);
                factory.DisplayModelInfo();
            }
            else
            {
                AnsiConsole.MarkupLine("[dim]AI:[/] Disabled");
                AnsiConsole.WriteLine();
            }
        }

        try
        {
            ExtractionResult? extractionResult = null;
            ParsedContent parsedContent;
            int originalLength;

            // Check if input is already extracted JSON or a document file
            if (input.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var json = await File.ReadAllTextAsync(input, cancellationToken);
                var extracted = JsonSerializer.Deserialize<ExtractedContent>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (extracted?.Text == null)
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] Invalid extracted content JSON");
                    return;
                }

                originalLength = extracted.Text.Length;
                parsedContent = new ParsedContent
                {
                    Text = extracted.Text,
                    Metadata = new DocumentMetadata { FileName = Path.GetFileName(input) },
                    Structure = new DocumentStructure(),
                    Info = new ParsingInfo()
                };
            }
            else
            {
                // Step 1: Extract document (implicitly performs extract stage)
                extractionResult = await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Extracting document...", async ctx =>
                    {
                        return await processor.ExtractToDirectoryAsync(
                            input, extractOptions, imageToTextService, cancellationToken);
                    });

                parsedContent = extractionResult.ParsedContent;
                // Use processed text (with image placeholders replaced)
                parsedContent.Text = extractionResult.ProcessedText;
                originalLength = extractionResult.ProcessedText.Length;
            }

            // Step 2: Refine the content
            var refined = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Refining content...", async ctx =>
                {
                    return await processor.RefineAsync(parsedContent, refineOptions, cancellationToken);
                });

            // Ensure output directory exists
            Directory.CreateDirectory(dirs.Refine);

            // Write output
            if (format == "json")
            {
                var result = new
                {
                    OriginalLength = originalLength,
                    RefinedLength = refined.Text.Length,
                    Reduction = $"{(1 - (double)refined.Text.Length / originalLength) * 100:F1}%",
                    Text = refined.Text,
                    Metadata = refined.Metadata
                };
                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(outputFile, json, cancellationToken);
            }
            else
            {
                await File.WriteAllTextAsync(outputFile, refined.Text, cancellationToken);
            }

            if (!quiet)
            {
                var reduction = (1 - (double)refined.Text.Length / originalLength) * 100;

                AnsiConsole.MarkupLine($"[green]✓[/] Refined successfully");
                AnsiConsole.MarkupLine($"[green]✓[/] Output: {Markup.Escape(outputFile)}");

                var table = new Table();
                table.AddColumn("Metric");
                table.AddColumn("Value");
                table.AddRow("Original length", $"{originalLength:N0} chars");
                table.AddRow("Refined length", $"{refined.Text.Length:N0} chars");
                table.AddRow("Reduction", $"{reduction:F1}%");

                if (extractionResult != null)
                {
                    if (extractionResult.Images.Count > 0)
                        table.AddRow("Images extracted", extractionResult.Images.Count.ToString());
                    if (extractionResult.SkippedImageCount > 0)
                        table.AddRow("Images skipped", extractionResult.SkippedImageCount.ToString());
                }

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

    private class ExtractedContent
    {
        public string? Text { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
}
