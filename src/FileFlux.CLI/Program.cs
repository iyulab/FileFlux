using FileFlux.CLI.Commands;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text;

namespace FileFlux.CLI;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Set console encoding to UTF-8 for proper Unicode character display
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        // Create root command
        var rootCommand = new RootCommand("FileFlux CLI - Document processing for RAG systems")
        {
            new ExtractCommand(),
            new ChunkCommand(),
            new ProcessCommand(),
            new InfoCommand()
        };

        // Show banner if no args
        if (args.Length == 0)
        {
            ShowBanner();
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Use --help to see available commands[/]");
            return 0;
        }

        // Execute command
        try
        {
            var parseResult = rootCommand.Parse(args);
            return await parseResult.InvokeAsync();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Fatal error:[/] {ex.Message}");
            return 1;
        }
    }

    static void ShowBanner()
    {
        var banner = new FigletText("FileFlux")
        {
            Color = Color.Blue
        };

        AnsiConsole.Write(banner);

        AnsiConsole.MarkupLine("[grey]Document Processing CLI for RAG Systems[/]");
        AnsiConsole.MarkupLine("[grey]Version 0.3.17[/]");
        AnsiConsole.WriteLine();

        var table = new Table();
        table.BorderStyle(new Style(Color.Grey));
        table.AddColumn("[bold]Command[/]");
        table.AddColumn("[bold]Description[/]");

        table.AddRow("extract", "Extract raw text from documents");
        table.AddRow("chunk", "Intelligent chunking with optional AI enrichment");
        table.AddRow("process", "Complete pipeline (extract + chunk + enrich)");
        table.AddRow("info", "Display document information");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[yellow]Examples:[/]");
        AnsiConsole.MarkupLine("  [grey]fileflux process document.pdf -o output.json[/]");
        AnsiConsole.MarkupLine("  [grey]fileflux chunk document.pdf --enrich --strategy Auto[/]");
        AnsiConsole.MarkupLine("  [grey]fileflux extract document.docx -f markdown[/]");
        AnsiConsole.MarkupLine("  [grey]fileflux info document.pdf[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[yellow]Environment Variables:[/]");
        AnsiConsole.MarkupLine("  [grey]OPENAI_API_KEY      OpenAI API key[/]");
        AnsiConsole.MarkupLine("  [grey]OPENAI_MODEL        Model name (default: gpt-5-nano)[/]");
        AnsiConsole.MarkupLine("  [grey]ANTHROPIC_API_KEY   Anthropic API key[/]");
        AnsiConsole.MarkupLine("  [grey]ANTHROPIC_MODEL     Model name (default: claude-3-haiku-20240307)[/]");
    }
}
