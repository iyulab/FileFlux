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
/// Chunk command - intelligent chunking with optional AI enrichment
/// </summary>
public class ChunkCommand : Command
{
    public ChunkCommand() : base("chunk", "Chunk document with intelligent strategies")
    {
        var inputArg = new Argument<string>("input", "Input file path");
        var outputOpt = new Option<string>(new[] { "-o", "--output" }, "Output file path (default: input.chunks.json)");
        var formatOpt = new Option<string>(new[] { "-f", "--format" }, () => "json", "Output format (json, jsonl, markdown)");
        var strategyOpt = new Option<string>(new[] { "-s", "--strategy" }, () => "Auto", "Chunking strategy (Auto, Smart, Intelligent, Semantic, Paragraph, FixedSize)");
        var maxSizeOpt = new Option<int>(new[] { "-m", "--max-size" }, () => 512, "Maximum chunk size in tokens");
        var overlapOpt = new Option<int>(new[] { "--overlap" }, () => 64, "Overlap size between chunks");
        var enrichOpt = new Option<bool>(new[] { "--enrich" }, "Enable AI metadata enrichment (requires AI provider)");
        var quietOpt = new Option<bool>(new[] { "-q", "--quiet" }, "Minimal output");

        AddArgument(inputArg);
        AddOption(outputOpt);
        AddOption(formatOpt);
        AddOption(strategyOpt);
        AddOption(maxSizeOpt);
        AddOption(overlapOpt);
        AddOption(enrichOpt);
        AddOption(quietOpt);

        this.SetHandler(async (InvocationContext context) =>
        {
            var input = context.ParseResult.GetValueForArgument(inputArg);
            var output = context.ParseResult.GetValueForOption(outputOpt);
            var format = context.ParseResult.GetValueForOption(formatOpt);
            var strategy = context.ParseResult.GetValueForOption(strategyOpt);
            var maxSize = context.ParseResult.GetValueForOption(maxSizeOpt);
            var overlap = context.ParseResult.GetValueForOption(overlapOpt);
            var enrich = context.ParseResult.GetValueForOption(enrichOpt);
            var quiet = context.ParseResult.GetValueForOption(quietOpt);
            var cancellationToken = context.GetCancellationToken();

            await ExecuteAsync(input, output, format, strategy, maxSize, overlap, enrich, quiet, cancellationToken);
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

        // Determine output path and format
        output ??= Path.ChangeExtension(input, ".chunks.json");
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
            AnsiConsole.MarkupLine($"  Input:    {input}");
            AnsiConsole.MarkupLine($"  Output:   {output}");
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
            await writer.WriteAsync(chunks, output, cancellationToken);

            if (!quiet)
            {
                var chunkList = chunks.ToList();
                AnsiConsole.MarkupLine($"[green]✓[/] Created {chunkList.Count} chunks");
                AnsiConsole.MarkupLine($"[green]✓[/] Saved to: {output}");

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
