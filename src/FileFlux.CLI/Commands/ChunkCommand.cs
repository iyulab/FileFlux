using FileFlux.CLI.Services;
using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using FluxImprover;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace FileFlux.CLI.Commands;

/// <summary>
/// Chunk command - intelligent chunking with optional refine and enrich stages
/// </summary>
public class ChunkCommand : Command
{
    public ChunkCommand() : base("chunk", "Chunk document with intelligent strategies (optional: --refine, --enrich)")
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
            Description = "Output format (md, json, jsonl)",
            DefaultValueFactory = _ => "md"
        };

        var strategyOpt = new Option<string>("--strategy", "-s")
        {
            Description = "Chunking strategy: Auto, Sentence, Paragraph, Token, Semantic, Hierarchical (default: Auto)",
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

        // Pipeline stage flags
        var refineOpt = new Option<bool>("--refine", "-r")
        {
            Description = "Enable refine stage before chunking (clean headers, whitespace, restructure)"
        };

        var enrichOpt = new Option<bool>("--enrich", "-e")
        {
            Description = "Enable enrich stage after chunking (AI summaries/keywords, requires --ai)"
        };

        var aiOpt = new Option<bool>("--ai", "-a")
        {
            Description = "Enable AI features (enrichment, image analysis)"
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
        Options.Add(refineOpt);
        Options.Add(enrichOpt);
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
            var enableRefine = parseResult.GetValue(refineOpt);
            var enableEnrich = parseResult.GetValue(enrichOpt);
            var enableAI = parseResult.GetValue(aiOpt);
            var quiet = parseResult.GetValue(quietOpt);
            var noExtractImages = parseResult.GetValue(noExtractImagesOpt);
            var minImageSize = parseResult.GetValue(minImageSizeOpt);
            var minImageDimension = parseResult.GetValue(minImageDimensionOpt);
            var verbose = parseResult.GetValue(verboseOpt);

            if (input != null)
            {
                await ExecuteAsync(input, output, format, strategy, maxSize, overlap,
                    enableRefine, enableEnrich && enableAI, enableAI, quiet,
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
        bool enableRefine,
        bool enableEnrich,
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
        FluxImproverServices? fluxImprover = null;

        if (enableAI && factory.HasAIProvider())
        {
            factory.ConfigureServices(services);
            aiProvider = config.DetectProvider();

            if (enableEnrich)
            {
                try
                {
                    fluxImprover = factory.CreateFluxImproverServices();
                }
                catch (Exception ex)
                {
                    if (!quiet)
                    {
                        AnsiConsole.MarkupLine($"[yellow]Warning:[/] FluxImprover init failed: {ex.Message}");
                    }
                    enableEnrich = false;
                }
            }
        }
        else if (enableAI)
        {
            if (!quiet)
            {
                AnsiConsole.MarkupLine("[yellow]Warning:[/] AI enabled but no provider configured");
            }
            enableAI = false;
            enableEnrich = false;
        }

        services.AddFileFlux();
        using var provider = services.BuildServiceProvider();
        var processor = (FluxDocumentProcessor)provider.GetRequiredService<IDocumentProcessor>();
        var imageToTextService = enableAI ? provider.GetService<IImageToTextService>() : null;

        // Configure options
        format ??= "md";
        strategy ??= "Auto";

        // Get output directories
        var dirs = OutputOptions.GetOutputDirectories(input, output);

        // Build pipeline description
        var stages = new List<string> { "Extract" };
        if (enableRefine) stages.Add("Refine");
        stages.Add("Chunk");
        if (enableEnrich) stages.Add("Enrich");
        var pipelineDesc = string.Join(" → ", stages);

        if (!quiet)
        {
            AnsiConsole.MarkupLine($"[blue]FileFlux CLI - Chunk[/]");
            AnsiConsole.MarkupLine($"  Input:    {Markup.Escape(input)}");
            AnsiConsole.MarkupLine($"  Output:   {Markup.Escape(dirs.Base)}/");
            AnsiConsole.MarkupLine($"  Pipeline: {pipelineDesc}");
            AnsiConsole.MarkupLine($"  Strategy: {strategy}");
            AnsiConsole.MarkupLine($"  Max size: {maxSize} tokens");
            AnsiConsole.MarkupLine($"  Overlap:  {overlap} tokens");
            AnsiConsole.MarkupLine($"  AI:       {(enableAI ? $"Enabled ({aiProvider})" : "Disabled")}");
            if (extractImages)
                AnsiConsole.MarkupLine($"  Images:   [green]Extracting[/]");
            AnsiConsole.WriteLine();
        }

        try
        {
            // Create output directories
            Directory.CreateDirectory(dirs.Base);
            Directory.CreateDirectory(dirs.Extract);
            if (enableRefine) Directory.CreateDirectory(dirs.Refine);
            Directory.CreateDirectory(dirs.Chunks);
            if (enableEnrich) Directory.CreateDirectory(dirs.Enrich);
            if (extractImages) Directory.CreateDirectory(dirs.Images);

            RawContent rawContent = null!;
            ParsedContent parsedContent = null!;
            ParsedContent contentToChunk = null!;
            DocumentChunk[] chunks = null!;
            var images = new List<Domain.ProcessedImage>();
            var skippedImageCount = 0;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Processing document...", async ctx =>
                {
                    // Stage 1: Extract
                    ctx.Status("Extracting...");
                    rawContent = await processor.ExtractAsync(input, cancellationToken);

                    // Stage 2: Parse
                    ctx.Status("Parsing...");
                    parsedContent = await processor.ParseAsync(rawContent, null, cancellationToken);

                    // Process images if needed
                    if (extractImages)
                    {
                        var outputOptions = new OutputOptions
                        {
                            ExtractImages = true,
                            MinImageSize = minImageSize,
                            MinImageDimension = minImageDimension
                        };
                        var imageProcessor = new Infrastructure.Services.ImageProcessor(outputOptions);
                        var imageResult = await imageProcessor.ProcessImagesAsync(
                            parsedContent.Text, dirs.Images, imageToTextService, cancellationToken);

                        parsedContent.Text = imageResult.ProcessedContent;
                        images = imageResult.Images;
                        skippedImageCount = imageResult.SkippedCount;
                    }
                    else
                    {
                        parsedContent.Text = Infrastructure.Services.ImageProcessor.RemoveBase64Images(parsedContent.Text);
                    }

                    // Save extract result
                    await File.WriteAllTextAsync(
                        Path.Combine(dirs.Extract, "extracted.md"),
                        parsedContent.Text,
                        cancellationToken);

                    // Stage 2.5: Refine (optional)
                    if (enableRefine)
                    {
                        ctx.Status("Refining...");
                        contentToChunk = await processor.RefineAsync(parsedContent, new RefiningOptions(), cancellationToken);

                        // Save refine result
                        await File.WriteAllTextAsync(
                            Path.Combine(dirs.Refine, "refined.md"),
                            contentToChunk.Text,
                            cancellationToken);
                    }
                    else
                    {
                        contentToChunk = parsedContent;
                    }

                    // Stage 3: Chunk
                    ctx.Status("Chunking...");
                    var chunkingOptions = new ChunkingOptions
                    {
                        Strategy = strategy,
                        MaxChunkSize = maxSize,
                        OverlapSize = overlap
                    };
                    chunks = await processor.ChunkAsync(contentToChunk, chunkingOptions, cancellationToken);

                    // Stage 4: Enrich (optional)
                    if (enableEnrich && fluxImprover != null && chunks.Length > 0)
                    {
                        ctx.Status("Enriching...");
                        for (int i = 0; i < chunks.Length; i++)
                        {
                            var chunk = chunks[i];
                            var fluxChunk = new FluxImprover.Models.Chunk
                            {
                                Id = chunk.Id.ToString(),
                                Content = chunk.Content,
                                Metadata = chunk.Props
                            };

                            var enriched = await fluxImprover.ChunkEnrichment.EnrichAsync(
                                fluxChunk, null, cancellationToken);

                            if (!string.IsNullOrEmpty(enriched.Summary))
                                chunk.Props[ChunkPropsKeys.EnrichedSummary] = enriched.Summary;
                            if (enriched.Keywords?.Any() == true)
                                chunk.Props[ChunkPropsKeys.EnrichedKeywords] = enriched.Keywords;
                        }
                    }
                });

            // Write output
            var chunkingOptions = new ChunkingOptions
            {
                Strategy = strategy,
                MaxChunkSize = maxSize,
                OverlapSize = overlap
            };

            var outputOptions = new OutputOptions
            {
                OutputDirectory = dirs.Chunks,
                Format = format,
                ExtractImages = extractImages,
                MinImageSize = minImageSize,
                MinImageDimension = minImageDimension,
                EnableAI = enableAI
            };

            var extraction = new ExtractionResult
            {
                ParsedContent = contentToChunk,
                ProcessedText = contentToChunk.Text,
                Images = images,
                SkippedImageCount = skippedImageCount,
                AIProvider = enableAI ? aiProvider : null,
                ImagesDirectory = extractImages ? dirs.Images : null,
                OutputDirectory = dirs.Base
            };

            var result = new ChunkingResult
            {
                Chunks = chunks,
                Extraction = extraction,
                OutputDirectory = dirs.Chunks,
                Options = chunkingOptions
            };

            var writer = new Infrastructure.Output.FileSystemOutputWriter();
            await writer.WriteChunkingAsync(result, dirs.Chunks, outputOptions, cancellationToken);

            if (!quiet)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Created {chunks.Length} chunks");
                AnsiConsole.MarkupLine($"[green]✓[/] Output: {Markup.Escape(dirs.Base)}/");

                // Summary table
                var totalChars = chunks.Sum(c => c.Content.Length);
                var avgChunkSize = chunks.Length > 0 ? totalChars / chunks.Length : 0;
                var enrichedCount = chunks.Count(c => c.Props.Keys.Any(k => k.StartsWith("enriched_")));

                var table = new Table();
                table.AddColumn("Metric");
                table.AddColumn("Value");
                table.AddRow("Pipeline", pipelineDesc);
                table.AddRow("Total chunks", chunks.Length.ToString());
                table.AddRow("Total characters", totalChars.ToString("N0"));
                table.AddRow("Average chunk size", avgChunkSize.ToString("N0"));
                table.AddRow("Min chunk size", chunks.Length > 0 ? chunks.Min(c => c.Content.Length).ToString("N0") : "0");
                table.AddRow("Max chunk size", chunks.Length > 0 ? chunks.Max(c => c.Content.Length).ToString("N0") : "0");

                if (enableRefine)
                {
                    var reduction = parsedContent.Text.Length > 0
                        ? (1 - (double)contentToChunk.Text.Length / parsedContent.Text.Length) * 100
                        : 0;
                    table.AddRow("Refine reduction", $"{reduction:F1}%");
                }

                if (images.Count > 0)
                    table.AddRow("Images extracted", images.Count.ToString());

                if (skippedImageCount > 0)
                    table.AddRow("Images skipped", skippedImageCount.ToString());

                if (enableEnrich && enrichedCount > 0)
                    table.AddRow("Enriched chunks", $"{enrichedCount} ({enrichedCount * 100 / chunks.Length}%)");

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
