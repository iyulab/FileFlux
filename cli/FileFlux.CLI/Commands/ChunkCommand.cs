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
            }
            enableAI = false;
            enableEnrich = false;
        }

        var fluxImprover = fluxImproverResult?.Services;
        services.AddFileFlux();
        await using var provider = services.BuildServiceProvider();
        var processor = provider.GetRequiredService<FluxDocumentProcessor>();
        var imageToTextService = enableAI ? provider.GetService<IImageToTextService>() : null;

        // Configure options
        format ??= "md";
        strategy ??= "Auto";

        // Model-aware chunking: adjust chunk size if AI model has smaller context
        var effectiveMaxSize = maxSize;
        var effectiveOverlap = overlap;
        var chunkSizeAdjusted = false;
        string? chunkAdjustReason = null;

        if (enableEnrich && fluxImproverResult != null && fluxImproverResult.IsLocalModel)
        {
            var modelMaxTokens = fluxImproverResult.MaxEnrichmentTokens;

            // First, apply model limit
            if (maxSize > modelMaxTokens)
            {
                effectiveMaxSize = modelMaxTokens;
                chunkSizeAdjusted = true;
                chunkAdjustReason = "local model limit";
            }

            // Read file sample to detect CJK content for token estimation
            try
            {
                var sampleSize = Math.Min(10000, (int)new FileInfo(input).Length);
                using var reader = new StreamReader(input);
                var buffer = new char[sampleSize];
                var readCount = await reader.ReadAsync(buffer, 0, sampleSize);
                var sampleContent = new string(buffer, 0, readCount);

                var cjkRatio = GetCjkRatio(sampleContent);

                if (cjkRatio > 0.1)  // More than 10% CJK characters
                {
                    var cjkMultiplier = GetCjkChunkSizeMultiplier(cjkRatio);
                    var cjkAdjustedSize = (int)(effectiveMaxSize * cjkMultiplier);

                    // Ensure minimum chunk size of 50 tokens
                    cjkAdjustedSize = Math.Max(50, cjkAdjustedSize);

                    if (cjkAdjustedSize < effectiveMaxSize)
                    {
                        effectiveMaxSize = cjkAdjustedSize;
                        chunkSizeAdjusted = true;
                        chunkAdjustReason = $"CJK content {cjkRatio:P0}";
                    }
                }
            }
            catch
            {
                // Ignore sample read errors - continue with model-based adjustment only
            }

            // Adjust overlap proportionally
            if (chunkSizeAdjusted)
            {
                effectiveOverlap = Math.Min(overlap, effectiveMaxSize / 4);
            }

            if (chunkSizeAdjusted && !quiet)
            {
                AnsiConsole.MarkupLine($"[yellow]Note:[/] Chunk size adjusted: {maxSize} → {effectiveMaxSize} tokens ({chunkAdjustReason})");
            }
        }

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
            var maxSizeDisplay = chunkSizeAdjusted
                ? $"{effectiveMaxSize} [dim](adjusted from {maxSize})[/]"
                : maxSize.ToString();
            var overlapDisplay = chunkSizeAdjusted
                ? $"{effectiveOverlap} [dim](adjusted from {overlap})[/]"
                : overlap.ToString();

            AnsiConsole.MarkupLine($"[blue]FileFlux CLI - Chunk[/]");
            AnsiConsole.MarkupLine($"  Input:    {Markup.Escape(input)}");
            AnsiConsole.MarkupLine($"  Output:   {Markup.Escape(dirs.Base)}/");
            AnsiConsole.MarkupLine($"  Pipeline: {pipelineDesc}");
            AnsiConsole.MarkupLine($"  Strategy: {strategy}");
            AnsiConsole.MarkupLine($"  Max size: {maxSizeDisplay} tokens");
            AnsiConsole.MarkupLine($"  Overlap:  {overlapDisplay} tokens");
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

                    // Stage 3: Chunk (uses model-aware effective size)
                    ctx.Status("Chunking...");
                    var chunkingOptions = new ChunkingOptions
                    {
                        Strategy = strategy,
                        MaxChunkSize = effectiveMaxSize,
                        OverlapSize = effectiveOverlap
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
}
