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
            Description = "Output file path (default: input.process.md)"
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

        Arguments.Add(inputArg);
        Options.Add(outputOpt);
        Options.Add(formatOpt);
        Options.Add(strategyOpt);
        Options.Add(maxSizeOpt);
        Options.Add(overlapOpt);
        Options.Add(aiOpt);
        Options.Add(quietOpt);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var input = parseResult.GetValue(inputArg);
            var output = parseResult.GetValue(outputOpt);
            var format = parseResult.GetValue(formatOpt);
            var strategy = parseResult.GetValue(strategyOpt);
            var maxSize = parseResult.GetValue(maxSizeOpt);
            var overlap = parseResult.GetValue(overlapOpt);
            var enrich = parseResult.GetValue(aiOpt);
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

        format ??= "md";
        strategy ??= "Auto";

        // Use chunked output writer for directory-based output
        var writer = new ChunkedOutputWriter(format);

        // Output directory: input.processed/
        output ??= $"{input}.processed";

        var config = new CliEnvironmentConfig();
        var factory = new AIProviderFactory(config);

        if (enrich && !factory.HasAIProvider())
        {
            if (!quiet)
            {
                AnsiConsole.MarkupLine("[yellow]Warning:[/] AI enabled but no provider configured");
                AnsiConsole.MarkupLine("[yellow]→[/] Set OPENAI_API_KEY or ANTHROPIC_API_KEY environment variable");
            }
            enrich = false;
        }

        if (!quiet)
        {
            var panel = new Panel(new Markup(
                $"[bold]Input:[/] {Markup.Escape(input)}\n" +
                $"[bold]Output:[/] {Markup.Escape(output)}\n" +
                $"[bold]Strategy:[/] {strategy} (max: {maxSize}, overlap: {overlap})\n" +
                $"[bold]AI Provider:[/] {(enrich ? factory.GetProviderStatus() : "Disabled")}\n" +
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
            var chunks = await AnsiConsole.Progress()
                .AutoRefresh(true)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[green]Processing document[/]");
                    task.IsIndeterminate = true;

                    // Setup services
                    var services = new ServiceCollection();

                    if (enrich)
                    {
                        factory.ConfigureServices(services);
                    }

                    services.AddFileFlux();
                    using var provider = services.BuildServiceProvider();
                    var processor = provider.GetRequiredService<IDocumentProcessor>();

                    // Configure options
                    var options = new ChunkingOptions
                    {
                        Strategy = strategy,
                        MaxChunkSize = maxSize,
                        OverlapSize = overlap
                    };

                    if (enrich)
                    {
                        options.CustomProperties["enableMetadataEnrichment"] = true;
                        options.CustomProperties["metadataSchema"] = "General";
                    }

                    task.Description = "[green]Extracting and chunking...[/]";
                    var result = await processor.ProcessAsync(input, options, cancellationToken);

                    task.StopTask();
                    return result;
                });

            // Write output
            var chunkList = chunks.ToList();
            await writer.WriteAsync(chunkList, output, cancellationToken);

            // Write info file for chunked output
            var info = new ProcessingInfo
            {
                Command = "process",
                Format = format,
                Strategy = strategy,
                MaxChunkSize = maxSize,
                OverlapSize = overlap,
                AIProvider = enrich ? factory.GetProviderStatus() : null,
                EnrichmentEnabled = enrich
            };
            await ProcessingInfoWriter.WriteChunkedInfoAsync(output, input, chunkList, info, cancellationToken);

            if (!quiet)
            {
                AnsiConsole.MarkupLine($"\n[green]✓ Success![/] Processed document into {chunkList.Count} chunks");
                AnsiConsole.MarkupLine($"[green]✓[/] Saved to: {Markup.Escape(output)}/");
                AnsiConsole.MarkupLine($"[green]✓[/] Info file: {Markup.Escape(Path.Combine(output, "info.json"))}\n");

                // Detailed summary
                var totalChars = chunkList.Sum(c => c.Content.Length);
                var avgSize = totalChars / chunkList.Count;
                var enrichedCount = chunkList.Count(c => c.Metadata.CustomProperties.ContainsKey("enriched_topics"));

                var grid = new Grid();
                grid.AddColumn();
                grid.AddColumn();

                grid.AddRow("[bold]Chunks created:[/]", chunkList.Count.ToString());
                grid.AddRow("[bold]Total characters:[/]", totalChars.ToString("N0"));
                grid.AddRow("[bold]Average size:[/]", avgSize.ToString("N0"));
                grid.AddRow("[bold]Size range:[/]", $"{chunkList.Min(c => c.Content.Length):N0} - {chunkList.Max(c => c.Content.Length):N0}");

                if (enrich && enrichedCount > 0)
                {
                    grid.AddRow("[bold]Enriched chunks:[/]", $"{enrichedCount} ({enrichedCount * 100 / chunkList.Count}%)");
                }

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
            if (!quiet)
            {
                AnsiConsole.WriteException(ex);
            }
        }
    }
}
