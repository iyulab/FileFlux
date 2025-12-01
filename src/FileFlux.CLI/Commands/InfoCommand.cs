using FileFlux;
using FileFlux.Core;
using FileFlux.CLI.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace FileFlux.CLI.Commands;

/// <summary>
/// Info command - display document information
/// </summary>
public class InfoCommand : Command
{
    public InfoCommand() : base("info", "Display document information and metadata")
    {
        var inputArg = new Argument<string>("input")
        {
            Description = "Input file path"
        };

        Arguments.Add(inputArg);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var input = parseResult.GetValue(inputArg);
            if (input != null)
            {
                await ExecuteAsync(input, cancellationToken);
            }
        });
    }

    private static async Task ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        if (!File.Exists(input))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] File not found: {Markup.Escape(input)}");
            return;
        }

        var fileInfo = new FileInfo(input);

        AnsiConsole.MarkupLine($"[blue]FileFlux CLI - Info[/]\n");

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow("[bold]File path:[/]", input);
        grid.AddRow("[bold]File name:[/]", fileInfo.Name);
        grid.AddRow("[bold]File size:[/]", FormatFileSize(fileInfo.Length));
        grid.AddRow("[bold]Extension:[/]", fileInfo.Extension);
        grid.AddRow("[bold]Modified:[/]", fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));

        var panel = new Panel(grid)
        {
            Header = new PanelHeader("[yellow]File Information[/]"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        // Check if format is supported
        var supportedFormats = new[] { ".pdf", ".docx", ".xlsx", ".pptx", ".md", ".txt", ".json", ".csv", ".html", ".htm" };
        var isSupported = supportedFormats.Contains(fileInfo.Extension.ToLowerInvariant());

        AnsiConsole.MarkupLine($"[bold]Supported format:[/] {(isSupported ? "[green]Yes[/]" : "[red]No[/]")}");

        if (isSupported)
        {
            try
            {
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Analyzing document...", async ctx =>
                    {
                        var services = new ServiceCollection();
                        services.AddFileFlux();
                        using var provider = services.BuildServiceProvider();
                        var processor = provider.GetRequiredService<IDocumentProcessor>();

                        var chunks = await processor.ProcessAsync(input, new ChunkingOptions
                        {
                            Strategy = ChunkingStrategies.Token,
                            MaxChunkSize = 100000
                        }, cancellationToken);

                        var totalChars = chunks.Sum(c => c.Content.Length);
                        var estimatedWords = totalChars / 5; // rough estimate
                        var estimatedTokens = totalChars / 4; // rough estimate

                        AnsiConsole.WriteLine();

                        var contentGrid = new Grid();
                        contentGrid.AddColumn();
                        contentGrid.AddColumn();

                        contentGrid.AddRow("[bold]Total characters:[/]", totalChars.ToString("N0"));
                        contentGrid.AddRow("[bold]Estimated words:[/]", estimatedWords.ToString("N0"));
                        contentGrid.AddRow("[bold]Estimated tokens:[/]", estimatedTokens.ToString("N0"));
                        contentGrid.AddRow("[bold]Chunks (large):[/]", chunks.Count().ToString());

                        var contentPanel = new Panel(contentGrid)
                        {
                            Header = new PanelHeader("[yellow]Content Analysis[/]"),
                            Border = BoxBorder.Rounded
                        };

                        AnsiConsole.Write(contentPanel);
                    });
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"\n[red]Error analyzing document:[/] {ex.Message}");
            }
        }

        // Show environment config
        AnsiConsole.WriteLine();
        var config = new CliEnvironmentConfig();
        var factory = new AIProviderFactory(config);

        var envGrid = new Grid();
        envGrid.AddColumn();
        envGrid.AddColumn();

        envGrid.AddRow("[bold]AI Provider:[/]", factory.GetProviderStatus());

        if (factory.HasAIProvider())
        {
            var provider = config.DetectProvider();
            if (provider == "openai")
            {
                envGrid.AddRow("[bold]Model:[/]", config.OpenAIModel ?? "gpt-5-nano");
            }
            else if (provider == "anthropic")
            {
                envGrid.AddRow("[bold]Model:[/]", config.AnthropicModel ?? "claude-3-haiku-20240307");
            }
        }

        var envPanel = new Panel(envGrid)
        {
            Header = new PanelHeader("[yellow]Environment Configuration[/]"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(envPanel);
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}
