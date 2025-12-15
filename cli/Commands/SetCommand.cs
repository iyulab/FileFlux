using FileFlux.CLI.Services;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace FileFlux.CLI.Commands;

/// <summary>
/// Set command - set configuration values
/// </summary>
public class SetCommand : Command
{
    public SetCommand() : base("set", "Set a configuration value")
    {
        var keyArg = new Argument<string>("key")
        {
            Description = "Configuration key (e.g., OPENAI_API_KEY, MODEL_PROVIDER)"
        };

        var valueArg = new Argument<string>("value")
        {
            Description = "Configuration value"
        };

        Arguments.Add(keyArg);
        Arguments.Add(valueArg);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var key = parseResult.GetValue(keyArg);
            var value = parseResult.GetValue(valueArg);

            if (key != null && value != null)
            {
                await ExecuteAsync(key, value);
            }
        });
    }

    private static Task ExecuteAsync(string key, string value)
    {
        // Validate key
        if (!ConfigManager.IsValidKey(key))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Unknown configuration key: {Markup.Escape(key)}");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Valid keys:[/]");
            foreach (var (k, desc) in ConfigManager.ValidKeys)
            {
                AnsiConsole.MarkupLine($"  [grey]{k,-20}[/] {desc}");
            }
            return Task.CompletedTask;
        }

        var configManager = new ConfigManager();

        // Mask sensitive values in output
        var displayValue = key.Contains("KEY", StringComparison.OrdinalIgnoreCase)
            ? MaskValue(value)
            : value;

        configManager.Set(key, value);
        AnsiConsole.MarkupLine($"[green]✓[/] Set [blue]{key.ToUpperInvariant()}[/] = {displayValue}");

        return Task.CompletedTask;
    }

    private static string MaskValue(string value)
    {
        if (value.Length <= 8)
            return "****";
        return $"{value[..4]}...{value[^4..]}";
    }
}
