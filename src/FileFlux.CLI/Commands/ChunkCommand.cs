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
            Description = "Chunking strategy (Auto, Smart, Intelligent, Semantic, Paragraph, FixedSize)",
            DefaultValueFactory = _ => "Auto"
        };

        var maxSizeOpt = new Option<int>("--max-size", "-m")
        {
            Description = "Maximum chunk size in tokens",
            DefaultValueFactory = _ => 512
        };

        var overlapOpt = new Option<int>("--overlap")
        {
            Description = "Overlap size between chunks",
            DefaultValueFactory = _ => 64
        };

        var enrichOpt = new Option<bool>("--enrich")
        {
            Description = "Enable AI metadata enrichment (requires AI provider)"
        };

        var quietOpt = new Option<bool>("--quiet", "-q")
        {
            Description = "Minimal output"
        };

        Arguments.Add(inputArg);
        Options.Add(outputOpt);
        Options.Add(formatOpt);
        Options.Add(strategyOpt);
        Options.Add(maxSizeOpt);
        Options.Add(overlapOpt);
        Options.Add(enrichOpt);
        Options.Add(quietOpt);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var input = parseResult.GetValue(inputArg);
            var output = parseResult.GetValue(outputOpt);
            var format = parseResult.GetValue(formatOpt);
            var strategy = parseResult.GetValue(strategyOpt);
            var maxSize = parseResult.GetValue(maxSizeOpt);
            var overlap = parseResult.GetValue(overlapOpt);
            var enrich = parseResult.GetValue(enrichOpt);
            var quiet = parseResult.GetValue(quietOpt);

            if (input != null)
            {
                await ExecuteAsync(input, output, format, strategy, maxSize, overlap, enrich, quiet, cancellationToken);
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
        bool enrich,
        bool quiet,
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

        IOutputWriter writer = format.ToLowerInvariant() switch
        {
            "json" => new JsonOutputWriter(),
            "jsonl" => new JsonLinesOutputWriter(),
            "markdown" or "md" => new MarkdownOutputWriter(),
            _ => new JsonOutputWriter()
        };

        // User-friendly output naming: input.chunk.{ext}
        output ??= $"{input}.chunk{writer.Extension}";

        // Check AI provider if enrichment requested
        var config = new CliEnvironmentConfig();
        var factory = new AIProviderFactory(config);

        if (enrich && !factory.HasAIProvider())
        {
            AnsiConsole.MarkupLine("[yellow]Warning:[/] AI enrichment requested but no provider configured");
            AnsiConsole.MarkupLine("[yellow]→[/] Set OPENAI_API_KEY or ANTHROPIC_API_KEY environment variable");
            enrich = false;
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
            AnsiConsole.MarkupLine($"  AI:       {(enrich ? $"Enabled ({factory.GetProviderStatus()})" : "Disabled")}");
            AnsiConsole.WriteLine();
        }

        try
        {
            var chunks = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Processing document...", async ctx =>
                {
                    // Setup services
                    var services = new ServiceCollection();

                    // Add AI provider if enrichment enabled
                    if (enrich)
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
                    if (enrich)
                    {
                        options.CustomProperties["enableMetadataEnrichment"] = true;
                        options.CustomProperties["metadataSchema"] = "General";
                    }

                    return await processor.ProcessAsync(input, options, cancellationToken);
                });

            // Write output
            var chunkList = chunks.ToList();
            await writer.WriteAsync(chunkList, output, cancellationToken);

            // Write info file
            var info = new ProcessingInfo
            {
                Command = "chunk",
                Format = format,
                Strategy = strategy,
                MaxChunkSize = maxSize,
                OverlapSize = overlap,
                AIProvider = enrich ? factory.GetProviderStatus() : null,
                EnrichmentEnabled = enrich
            };
            await ProcessingInfoWriter.WriteInfoAsync(output, input, chunkList, info, cancellationToken);

            if (!quiet)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Created {chunkList.Count} chunks");
                AnsiConsole.MarkupLine($"[green]✓[/] Saved to: {Markup.Escape(output)}");
                AnsiConsole.MarkupLine($"[green]✓[/] Info file: {Markup.Escape(ProcessingInfoWriter.GetInfoPath(output))}");

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

                if (enrich)
                {
                    var enrichedCount = chunkList.Count(c => c.Metadata.CustomProperties.ContainsKey("enriched_topics"));
                    table.AddRow("Enriched chunks", enrichedCount.ToString());
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
