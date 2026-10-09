using FileFlux.CLI.Services;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace FileFlux.CLI.Commands;

/// <summary>
/// Get command - get configuration values
/// </summary>
public class GetCommand : Command
{
    public GetCommand() : base("get", "Get a configuration value")
    {
        var keyArg = new Argument<string?>("key")
        {
            Description = "Configuration key (omit to list all)",
            Arity = ArgumentArity.ZeroOrOne
        };

        Arguments.Add(keyArg);

        this.SetAction(parseResult => Execute(parseResult.GetValue(keyArg)));
    }

    private static int Execute(string? key)
    {
        var configManager = new ConfigManager();

        if (string.IsNullOrWhiteSpace(key))
        {
            // List all configuration
            ListAll(configManager);
            return ExitCodes.Success;
        }

        // Get specific key
        return GetSpecific(configManager, key);
    }

    private static void ListAll(ConfigManager configManager)
    {
        var config = configManager.GetAll();

        AnsiConsole.MarkupLine("[blue]FileFlux Configuration[/]");
        AnsiConsole.MarkupLine($"[grey]Path: {ConfigManager.GetConfigPath()}[/]");
        AnsiConsole.WriteLine();

        if (config.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No configuration set.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Use 'fileflux set <key> <value>' to configure.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Available keys:[/]");
            foreach (var (k, desc) in ConfigManager.ValidKeys)
            {
                AnsiConsole.MarkupLine($"  [grey]{k,-20}[/] {desc}");
            }
            return;
        }

        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn("[bold]Key[/]");
        table.AddColumn("[bold]Value[/]");
        table.AddColumn("[bold]Description[/]");

        foreach (var (k, v) in config.OrderBy(x => x.Key))
        {
            var displayValue = k.Contains("KEY", StringComparison.OrdinalIgnoreCase)
                ? MaskValue(v)
                : v;

            var desc = ConfigManager.ValidKeys.TryGetValue(k, out var d) ? d : "";
            table.AddRow(k, displayValue, $"[grey]{desc}[/]");
        }

        AnsiConsole.Write(table);
    }

    private static int GetSpecific(ConfigManager configManager, string key)
    {
        var normalizedKey = key.ToUpperInvariant().Replace("-", "_");

        if (!ConfigManager.IsValidKey(key))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Unknown configuration key: {Markup.Escape(key)}");
            return ExitCodes.Failure;
        }

        var value = configManager.Get(key);

        if (value == null)
        {
            AnsiConsole.MarkupLine($"[yellow]{normalizedKey}[/] is not set");

            // Check if environment variable is set
            var envValue = Environment.GetEnvironmentVariable(normalizedKey);
            if (!string.IsNullOrEmpty(envValue))
            {
                var displayEnv = normalizedKey.Contains("KEY")
                    ? MaskValue(envValue)
                    : envValue;
                AnsiConsole.MarkupLine($"[grey]  (environment: {displayEnv})[/]");
            }
        }
        else
        {
            var displayValue = normalizedKey.Contains("KEY")
                ? MaskValue(value)
                : value;
            AnsiConsole.MarkupLine($"[blue]{normalizedKey}[/] = {displayValue}");
        }

        return ExitCodes.Success;
    }

    private static string MaskValue(string value)
    {
        if (value.Length <= 8)
            return "****";
        return $"{value[..4]}...{value[^4..]}";
    }
}
