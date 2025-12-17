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
            new RefineCommand(),
            new ChunkCommand(),
            new EnrichCommand(),
            new ProcessCommand(),
            new QACommand(),
            new EvaluateCommand(),
            new InfoCommand(),
            new StatusCommand(),
            new SetCommand(),
            new GetCommand()
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
        AnsiConsole.MarkupLine("[grey]Version 0.4.4[/]");
        AnsiConsole.WriteLine();

        var table = new Table();
        table.BorderStyle(new Style(Color.Grey));
        table.AddColumn("[bold]Command[/]");
        table.AddColumn("[bold]Description[/]");

        table.AddRow("extract", "Extract raw text from documents");
        table.AddRow("refine", "Clean/refine extracted content (headers, whitespace)");
        table.AddRow("chunk", "Intelligent chunking (optional: --refine, --enrich)");
        table.AddRow("enrich", "Add AI summaries and keywords to chunks");
        table.AddRow("process", "Complete 4-stage pipeline: Extract → Refine → Chunk → Enrich");
        table.AddRow("qa", "Generate QA pairs from document chunks");
        table.AddRow("evaluate", "Evaluate QA pairs for quality metrics");
        table.AddRow("info", "Display document information");
        table.AddRow("status", "Display AI provider and configuration status");
        table.AddRow("set", "Set a configuration value");
        table.AddRow("get", "Get configuration value(s)");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[yellow]Examples:[/]");
        AnsiConsole.MarkupLine("  [grey]fileflux process document.pdf --ai[/]       [dim]# Full pipeline with AI[/]");
        AnsiConsole.MarkupLine("  [grey]fileflux chunk document.pdf --refine[/]     [dim]# Extract → Refine → Chunk[/]");
        AnsiConsole.MarkupLine("  [grey]fileflux extract document.docx[/]           [dim]# Extract only[/]");
        AnsiConsole.MarkupLine("  [grey]fileflux refine extracted.json[/]           [dim]# Refine only[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Run 'fileflux status' for detailed configuration[/]");
    }
}
