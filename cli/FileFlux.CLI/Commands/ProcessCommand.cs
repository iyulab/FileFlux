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
        FluxImproverResult? fluxImproverResult = null;

        if (enableAI && factory.HasAIProvider())
        {
            factory.ConfigureServices(services);
            aiProvider = config.DetectProvider();

            if (enableEnrich)
            {
                try
                {
                    fluxImproverResult = factory.CreateFluxImproverServices();
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

        // Get FluxImprover services (if available)
        var fluxImprover = fluxImproverResult?.Services;

        services.AddFileFlux();
        await using var provider = services.BuildServiceProvider();
        var processor = provider.GetRequiredService<FluxDocumentProcessor>();
        var imageToTextService = enableAI ? provider.GetService<IImageToTextService>() : null;

        // Configure options
        format ??= "md";
        strategy ??= "Auto";

        // Model-aware chunking: adjust chunk size if AI model has smaller context
        // Initial adjustment based on model limit (CJK detection happens after parsing)
        var effectiveMaxSize = maxSize;
        var effectiveOverlap = overlap;
        var chunkSizeAdjusted = false;
        string? chunkAdjustReason = null;
        var isLocalModel = enableEnrich && fluxImproverResult != null && fluxImproverResult.IsLocalModel;

        if (isLocalModel)
        {
            var modelMaxTokens = fluxImproverResult!.MaxEnrichmentTokens;

            // Apply model limit
            if (maxSize > modelMaxTokens)
            {
                effectiveMaxSize = modelMaxTokens;
                chunkSizeAdjusted = true;
                chunkAdjustReason = "local model limit";
                effectiveOverlap = Math.Min(overlap, effectiveMaxSize / 4);

                if (!quiet)
                {
                    AnsiConsole.MarkupLine($"[yellow]Note:[/] Chunk size adjusted: {maxSize} → {effectiveMaxSize} tokens ({chunkAdjustReason})");
                }
            }
        }

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
            var strategyInfo = chunkSizeAdjusted
                ? $"{strategy} (max: {effectiveMaxSize} [dim]←{maxSize}[/], overlap: {effectiveOverlap})"
                : $"{strategy} (max: {maxSize}, overlap: {overlap})";

            var panel = new Panel(new Markup(
                $"[bold]Input:[/] {Markup.Escape(input)}\n" +
                $"[bold]Output:[/] {Markup.Escape(dirs.Base)}/\n" +
                $"[bold]Pipeline:[/] {pipelineDesc}\n" +
                $"[bold]Strategy:[/] {strategyInfo}\n" +
                $"[bold]Format:[/] {format}"))
            {
                Header = new PanelHeader("[blue]FileFlux CLI - Process[/]"),
                Border = BoxBorder.Rounded
            };

            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine();

            // Display detailed model information if AI is enabled
            if (enableAI && factory.HasAIProvider())
            {
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
            RawContent rawContent = null!;
            ParsedContent parsedContent = null!;
            ParsedContent refinedContent = null!;
            DocumentChunk[] chunks = null!;
            var images = new List<Domain.ProcessedImage>();
            var skippedImageCount = 0;
            var enrichSkippedCount = 0;
            var enrichErrorMessages = new List<string>();

            // Pre-load AI model if enrichment is enabled (shows loading progress)
            if (enableEnrich && fluxImprover != null)
            {
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("blue"))
                    .StartAsync("[blue]Loading AI model for enrichment...[/]", async ctx =>
                    {
                        // Warm up the model by making a small test call
                        try
                        {
                            var warmupChunk = new FluxImprover.Models.Chunk
                            {
                                Id = "warmup",
                                Content = "test",
                                Metadata = new Dictionary<string, object>()
                            };
                            await fluxImprover.ChunkEnrichment.EnrichAsync(warmupChunk, null, cancellationToken);
                        }
                        catch
                        {
                            // Ignore warmup errors - actual errors will be caught during processing
                        }
                    });
                AnsiConsole.MarkupLine("[green]✓[/] AI model loaded");
                AnsiConsole.WriteLine();
            }

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
                    var currentStage = 0;

                    // ── Stage 1: Read file ──
                    var readTask = ctx.AddTask("[yellow]  Reading file...[/]", maxValue: 100);
                    rawContent = await processor.ExtractAsync(input, cancellationToken);
                    readTask.Value = 100;
                    readTask.Description = "[green]  Reading file[/] ✓";

                    // ── Stage 2: Parse content ──
                    var parseTask = ctx.AddTask("[yellow]  Parsing content...[/]", maxValue: 100);
                    parsedContent = await processor.ParseAsync(rawContent, null, cancellationToken);
                    parseTask.Value = 100;
                    parseTask.Description = $"[green]  Parsing content[/] ✓ ({parsedContent.Text.Length:N0} chars)";

                    // ── Stage 3: Process images (if needed) ──
                    if (extractImages)
                    {
                        var imageCount = rawContent.Images.Count;
                        var imageTaskDesc = imageCount > 0
                            ? $"[yellow]  Processing {imageCount} image(s)...[/]"
                            : "[yellow]  Scanning for images...[/]";
                        var imageTask = ctx.AddTask(imageTaskDesc, maxValue: 100);

                        var outputOptions = new OutputOptions
                        {
                            ExtractImages = true,
                            MinImageSize = minImageSize,
                            MinImageDimension = minImageDimension,
                            Verbose = verbose
                        };
                        var imageProcessor = new Infrastructure.Services.ImageProcessor(outputOptions);

                        // Check if images were pre-extracted by Reader (e.g., HTML with embedded base64)
                        if (rawContent.Images.Count > 0 && rawContent.Images.Any(i => i.Data != null))
                        {
                            var imageResult = await imageProcessor.ProcessPreExtractedImagesAsync(
                                parsedContent.Text, rawContent.Images, dirs.Images, imageToTextService, cancellationToken);

                            parsedContent.Text = imageResult.ProcessedContent;
                            images = imageResult.Images;
                            skippedImageCount = imageResult.SkippedCount;
                        }
                        else
                        {
                            // Fallback to inline base64 processing (for other document types)
                            var imageResult = await imageProcessor.ProcessImagesAsync(
                                parsedContent.Text, dirs.Images, imageToTextService, cancellationToken);

                            parsedContent.Text = imageResult.ProcessedContent;
                            images = imageResult.Images;
                            skippedImageCount = imageResult.SkippedCount;
                        }

                        imageTask.Value = 100;
                        if (images.Count > 0)
                            imageTask.Description = $"[green]  Processing images[/] ✓ ({images.Count} extracted)";
                        else
                            imageTask.Description = "[dim]  Processing images[/] (none found)";
                    }
                    else
                    {
                        parsedContent.Text = Infrastructure.Services.ImageProcessor.RemoveBase64Images(parsedContent.Text);
                    }

                    // ── Stage 4: Save extract result ──
                    var saveTask = ctx.AddTask("[yellow]  Saving extracted content...[/]", maxValue: 100);
                    await File.WriteAllTextAsync(
                        Path.Combine(dirs.Extract, "extracted.md"),
                        parsedContent.Text,
                        cancellationToken);
                    saveTask.Value = 100;
                    saveTask.Description = "[green]  Saving extracted content[/] ✓";
                    currentStage++;

                    // ── Stage 5: Refine (optional) ──
                    if (enableRefine)
                    {
                        var refineTask = ctx.AddTask("[yellow]  Refining content...[/]", maxValue: 100);

                        // Auto-select refining options based on document type and language
                        var refiningOptions = SelectRefiningOptions(input, parsedContent.Text);

                        // Pass RawContent via Extra for full table/structure conversion support
                        refiningOptions.Extra["_rawContent"] = rawContent;

                        refinedContent = await processor.RefineAsync(parsedContent, refiningOptions, cancellationToken);
                        var reduction = parsedContent.Text.Length > 0
                            ? (1 - (double)refinedContent.Text.Length / parsedContent.Text.Length) * 100
                            : 0;
                        refineTask.Value = 100;
                        refineTask.Description = $"[green]  Refining content[/] ✓ ({reduction:F0}% reduced)";
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

                    // ── CJK Detection (after parsing, before chunking) ──
                    // Detect CJK in parsed content (not raw file) for accurate token estimation
                    if (isLocalModel)
                    {
                        var contentToAnalyze = refinedContent.Text.Length > 0 ? refinedContent.Text : parsedContent.Text;
                        var sampleForCjk = contentToAnalyze.Length > 5000
                            ? contentToAnalyze.Substring(0, 5000)
                            : contentToAnalyze;

                        var cjkRatio = GetCjkRatio(sampleForCjk);

                        if (cjkRatio > 0.1)  // More than 10% CJK characters
                        {
                            var cjkMultiplier = GetCjkChunkSizeMultiplier(cjkRatio);
                            var cjkAdjustedSize = (int)(effectiveMaxSize * cjkMultiplier);

                            // Ensure minimum chunk size of 50 tokens
                            cjkAdjustedSize = Math.Max(50, cjkAdjustedSize);

                            if (cjkAdjustedSize < effectiveMaxSize)
                            {
                                effectiveMaxSize = cjkAdjustedSize;
                                effectiveOverlap = Math.Min(overlap, effectiveMaxSize / 4);
                                chunkSizeAdjusted = true;
                                chunkAdjustReason = $"CJK {cjkRatio:P0}";
                            }
                        }
                    }

                    // ── Stage 6: Chunk ──
                    var chunkDesc = chunkSizeAdjusted
                        ? $"[yellow]  Chunking content ({strategy}, max:{effectiveMaxSize})...[/]"
                        : $"[yellow]  Chunking content ({strategy})...[/]";
                    var chunkTask = ctx.AddTask(chunkDesc, maxValue: 100);
                    var chunkingOptions = new ChunkingOptions
                    {
                        Strategy = strategy,
                        MaxChunkSize = effectiveMaxSize,  // Use model-aware effective size (with CJK adjustment)
                        OverlapSize = effectiveOverlap  // Adjusted overlap
                    };
                    chunks = await processor.ChunkAsync(refinedContent, chunkingOptions, cancellationToken);
                    chunkTask.Value = 100;
                    var chunkSuffix = chunkSizeAdjusted ? $", max:{effectiveMaxSize}" : "";
                    chunkTask.Description = $"[green]  Chunking content[/] ✓ ({chunks.Length} chunks{chunkSuffix})";

                    // ── Stage 7: Enrich (optional) ──
                    if (enableEnrich && fluxImprover != null && chunks.Length > 0)
                    {
                        var enrichTask = ctx.AddTask($"[yellow]  Enriching chunks...[/]", maxValue: chunks.Length);
                        int adaptiveCount = 0; // Count of chunks processed adaptively (split)

                        for (int i = 0; i < chunks.Length; i++)
                        {
                            enrichTask.Description = $"[yellow]  Enriching chunk {i + 1}/{chunks.Length}...[/]";

                            var chunk = chunks[i];
                            var fluxChunk = new FluxImprover.Models.Chunk
                            {
                                Id = chunk.Id.ToString(),
                                Content = chunk.Content,
                                Metadata = chunk.Props
                            };

                            try
                            {
                                // Use adaptive enrichment that handles long chunks
                                var (summary, keywords, success) = await AdaptiveEnrichChunkAsync(
                                    fluxImprover, fluxChunk, DefaultMaxEnrichmentChars, cancellationToken);

                                if (success)
                                {
                                    if (!string.IsNullOrEmpty(summary))
                                        chunk.Props[ChunkPropsKeys.EnrichedSummary] = summary;
                                    if (keywords?.Any() == true)
                                        chunk.Props[ChunkPropsKeys.EnrichedKeywords] = keywords;

                                    // Check if this was processed adaptively (content was longer than threshold)
                                    if (chunk.Content.Length > DefaultMaxEnrichmentChars)
                                        adaptiveCount++;
                                }
                                else
                                {
                                    enrichSkippedCount++;
                                    chunk.Props["enrichment_error"] = "Failed to enrich even with adaptive splitting";
                                }
                            }
                            catch (Exception ex)
                            {
                                // Other errors - log and continue
                                enrichSkippedCount++;
                                chunk.Props["enrichment_error"] = ex.Message;

                                if (verbose && enrichErrorMessages.Count < 3)
                                {
                                    enrichErrorMessages.Add($"Chunk {i + 1}: {ex.Message}");
                                }
                            }

                            enrichTask.Increment(1);
                        }

                        var enrichedCount = chunks.Length - enrichSkippedCount;
                        if (enrichSkippedCount > 0)
                        {
                            enrichTask.Description = $"[yellow]  Enriching chunks[/] ⚠ ({enrichedCount}/{chunks.Length} succeeded)";
                        }
                        else if (adaptiveCount > 0)
                        {
                            enrichTask.Description = $"[green]  Enriching chunks[/] ✓ (all {chunks.Length}, {adaptiveCount} split)";
                        }
                        else
                        {
                            enrichTask.Description = $"[green]  Enriching chunks[/] ✓ (all {chunks.Length})";
                        }
                    }

                });

            // Report enrichment errors after progress completes
            if (enrichSkippedCount > 0 && !quiet)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[yellow]⚠ Warning:[/] {enrichSkippedCount} chunk(s) skipped during enrichment (content too long for model)");
                if (enrichErrorMessages.Count > 0)
                {
                    foreach (var msg in enrichErrorMessages)
                    {
                        AnsiConsole.MarkupLine($"  [dim]• {Markup.Escape(msg)}[/]");
                    }
                    if (enrichSkippedCount > enrichErrorMessages.Count)
                    {
                        AnsiConsole.MarkupLine($"  [dim]• ... and {enrichSkippedCount - enrichErrorMessages.Count} more[/]");
                    }
                }
            }

            // Write output
            var chunkingOptions = new ChunkingOptions
            {
                Strategy = strategy,
                MaxChunkSize = effectiveMaxSize,
                OverlapSize = effectiveOverlap
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
        finally
        {
            // Dispose FluxImprover resources (including ONNX GenAI models)
            if (fluxImproverResult != null)
            {
                await fluxImproverResult.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    #region CJK Detection

    /// <summary>
    /// Calculate the ratio of CJK (Chinese, Japanese, Korean) characters in content.
    /// CJK characters use ~2-3 tokens each vs English ~0.25 tokens/char.
    /// </summary>
    private static double GetCjkRatio(string content)
    {
        if (string.IsNullOrEmpty(content))
            return 0;

        int cjkCount = 0;
        int totalChars = 0;

        foreach (var c in content)
        {
            // Skip whitespace and control characters
            if (char.IsWhiteSpace(c) || char.IsControl(c))
                continue;

            totalChars++;

            // CJK Unified Ideographs and extensions
            if (c >= 0x4E00 && c <= 0x9FFF)  // CJK Unified Ideographs
                cjkCount++;
            else if (c >= 0x3400 && c <= 0x4DBF)  // CJK Extension A
                cjkCount++;
            else if (c >= 0xAC00 && c <= 0xD7AF)  // Hangul Syllables (Korean)
                cjkCount++;
            else if (c >= 0x1100 && c <= 0x11FF)  // Hangul Jamo
                cjkCount++;
            else if (c >= 0x3130 && c <= 0x318F)  // Hangul Compatibility Jamo
                cjkCount++;
            else if (c >= 0x3040 && c <= 0x309F)  // Hiragana
                cjkCount++;
            else if (c >= 0x30A0 && c <= 0x30FF)  // Katakana
                cjkCount++;
        }

        return totalChars > 0 ? (double)cjkCount / totalChars : 0;
    }

    /// <summary>
    /// Calculate effective chunk size multiplier based on CJK content ratio.
    /// CJK text uses ~2-3 tokens per character vs ~0.25 tokens per char for English.
    /// </summary>
    private static double GetCjkChunkSizeMultiplier(double cjkRatio)
    {
        // Token density: English ~0.25 tokens/char, CJK ~2.5 tokens/char
        // Blended density = cjkRatio * 2.5 + (1-cjkRatio) * 0.25
        // We need to scale down chunk size proportionally

        if (cjkRatio < 0.1)
            return 1.0;  // Mostly English, no adjustment needed

        // For high CJK content, we need much smaller chunks
        // cjkRatio=0.2 → multiplier ~0.6
        // cjkRatio=0.5 → multiplier ~0.35
        // cjkRatio=0.8 → multiplier ~0.2
        var multiplier = 1.0 / (1.0 + cjkRatio * 4.0);

        // Ensure minimum multiplier of 0.15 (don't create too tiny chunks)
        return Math.Max(0.15, multiplier);
    }

    #endregion

    #region Adaptive Enrichment

    /// <summary>
    /// Default maximum characters for enrichment (conservative estimate: ~400 tokens)
    /// Models vary: Phi-4-mini=512, GPT-4=8K+, etc.
    /// </summary>
    private const int DefaultMaxEnrichmentChars = 1600;

    /// <summary>
    /// Adaptively enrich a chunk by splitting if too long for the model.
    /// </summary>
    private static async Task<(string? Summary, IEnumerable<string>? Keywords, bool Success)> AdaptiveEnrichChunkAsync(
        FluxImproverServices fluxImprover,
        FluxImprover.Models.Chunk chunk,
        int maxChars,
        CancellationToken cancellationToken)
    {
        // First, try direct enrichment
        try
        {
            var enriched = await fluxImprover.ChunkEnrichment.EnrichAsync(chunk, null, cancellationToken);
            return (enriched.Summary, enriched.Keywords, true);
        }
        catch (Exception ex) when (IsTokenLengthError(ex))
        {
            // Content too long - split and process adaptively
        }

        // Split content into smaller segments
        var segments = SplitContentForEnrichment(chunk.Content, maxChars);
        if (segments.Count == 0)
            return (null, null, false);

        var summaries = new List<string>();
        var allKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (segment, index) in segments.Select((s, i) => (s, i)))
        {
            var subChunk = new FluxImprover.Models.Chunk
            {
                Id = $"{chunk.Id}_part{index + 1}",
                Content = segment,
                Metadata = chunk.Metadata
            };

            try
            {
                var enriched = await fluxImprover.ChunkEnrichment.EnrichAsync(subChunk, null, cancellationToken);

                if (!string.IsNullOrWhiteSpace(enriched.Summary))
                    summaries.Add(enriched.Summary);

                if (enriched.Keywords != null)
                {
                    foreach (var kw in enriched.Keywords)
                        allKeywords.Add(kw);
                }
            }
            catch (Exception subEx) when (IsTokenLengthError(subEx))
            {
                // Even sub-segment is too long - skip this part
                continue;
            }
        }

        // Merge results
        var mergedSummary = MergeSummaries(summaries);
        var mergedKeywords = allKeywords.Take(10).ToList(); // Limit keywords

        return (mergedSummary, mergedKeywords.Count > 0 ? mergedKeywords : null, summaries.Count > 0);
    }

    /// <summary>
    /// Check if exception is related to token length limits.
    /// </summary>
    private static bool IsTokenLengthError(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("exceeds max length") ||
               message.Contains("input_ids") ||
               message.Contains("token") ||
               message.Contains("context length") ||
               message.Contains("maximum context");
    }

    /// <summary>
    /// Split content into segments suitable for enrichment.
    /// Tries to split at natural boundaries (sentences, paragraphs).
    /// </summary>
    private static List<string> SplitContentForEnrichment(string content, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new List<string>();

        if (content.Length <= maxChars)
            return new List<string> { content };

        var segments = new List<string>();
        var paragraphs = content.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);

        var currentSegment = new System.Text.StringBuilder();

        foreach (var para in paragraphs)
        {
            // If single paragraph exceeds max, split by sentences
            if (para.Length > maxChars)
            {
                // Flush current segment first
                if (currentSegment.Length > 0)
                {
                    segments.Add(currentSegment.ToString().Trim());
                    currentSegment.Clear();
                }

                // Split long paragraph by sentences
                var sentences = SplitIntoSentences(para);
                var sentenceSegment = new System.Text.StringBuilder();

                foreach (var sentence in sentences)
                {
                    if (sentenceSegment.Length + sentence.Length > maxChars)
                    {
                        if (sentenceSegment.Length > 0)
                        {
                            segments.Add(sentenceSegment.ToString().Trim());
                            sentenceSegment.Clear();
                        }

                        // If single sentence is still too long, truncate
                        if (sentence.Length > maxChars)
                        {
                            segments.Add(sentence.Substring(0, maxChars - 50) + "...");
                        }
                        else
                        {
                            sentenceSegment.Append(sentence);
                        }
                    }
                    else
                    {
                        sentenceSegment.Append(sentence);
                    }
                }

                if (sentenceSegment.Length > 0)
                    segments.Add(sentenceSegment.ToString().Trim());
            }
            else if (currentSegment.Length + para.Length + 2 > maxChars)
            {
                // Current segment would exceed max - flush it
                if (currentSegment.Length > 0)
                {
                    segments.Add(currentSegment.ToString().Trim());
                    currentSegment.Clear();
                }
                currentSegment.Append(para);
            }
            else
            {
                if (currentSegment.Length > 0)
                    currentSegment.Append("\n\n");
                currentSegment.Append(para);
            }
        }

        if (currentSegment.Length > 0)
            segments.Add(currentSegment.ToString().Trim());

        return segments.Where(s => s.Length > 50).ToList(); // Filter very short segments
    }

    /// <summary>
    /// Split text into sentences (simple heuristic).
    /// </summary>
    private static IEnumerable<string> SplitIntoSentences(string text)
    {
        // Simple sentence splitting - handles Korean and English
        var sentenceEnders = new[] { ".", "!", "?", "。", "！", "？" };
        var result = new List<string>();
        var current = new System.Text.StringBuilder();

        for (int i = 0; i < text.Length; i++)
        {
            current.Append(text[i]);

            // Check for sentence enders
            if (sentenceEnders.Any(e => text[i].ToString() == e))
            {
                // Look ahead - don't split if followed by digit or lowercase (e.g., "3.14" or abbreviations)
                if (i + 1 < text.Length)
                {
                    var nextChar = text[i + 1];
                    if (char.IsDigit(nextChar) || (char.IsLetter(nextChar) && char.IsLower(nextChar)))
                        continue;
                }

                result.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }

    /// <summary>
    /// Merge multiple summaries into one coherent summary.
    /// </summary>
    private static string? MergeSummaries(List<string> summaries)
    {
        if (summaries.Count == 0)
            return null;

        if (summaries.Count == 1)
            return summaries[0];

        // Join summaries with transition
        var merged = string.Join(" ", summaries.Select((s, i) =>
        {
            var trimmed = s.Trim();
            // Remove redundant starting phrases from subsequent summaries
            if (i > 0)
            {
                var prefixesToRemove = new[] { "This text", "This section", "The text", "The document" };
                foreach (var prefix in prefixesToRemove)
                {
                    if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var afterPrefix = trimmed.Substring(prefix.Length).TrimStart();
                        if (afterPrefix.Length > 0)
                            trimmed = char.ToUpper(afterPrefix[0]) + afterPrefix.Substring(1);
                        break;
                    }
                }
            }
            return trimmed;
        }));

        // Truncate if too long
        if (merged.Length > 1000)
            merged = merged.Substring(0, 997) + "...";

        return merged;
    }

    #endregion

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
