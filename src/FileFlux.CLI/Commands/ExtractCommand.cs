using FileFlux;
using FileFlux.CLI.Output;
using FileFlux.Domain;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace FileFlux.CLI.Commands;

/// <summary>
/// Extract command - extract raw text/content from documents
/// </summary>
public class ExtractCommand : Command
{
    public ExtractCommand() : base("extract", "Extract raw text and content from document")
    {
        var inputArg = new Argument<string>("input", "Input file path");
        var outputOpt = new Option<string>(new[] { "-o", "--output" }, "Output file path (default: input.extracted.json)");
        var formatOpt = new Option<string>(new[] { "-f", "--format" }, () => "json", "Output format (json, jsonl, markdown)");
        var quietOpt = new Option<bool>(new[] { "-q", "--quiet" }, "Minimal output");

        AddArgument(inputArg);
        AddOption(outputOpt);
        AddOption(formatOpt);
        AddOption(quietOpt);

        this.SetHandler(async (InvocationContext context) =>
        {
            var input = context.ParseResult.GetValueForArgument(inputArg);
            var output = context.ParseResult.GetValueForOption(outputOpt);
            var format = context.ParseResult.GetValueForOption(formatOpt);
            var quiet = context.ParseResult.GetValueForOption(quietOpt);
            var cancellationToken = context.GetCancellationToken();

            await ExecuteAsync(input, output, format, quiet, cancellationToken);
        });
    }

    private static async Task ExecuteAsync(
        string input,
        string? output,
        string? format,
        bool quiet,
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
            AnsiConsole.WriteLine();
        }

        try
        {
            var chunks = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Extracting document...", async ctx =>
                {
                    // Create basic services without AI
                    var services = new ServiceCollection();
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
