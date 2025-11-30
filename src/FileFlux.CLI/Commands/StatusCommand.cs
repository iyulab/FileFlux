using FileFlux.CLI.Services;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reflection;

namespace FileFlux.CLI.Commands;

/// <summary>
/// Status command - display AI provider and configuration status
/// </summary>
public class StatusCommand : Command
{
    public StatusCommand() : base("status", "Display AI provider and configuration status")
    {
        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            await ExecuteAsync(cancellationToken);
        });
    }

    private static Task ExecuteAsync(CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine("[blue]FileFlux CLI - Status[/]\n");

        // Version info
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        AnsiConsole.MarkupLine($"[bold]Version:[/] {version}");
        AnsiConsole.MarkupLine($"[bold]Runtime:[/] {Environment.Version}");
        AnsiConsole.WriteLine();

        var config = new CliEnvironmentConfig();

        // Provider status table
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn("[bold]Provider[/]");
        table.AddColumn("[bold]Status[/]");
        table.AddColumn("[bold]Model[/]");
        table.AddColumn("[bold]API Key[/]");

        // OpenAI
        var openAIStatus = !string.IsNullOrEmpty(config.OpenAIApiKey);
        var openAIKeyDisplay = MaskApiKey(config.OpenAIApiKey);
        table.AddRow(
            "OpenAI",
            openAIStatus ? "[green]Configured[/]" : "[grey]Not configured[/]",
            config.OpenAIModel ?? "-",
            openAIKeyDisplay
        );

        // Anthropic
        var anthropicStatus = !string.IsNullOrEmpty(config.AnthropicApiKey);
        var anthropicKeyDisplay = MaskApiKey(config.AnthropicApiKey);
        table.AddRow(
            "Anthropic",
            anthropicStatus ? "[green]Configured[/]" : "[grey]Not configured[/]",
            config.AnthropicModel ?? "-",
            anthropicKeyDisplay
        );

        // GPU-Stack
        var gpuStackStatus = !string.IsNullOrEmpty(config.GpuStackApiKey);
        var gpuStackKeyDisplay = MaskApiKey(config.GpuStackApiKey);
        table.AddRow(
            "GPU-Stack",
            gpuStackStatus ? "[green]Configured[/]" : "[grey]Not configured[/]",
            config.GpuStackModel ?? "-",
            gpuStackKeyDisplay
        );

        // Google Gemini
        var googleStatus = !string.IsNullOrEmpty(config.GoogleApiKey);
        var googleKeyDisplay = MaskApiKey(config.GoogleApiKey);
        table.AddRow(
            "Google Gemini",
            googleStatus ? "[green]Configured[/]" : "[grey]Not configured[/]",
            config.GoogleModel ?? "-",
            googleKeyDisplay
        );

        AnsiConsole.MarkupLine("[bold]AI Providers:[/]");
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Active provider
        var activeProvider = config.DetectProvider();
        if (activeProvider == "ambiguous")
        {
            var providers = config.GetConfiguredProviders();
            AnsiConsole.MarkupLine($"[bold]Active Provider:[/] [red]Ambiguous[/]");
            AnsiConsole.MarkupLine($"[yellow]  Multiple API keys configured: {string.Join(", ", providers)}[/]");
            AnsiConsole.MarkupLine($"[yellow]  Set MODEL_PROVIDER environment variable to select one[/]");
        }
        else if (activeProvider != "none")
        {
            AnsiConsole.MarkupLine($"[bold]Active Provider:[/] [green]{activeProvider}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[bold]Active Provider:[/] [yellow]None (AI features disabled)[/]");
        }
        AnsiConsole.WriteLine();

        // Configuration table (showing effective values from env + config)
        var configTable = new Table();
        configTable.Border(TableBorder.Rounded);
        configTable.AddColumn("[bold]Setting[/]");
        configTable.AddColumn("[bold]Value[/]");
        configTable.AddColumn("[bold]Source[/]");

        var configManager = new ConfigManager();
        AddConfigRow(configTable, configManager, "MODEL_PROVIDER");
        AddConfigRow(configTable, configManager, "OPENAI_API_KEY");
        AddConfigRow(configTable, configManager, "OPENAI_MODEL");
        AddConfigRow(configTable, configManager, "ANTHROPIC_API_KEY");
        AddConfigRow(configTable, configManager, "ANTHROPIC_MODEL");
        AddConfigRow(configTable, configManager, "GPUSTACK_API_KEY");
        AddConfigRow(configTable, configManager, "GPUSTACK_ENDPOINT");
        AddConfigRow(configTable, configManager, "GPUSTACK_MODEL");
        AddConfigRow(configTable, configManager, "GOOGLE_API_KEY");
        AddConfigRow(configTable, configManager, "GOOGLE_MODEL");

        AnsiConsole.MarkupLine("[bold]Configuration:[/]");
        AnsiConsole.Write(configTable);
        AnsiConsole.WriteLine();

        // Config file location
        AnsiConsole.MarkupLine($"[grey]Config file: {ConfigManager.GetConfigPath()}[/]");
        AnsiConsole.WriteLine();

        // Supported formats
        AnsiConsole.MarkupLine("[bold]Supported Formats:[/]");
        AnsiConsole.MarkupLine("  [grey]PDF, DOCX, XLSX, PPTX, MD, TXT, JSON, CSV, HTML[/]");
        AnsiConsole.WriteLine();

        // Configuration help
        if (activeProvider == "none")
        {
            AnsiConsole.MarkupLine("[bold]Quick Setup:[/]");
            AnsiConsole.MarkupLine("  Set environment variable to enable AI features:");
            AnsiConsole.MarkupLine("  [yellow]  $env:OPENAI_API_KEY=\"sk-...\"[/]       (PowerShell)");
            AnsiConsole.MarkupLine("  [yellow]  $env:GOOGLE_API_KEY=\"AI...\"[/]        (PowerShell)");
            AnsiConsole.MarkupLine("  [yellow]  export OPENAI_API_KEY=\"sk-...\"[/]     (Bash)");
            AnsiConsole.MarkupLine("  Or use: [cyan]fileflux config set OPENAI_API_KEY sk-...[/]");
        }
        else if (activeProvider == "ambiguous")
        {
            AnsiConsole.MarkupLine("[bold]Resolution:[/]");
            AnsiConsole.MarkupLine("  Set MODEL_PROVIDER to select one:");
            AnsiConsole.MarkupLine("  [yellow]  $env:MODEL_PROVIDER=\"openai\"[/]       (PowerShell)");
            AnsiConsole.MarkupLine("  [yellow]  $env:MODEL_PROVIDER=\"anthropic\"[/]    (PowerShell)");
            AnsiConsole.MarkupLine("  [yellow]  $env:MODEL_PROVIDER=\"google\"[/]       (PowerShell)");
            AnsiConsole.MarkupLine("  Or use: [cyan]fileflux config set MODEL_PROVIDER openai[/]");
        }

        return Task.CompletedTask;
    }

    private static string MaskApiKey(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return "[grey]-[/]";

        if (apiKey.Length <= 8)
            return "[green]****[/]";

        return $"[green]{apiKey[..4]}...{apiKey[^4..]}[/]";
    }

    private static void AddConfigRow(Table table, ConfigManager configManager, string key)
    {
        var envValue = Environment.GetEnvironmentVariable(key);
        var configValue = configManager.Get(key);

        string displayValue;
        string source;

        if (!string.IsNullOrWhiteSpace(envValue))
        {
            // Environment variable takes priority
            displayValue = key.Contains("KEY", StringComparison.OrdinalIgnoreCase)
                ? MaskApiKey(envValue)
                : Markup.Escape(envValue);
            source = "[blue]env[/]";
        }
        else if (!string.IsNullOrWhiteSpace(configValue))
        {
            // Config file value
            displayValue = key.Contains("KEY", StringComparison.OrdinalIgnoreCase)
                ? MaskApiKey(configValue)
                : Markup.Escape(configValue);
            source = "[green]config[/]";
        }
        else
        {
            displayValue = "[grey]-[/]";
            source = "[grey]-[/]";
        }

        table.AddRow(key, displayValue, source);
    }
}
