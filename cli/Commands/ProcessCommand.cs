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
/// Process command - complete 4-stage pipeline (Extract → Refine → Chunk → Enrich)
/// </summary>
public class ProcessCommand : Command
{
    public ProcessCommand() : base("process", "Complete 4-stage pipeline: Extract → Refine → Chunk → Enrich")
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

        // Pipeline stage options
        var noRefineOpt = new Option<bool>("--no-refine")
        {
            Description = "Skip refine stage (cleaning/structure)"
        };

        var noEnrichOpt = new Option<bool>("--no-enrich")
        {
            Description = "Skip enrich stage (AI summaries/keywords)"
        };

        // Chunking options
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

        // AI options (enabled by default via LMSupply)
        var noAiOpt = new Option<bool>("--no-ai")
        {
            Description = "Disable AI features (enrichment, image analysis). AI is enabled by default."
        };

        // Image options
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

        // Output options
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
        Options.Add(noRefineOpt);
        Options.Add(noEnrichOpt);
        Options.Add(strategyOpt);
        Options.Add(maxSizeOpt);
        Options.Add(overlapOpt);
        Options.Add(noAiOpt);
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
            var noRefine = parseResult.GetValue(noRefineOpt);
            var noEnrich = parseResult.GetValue(noEnrichOpt);
            var strategy = parseResult.GetValue(strategyOpt);
            var maxSize = parseResult.GetValue(maxSizeOpt);
            var overlap = parseResult.GetValue(overlapOpt);
            var noAi = parseResult.GetValue(noAiOpt);
            var noExtractImages = parseResult.GetValue(noExtractImagesOpt);
            var minImageSize = parseResult.GetValue(minImageSizeOpt);
            var minImageDimension = parseResult.GetValue(minImageDimensionOpt);
            var quiet = parseResult.GetValue(quietOpt);
            var verbose = parseResult.GetValue(verboseOpt);

            // AI enabled by default (via LMSupply), disabled with --no-ai
            var enableAI = !noAi;

