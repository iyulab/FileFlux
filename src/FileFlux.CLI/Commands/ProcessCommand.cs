using FileFlux;
using FileFlux.CLI.Output;
using FileFlux.CLI.Services;
using FileFlux.Domain;
using FileFlux.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace FileFlux.CLI.Commands;

/// <summary>
/// Process command - complete pipeline (extract + chunk + enrich)
/// </summary>
public class ProcessCommand : Command
{
    public ProcessCommand() : base("process", "Complete processing pipeline with extraction, chunking, and enrichment")
    {
        var inputArg = new Argument<string>("input", "Input file path");
        var outputOpt = new Option<string>(new[] { "-o", "--output" }, "Output file path (default: input.processed.json)");
        var formatOpt = new Option<string>(new[] { "-f", "--format" }, () => "json", "Output format (json, jsonl, markdown)");
        var strategyOpt = new Option<string>(new[] { "-s", "--strategy" }, () => "Auto", "Chunking strategy");
        var maxSizeOpt = new Option<int>(new[] { "-m", "--max-size" }, () => 512, "Maximum chunk size");
        var overlapOpt = new Option<int>(new[] { "--overlap" }, () => 64, "Overlap size");
        var noEnrichOpt = new Option<bool>(new[] { "--no-enrich" }, "Disable AI enrichment");
        var quietOpt = new Option<bool>(new[] { "-q", "--quiet" }, "Minimal output");

        AddArgument(inputArg);
        AddOption(outputOpt);
        AddOption(formatOpt);
        AddOption(strategyOpt);
        AddOption(maxSizeOpt);
        AddOption(overlapOpt);
        AddOption(noEnrichOpt);
        AddOption(quietOpt);

        this.SetHandler(async (InvocationContext context) =>
        {
            var input = context.ParseResult.GetValueForArgument(inputArg);
            var output = context.ParseResult.GetValueForOption(outputOpt);
            var format = context.ParseResult.GetValueForOption(formatOpt);
            var strategy = context.ParseResult.GetValueForOption(strategyOpt);
            var maxSize = context.ParseResult.GetValueForOption(maxSizeOpt);
            var overlap = context.ParseResult.GetValueForOption(overlapOpt);
            var noEnrich = context.ParseResult.GetValueForOption(noEnrichOpt);
            var quiet = context.ParseResult.GetValueForOption(quietOpt);
            var cancellationToken = context.GetCancellationToken();

            await ExecuteAsync(input, output, format, strategy, maxSize, overlap, !noEnrich, quiet, cancellationToken);
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
            AnsiConsole.MarkupLine($"[red]Error:[/] File not found: {input}");
            return;
        }

        output ??= Path.ChangeExtension(input, ".processed.json");
        format ??= "json";
        strategy ??= "Auto";

        IOutputWriter writer = format.ToLowerInvariant() switch
        {
            "json" => new JsonOutputWriter(),
            "jsonl" => new JsonLinesOutputWriter(),
            "markdown" or "md" => new MarkdownOutputWriter(),
            _ => new JsonOutputWriter()
        };

        if (!output.EndsWith(writer.Extension, StringComparison.OrdinalIgnoreCase))
        {
            output = Path.ChangeExtension(output, writer.Extension);
        }

        var config = new CliEnvironmentConfig();
        var factory = new AIProviderFactory(config);

        if (enrich && !factory.HasAIProvider())
        {
            if (!quiet)
            {
                AnsiConsole.MarkupLine("[yellow]Warning:[/] No AI provider configured - enrichment disabled");
            }
            enrich = false;
        }

        if (!quiet)
        {
            var panel = new Panel(new Markup(
                $"[bold]Input:[/] {input}\n" +
                $"[bold]Output:[/] {output}\n" +
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
            await writer.WriteAsync(chunks, output, cancellationToken);

            if (!quiet)
            {
                var chunkList = chunks.ToList();
                AnsiConsole.MarkupLine($"\n[green]✓ Success![/] Processed document into {chunkList.Count} chunks");
                AnsiConsole.MarkupLine($"[green]✓[/] Saved to: {output}\n");

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
