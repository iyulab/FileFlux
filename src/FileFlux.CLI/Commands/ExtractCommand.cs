using FileFlux;
using FileFlux.CLI.Output;
using FileFlux.CLI.Services;
using FileFlux.Domain;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace FileFlux.CLI.Commands;

/// <summary>
/// Extract command - extract raw text/content from documents
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
            Description = "Output file path (default: input.extracted.json)"
        };

        var formatOpt = new Option<string>("--format", "-f")
        {
            Description = "Output format (json, jsonl, markdown)",
            DefaultValueFactory = _ => "json"
        };

        var quietOpt = new Option<bool>("--quiet", "-q")
        {
            Description = "Minimal output"
        };

        var enableVisionOpt = new Option<bool>("--enable-vision")
        {
            Description = "Enable image extraction using AI vision (requires OpenAI API key)"
        };

        Arguments.Add(inputArg);
        Options.Add(outputOpt);
        Options.Add(formatOpt);
        Options.Add(quietOpt);
        Options.Add(enableVisionOpt);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var input = parseResult.GetValue(inputArg);
            var output = parseResult.GetValue(outputOpt);
            var format = parseResult.GetValue(formatOpt);
            var quiet = parseResult.GetValue(quietOpt);
            var enableVision = parseResult.GetValue(enableVisionOpt);

            if (input != null)
            {
                await ExecuteAsync(input, output, format, quiet, enableVision, cancellationToken);
            }
        });
    }

    private static async Task ExecuteAsync(
        string input,
        string? output,
        string? format,
        bool quiet,
        bool enableVision,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(input))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] File not found: {input}");
            return;
        }

        // Determine output path and format
        output ??= Path.ChangeExtension(input, ".extracted.json");
        format ??= "json";

        IOutputWriter writer = format.ToLowerInvariant() switch
        {
            "json" => new JsonOutputWriter(),
            "jsonl" => new JsonLinesOutputWriter(),
            "markdown" or "md" => new MarkdownOutputWriter(),
            _ => new JsonOutputWriter()
        };

        // Ensure output has correct extension
        if (!output.EndsWith(writer.Extension, StringComparison.OrdinalIgnoreCase))
        {
            output = Path.ChangeExtension(output, writer.Extension);
        }

        if (!quiet)
        {
            AnsiConsole.MarkupLine($"[blue]FileFlux CLI - Extract[/]");
            AnsiConsole.MarkupLine($"  Input:  {input}");
            AnsiConsole.MarkupLine($"  Output: {output}");
            AnsiConsole.MarkupLine($"  Format: {format}");
            if (enableVision)
            {
                AnsiConsole.MarkupLine($"  Vision: [green]Enabled[/] (AI image extraction)");
            }
            AnsiConsole.WriteLine();
        }

        try
        {
            var chunks = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Extracting document...", async ctx =>
                {
                    // Create services with optional AI
                    var services = new ServiceCollection();

                    // Configure AI provider if vision is enabled
                    if (enableVision)
                    {
                        var config = new CliEnvironmentConfig();
                        var factory = new AIProviderFactory(config, enableVision: true);

                        if (config.HasAnyProvider())
                        {
                            ctx.Status($"Extracting document with AI vision ({factory.GetProviderStatus()})...");
                            factory.ConfigureServices(services);
                        }
                        else
                        {
                            AnsiConsole.MarkupLine("[yellow]Warning:[/] Vision enabled but no API key found. Set OPENAI_API_KEY environment variable.");
                            AnsiConsole.MarkupLine("[yellow]Falling back to basic extraction...[/]");
                        }
                    }

                    services.AddFileFlux();
                    using var provider = services.BuildServiceProvider();
                    var processor = provider.GetRequiredService<IDocumentProcessor>();

                    // Extract with basic options (no enrichment)
                    var options = new ChunkingOptions
                    {
                        Strategy = "FixedSize", // Simple extraction
                        MaxChunkSize = 100000,  // Large chunks for extraction
                        OverlapSize = 0
                    };

                    return await processor.ProcessAsync(input, options, cancellationToken);
                });

            // Write output
            await writer.WriteAsync(chunks, output, cancellationToken);

            if (!quiet)
            {
                var chunkList = chunks.ToList();
                AnsiConsole.MarkupLine($"[green]✓[/] Extracted {chunkList.Count} chunks");
                AnsiConsole.MarkupLine($"[green]✓[/] Saved to: {output}");

                // Show summary
                var totalChars = chunkList.Sum(c => c.Content.Length);
                var table = new Table();
                table.AddColumn("Metric");
                table.AddColumn("Value");
                table.AddRow("Total chunks", chunkList.Count.ToString());
                table.AddRow("Total characters", totalChars.ToString("N0"));
                table.AddRow("Average chunk size", (totalChars / chunkList.Count).ToString("N0"));

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