            if (input != null)
            {
                await ExecuteAsync(input, output, format, !noRefine, !noEnrich && enableAI,
                    strategy, maxSize, overlap, enableAI, !noExtractImages,
                    minImageSize, minImageDimension, quiet, verbose, cancellationToken);
            }
        });
    }

    private static async Task ExecuteAsync(
        string input,
        string? output,
        string? format,
        bool enableRefine,
        bool enableEnrich,
        string? strategy,
        int maxSize,
        int overlap,
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
                AnsiConsole.MarkupLine("[yellow]→[/] Set OPENAI_API_KEY, ANTHROPIC_API_KEY, or GOOGLE_API_KEY");
            }
            enableAI = false;
            enableEnrich = false;
        }

        services.AddFileFlux();
        using var provider = services.BuildServiceProvider();
        var processor = provider.GetRequiredService<FluxDocumentProcessor>();
        var imageToTextService = enableAI ? provider.GetService<IImageToTextService>() : null;

        // Configure options
        format ??= "md";
        strategy ??= "Auto";

        // Get output directories
        var dirs = OutputOptions.GetOutputDirectories(input, output);
        Directory.CreateDirectory(dirs.Base);
        Directory.CreateDirectory(dirs.Extract);
        if (enableRefine) Directory.CreateDirectory(dirs.Refine);
        Directory.CreateDirectory(dirs.Chunks);
        if (enableEnrich) Directory.CreateDirectory(dirs.Enrich);
        if (extractImages) Directory.CreateDirectory(dirs.Images);

        // Build pipeline description
        var stages = new List<string> { "Extract" };
        if (enableRefine) stages.Add("Refine");
        stages.Add("Chunk");
        if (enableEnrich) stages.Add("Enrich");
        var pipelineDesc = string.Join(" → ", stages);

        if (!quiet)
        {
            var panel = new Panel(new Markup(
                $"[bold]Input:[/] {Markup.Escape(input)}\n" +
                $"[bold]Output:[/] {Markup.Escape(dirs.Base)}/\n" +
                $"[bold]Pipeline:[/] {pipelineDesc}\n" +
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
            RawContent rawContent = null!;
            ParsedContent parsedContent = null!;
            ParsedContent refinedContent = null!;
            DocumentChunk[] chunks = null!;
            var images = new List<Domain.ProcessedImage>();
            var skippedImageCount = 0;

            await AnsiConsole.Progress()
                .AutoClear(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn()
                })
                .StartAsync(async ctx =>
                {
                    var totalStages = stages.Count;
                    var currentStage = 0;

                    // Stage 1: Extract
                    var extractTask = ctx.AddTask("[yellow]Stage 1: Extract[/]", maxValue: 100);
                    rawContent = await processor.ExtractAsync(input, cancellationToken);
                    extractTask.Value = 100;
                    currentStage++;

                    // Stage 2: Parse
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
                        var refineTask = ctx.AddTask("[yellow]Stage 2: Refine[/]", maxValue: 100);

                        // Auto-select refining options based on document type and language
                        var refiningOptions = SelectRefiningOptions(input, parsedContent.Text);
                        refinedContent = await processor.RefineAsync(parsedContent, refiningOptions, cancellationToken);
                        refineTask.Value = 100;
                        currentStage++;

                        // Save refine result
                        await File.WriteAllTextAsync(
                            Path.Combine(dirs.Refine, "refined.md"),
                            refinedContent.Text,
                            cancellationToken);
                    }
                    else
                    {
                        refinedContent = parsedContent;
                    }

                    // Stage 3: Chunk
                    var chunkTask = ctx.AddTask($"[yellow]Stage {currentStage + 1}: Chunk[/]", maxValue: 100);
                    var chunkingOptions = new ChunkingOptions
                    {
                        Strategy = strategy,
                        MaxChunkSize = maxSize,
                        OverlapSize = overlap
                    };
                    chunks = await processor.ChunkAsync(refinedContent, chunkingOptions, cancellationToken);
                    chunkTask.Value = 100;
                    currentStage++;

                    // Stage 4: Enrich (optional)
                    if (enableEnrich && fluxImprover != null && chunks.Length > 0)
                    {
                        var enrichTask = ctx.AddTask($"[yellow]Stage {currentStage + 1}: Enrich[/]", maxValue: chunks.Length);

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

                            enrichTask.Increment(1);
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
                ParsedContent = refinedContent,
                ProcessedText = refinedContent.Text,
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
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[green]✓ Success![/] Processed document through {stages.Count} stages");
                AnsiConsole.MarkupLine($"[green]✓[/] Output: {Markup.Escape(dirs.Base)}/\n");

                // Summary
                var totalChars = chunks.Sum(c => c.Content.Length);
                var avgSize = chunks.Length > 0 ? totalChars / chunks.Length : 0;
                var enrichedCount = chunks.Count(c => c.Props.Keys.Any(k => k.StartsWith("enriched_")));

                var grid = new Grid();
                grid.AddColumn();
                grid.AddColumn();

                grid.AddRow("[bold]Pipeline:[/]", pipelineDesc);
                grid.AddRow("[bold]Chunks created:[/]", chunks.Length.ToString());
                grid.AddRow("[bold]Total characters:[/]", totalChars.ToString("N0"));
                grid.AddRow("[bold]Average size:[/]", avgSize.ToString("N0"));
                grid.AddRow("[bold]Size range:[/]", chunks.Length > 0
                    ? $"{chunks.Min(c => c.Content.Length):N0} - {chunks.Max(c => c.Content.Length):N0}"
                    : "0 - 0");

                if (enableRefine)
                {
                    var reduction = parsedContent.Text.Length > 0
                        ? (1 - (double)refinedContent.Text.Length / parsedContent.Text.Length) * 100
                        : 0;
                    grid.AddRow("[bold]Refine reduction:[/]", $"{reduction:F1}%");
                }

                if (images.Count > 0)
                    grid.AddRow("[bold]Images extracted:[/]", images.Count.ToString());

                if (enableEnrich && enrichedCount > 0)
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

    /// <summary>
    /// Auto-select refining options based on document type and detected language.
    /// </summary>
    private static RefiningOptions SelectRefiningOptions(string filePath, string content)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // Detect language for Korean-specific processing
        var (language, confidence) = Infrastructure.Services.LanguageDetector.Detect(content);
        var isKorean = language == "ko" && confidence >= 0.5;

        // Select preset based on document type and language
        return extension switch
        {
            ".pdf" when isKorean => new RefiningOptions
            {
                TextRefinementPreset = "ForKorean",  // Korean takes priority
                RemoveHeadersFooters = true,
                RemovePageNumbers = true,
                CleanWhitespace = true,
                RestructureHeadings = true,
                ConvertToMarkdown = true
            },
            ".pdf" => RefiningOptions.ForPdfDocument,
            ".html" or ".htm" when isKorean => RefiningOptions.ForKoreanWebContent,
            ".html" or ".htm" => RefiningOptions.ForWebContent,
            _ when isKorean => RefiningOptions.ForKoreanWebContent,
            _ => RefiningOptions.ForRAG  // Default: Standard preset for RAG
        };
    }
}
